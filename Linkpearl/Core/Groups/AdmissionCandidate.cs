using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>Où en est une candidature. Strictement local.</summary>
public enum CandidacyState
{
    Idle,

    /// <summary>Demande déposée, en attente d'un défi, d'un modérateur ou d'une réponse.</summary>
    Waiting,

    /// <summary>Un membre a défié, mais nous n'avons pas de mot de passe à lui donner.</summary>
    NeedsPassword,

    Proving,
    Joined,
    Refused,

    /// <summary>Dix minutes sans réponse : personne d'autorisé n'était en ligne.</summary>
    Expired,
}

/// <summary>
/// Le côté candidat de l'admission. Une seule candidature à la fois.
/// </summary>
/// <remarks>
/// Appelé depuis le fil qui lit les boîtes, la boucle de rafraîchissement et
/// l'interface : tout passe sous un verrou.
/// </remarks>
public sealed class AdmissionCandidate(IClock clock) : IDisposable
{
    /// <summary>Les boîtes ne gardent rien : un modérateur qui se connecte doit pouvoir voir la demande.</summary>
    public static readonly TimeSpan RedepositInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private ECDiffieHellman? _ephemeral;
    private AdmissionRequest? _request;
    private GroupGrant? _grant;
    private string _password = "";
    private CandidacyState _state = CandidacyState.Idle;
    private DateTimeOffset _started;
    private DateTimeOffset _lastSent;

    public CandidacyState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public byte RefusalReason { get; private set; }

    public InvitationTicket? Code { get; private set; }

    public RendezvousAddress? Service { get; private set; }

    /// <summary>Le nom du dernier groupe rejoint, pour que l'interface puisse le dire.</summary>
    public string? LastJoinedName { get; private set; }

    /// <summary>Commence une candidature et rend la demande à déposer.</summary>
    /// <param name="identityKey">Notre clé d'identité, point de 65 octets.</param>
    public byte[] Start(
        InvitationTicket code, RendezvousAddress service, string password, byte[] identityKey,
        string characterName, ushort worldId)
    {
        lock (_gate)
        {
            Reset();

            // Un éphémère et un aléa neufs à chaque candidature : deux essais
            // successifs de mot de passe (par exemple après un refus) ne
            // réutilisent donc jamais le même accord de clés.
            _ephemeral = CryptoPrimitives.GenerateEphemeral();
            _request = new AdmissionRequest(
                code.ToBytes(), identityKey, CryptoPrimitives.ExportPublicPoint(_ephemeral),
                RandomNumberGenerator.GetBytes(AdmissionCodec.NonceLength), worldId, characterName);

            _password = password;
            Code = code;
            Service = service;
            LastJoinedName = null;
            _state = CandidacyState.Waiting;
            _started = _lastSent = clock.UtcNow;

            return AdmissionCodec.Encode(_request);
        }
    }

    /// <summary>La demande à redéposer, si c'est le moment.</summary>
    public byte[]? DueRedeposit()
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Waiting || _request is null)
                return null;

            var now = clock.UtcNow;

            if (now - _started >= Lifetime)
            {
                _state = CandidacyState.Expired;
                DropEphemeral();
                return null;
            }

            if (now - _lastSent < RedepositInterval)
                return null;

            _lastSent = now;
            return AdmissionCodec.Encode(_request);
        }
    }

    /// <summary>Répond au premier défi reçu par la preuve à déposer.</summary>
    public byte[]? OnChallenge(AdmissionChallenge challenge)
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Waiting || _request is null || _ephemeral is null
                || challenge.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return null;

            if (_password.Length == 0)
            {
                _state = CandidacyState.NeedsPassword;
                return null;
            }

            // L'étiquette, pas le mot de passe scellé : l'accord ECDH est
            // symétrique, donc le membre la recalcule de son côté avec son
            // propre éphémère et le nôtre (voir AdmissionSealing.ProofTag).
            var associated = AdmissionSealing.Associated(AdmissionKind.Proof, _request.Nonce, challenge.MemberEphemeral);
            var tag = AdmissionSealing.ProofTag(_ephemeral, challenge.MemberEphemeral, _request.Nonce, _password, associated);

            _state = CandidacyState.Proving;
            return AdmissionCodec.Encode(new AdmissionProof(_request.Code, _request.Nonce, challenge.MemberEphemeral, tag));
        }
    }

    public bool OnWelcome(AdmissionWelcome welcome)
    {
        lock (_gate)
        {
            if (_state is not (CandidacyState.Waiting or CandidacyState.Proving) || _request is null || _ephemeral is null
                || welcome.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return false;

            var key = AdmissionSealing.Key(_ephemeral, welcome.MemberEphemeral, _request.Nonce, AdmissionSealing.Welcome);

            if (AdmissionSealing.TryOpen(key, welcome.SealedGrant,
                    AdmissionSealing.Associated(AdmissionKind.Welcome, _request.Nonce, welcome.MemberEphemeral), out var plain) is false
                || GroupGrantCodec.TryDecode(plain, out var grant, out _) is false)
                return false;

            _grant = grant;
            _state = CandidacyState.Joined;
            DropEphemeral();
            return true;
        }
    }

    public void OnRefusal(AdmissionRefusal refusal)
    {
        lock (_gate)
        {
            if (_state is not (CandidacyState.Waiting or CandidacyState.Proving) || _request is null
                || refusal.Nonce.AsSpan().SequenceEqual(_request.Nonce) is false)
                return;

            _state = CandidacyState.Refused;
            RefusalReason = refusal.Reason;
            DropEphemeral();
        }
    }

    /// <summary>Le groupe rejoint, une seule fois ; la candidature revient au repos.</summary>
    /// <remarks>Sans politique : elle arrive par la première session avec un membre.</remarks>
    public GroupRecord? TakeJoined(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state is not CandidacyState.Joined || _grant is not { } grant || Service is not { } service)
                return null;

            Reset();
            LastJoinedName = grant.Name;

            return new GroupRecord
            {
                Id = grant.Group,
                Name = grant.Name,
                Secret = grant.Secret,
                OwnerKey = grant.OwnerKey,
                Rendezvous = [service],
                JoinedAt = now,
            };
        }
    }

    public void Cancel()
    {
        lock (_gate)
            Reset();
    }

    private void Reset()
    {
        DropEphemeral();
        _request = null;
        _grant = null;
        _password = "";
        _state = CandidacyState.Idle;
        RefusalReason = 0;
    }

    private void DropEphemeral()
    {
        _ephemeral?.Dispose();
        _ephemeral = null;
    }

    public void Dispose()
    {
        lock (_gate)
            DropEphemeral();
    }
}
