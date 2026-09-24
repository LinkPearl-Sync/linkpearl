using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>Un dépôt à faire dans la boîte personnelle du candidat, sur ces services.</summary>
/// <remarks>Le candidat est désigné par nom et monde : c'est l'adaptateur qui en tire l'adresse.</remarks>
public sealed record AdmissionOutbound(string CharacterName, ushort WorldId, IReadOnlyList<RendezvousAddress> Via, byte[] Payload);

/// <summary>Une demande qui attend qu'un modérateur tranche.</summary>
public sealed record PendingValidation(
    GroupId Group, string GroupName, byte[] Nonce, string CharacterName, ushort WorldId, DateTimeOffset LastSeen);

/// <summary>
/// Le côté membre de l'admission.
/// </summary>
/// <remarks>
/// Appelé depuis le fil qui lit les boîtes et depuis l'interface : tout passe
/// sous un verrou. Le verrou du carnet de groupes est pris après le nôtre,
/// jamais l'inverse.
/// </remarks>
public sealed class AdmissionHost(GroupBook book, Func<byte[]?> ourIdentityKey, IClock clock) : IDisposable
{
    /// <summary>Au-delà, deviner le mot de passe au hasard ne vaut plus rien.</summary>
    public const int MaxFailures = 5;

    /// <summary>Le temps pour un candidat de répondre à un défi.</summary>
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Une demande en validation disparaît si elle n'est plus redéposée.
    /// </summary>
    /// <remarks>Le candidat redépose chaque minute : trois minutes de silence veulent dire qu'il a abandonné.</remarks>
    public static readonly TimeSpan ValidationSilence = TimeSpan.FromMinutes(3);

    /// <summary>Combien de temps on se souvient d'avoir déjà répondu à une demande.</summary>
    private static readonly TimeSpan AnswerMemory = TimeSpan.FromMinutes(15);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Challenge> _challenges = [];
    private readonly Dictionary<string, Waiting> _pending = [];
    private readonly Dictionary<string, DateTimeOffset> _answered = [];
    private readonly Dictionary<PeerId, (int Count, long Window)> _failures = [];

    private sealed record Challenge(GroupId Group, AdmissionRequest Request, ECDiffieHellman Ephemeral, DateTimeOffset Created);

    private sealed record Waiting(GroupId Group, AdmissionRequest Request, DateTimeOffset LastSeen);

    public IReadOnlyList<PendingValidation> Pending
    {
        get
        {
            lock (_gate)
            {
                Prune();

                return [.. _pending.Values
                    .OrderBy(waiting => waiting.LastSeen)
                    .Select(waiting => new PendingValidation(
                        waiting.Group, book.Find(waiting.Group)?.Name ?? "?", waiting.Request.Nonce,
                        waiting.Request.CharacterName, waiting.Request.WorldId, waiting.LastSeen))];
            }
        }
    }

    /// <summary>Les codes dont on ouvre la boîte d'admission : ceux des groupes qu'on peut admettre.</summary>
    /// <remarks>
    /// En mode validation, un simple membre n'y peut rien : il n'ouvre pas la
    /// boîte, et ne fait pas payer au service des dépôts qu'il jetterait.
    /// </remarks>
    public IReadOnlyList<byte[]> AdmissionCodes
        => [.. book.All.Where(CanAdmit).Select(group => group.Policy!.Code)];

    public IReadOnlyList<AdmissionOutbound> OnRequest(AdmissionRequest request, PlayerFingerprint candidate)
    {
        var group = Admitting(request.Code);

        if (group?.Policy is not { } policy)
            return [];

        var ours = ourIdentityKey();

        if (ours is not null && ours.AsSpan().SequenceEqual(request.PublicKey))
            return [];

        if (policy.IsBanned(PeerId.Of(request.PublicKey), candidate))
            return [];

        var key = Convert.ToHexStringLower(request.Nonce);

        lock (_gate)
        {
            Prune();

            if (_answered.ContainsKey(key))
                return [];

            if (policy.Attestation.Admission == AdmissionMode.Validation)
            {
                _pending[key] = new Waiting(group.Id, request, clock.UtcNow);
                return [];
            }

            if (_challenges.ContainsKey(key))
                return [];

            var ephemeral = CryptoPrimitives.GenerateEphemeral();
            _challenges[key] = new Challenge(group.Id, request, ephemeral, clock.UtcNow);

            var challenge = new AdmissionChallenge(request.Nonce, CryptoPrimitives.ExportPublicPoint(ephemeral));
            return [new AdmissionOutbound(request.CharacterName, request.WorldId, group.Rendezvous, AdmissionCodec.Encode(challenge))];
        }
    }

    public IReadOnlyList<AdmissionOutbound> OnProof(AdmissionProof proof)
    {
        var key = Convert.ToHexStringLower(proof.Nonce);
        Challenge? challenge;

        lock (_gate)
        {
            Prune();

            // Plusieurs membres défient le même candidat : la preuve désigne
            // celui qu'il a retenu par son éphémère, les autres l'ignorent.
            if (_challenges.TryGetValue(key, out challenge) is false
                || CryptoPrimitives.ExportPublicPoint(challenge.Ephemeral).AsSpan().SequenceEqual(proof.MemberEphemeral) is false)
                return [];

            _challenges.Remove(key);
            _answered[key] = clock.UtcNow;
        }

        using (challenge.Ephemeral)
        {
            // Le groupe vient de l'aléa du défi, jamais de proof.Code : ce
            // dernier ne figure dans aucune donnée associée, donc un porteur
            // du réseau peut le modifier en route sans invalider l'étiquette.
            // Le défi, lui, a été créé par OnRequest sur la base du code lu à
            // ce moment-là dans Admitting : le groupe qu'il désigne est déjà
            // celui pour lequel le candidat prouve tenir le mot de passe, sans
            // qu'il soit besoin ni utile de revérifier proof.Code (voir le
            // rapport de tâche pour la discussion de ce choix).
            var group = book.Find(challenge.Group);

            if (group?.Policy is not { Dissolved: false } policy)
                return [];

            var request = challenge.Request;
            var candidate = PeerId.Of(request.PublicKey);
            var window = MailboxAddress.IndexAt(clock.UtcNow);

            lock (_gate)
            {
                if (_failures.TryGetValue(candidate, out var failures) && failures.Window == window && failures.Count >= MaxFailures)
                    return [Refuse(request, group, RefusalReason.TooManyAttempts)];
            }

            var associated = AdmissionSealing.Associated(AdmissionKind.Proof, request.Nonce, proof.MemberEphemeral);
            var expected = AdmissionSealing.ProofTag(challenge.Ephemeral, request.Ephemeral, request.Nonce, policy.Password, associated);

            if (CryptographicOperations.FixedTimeEquals(expected, proof.Tag) is false)
            {
                lock (_gate)
                {
                    var previous = _failures.TryGetValue(candidate, out var f) && f.Window == window ? f.Count : 0;
                    _failures[candidate] = (previous + 1, window);
                }

                return [Refuse(request, group, RefusalReason.WrongPassword)];
            }

            return [Welcome(request, group, challenge.Ephemeral)];
        }
    }

    public AdmissionOutbound? Approve(byte[] nonce)
    {
        if (TakePending(nonce) is not { } waiting || book.Find(waiting.Group) is not { Policy.Dissolved: false } group)
            return null;

        using var ephemeral = CryptoPrimitives.GenerateEphemeral();
        return Welcome(waiting.Request, group, ephemeral);
    }

    public AdmissionOutbound? Decline(byte[] nonce)
    {
        if (TakePending(nonce) is not { } waiting || book.Find(waiting.Group) is not { } group)
            return null;

        return Refuse(waiting.Request, group, RefusalReason.Declined);
    }

    private Waiting? TakePending(byte[] nonce)
    {
        var key = Convert.ToHexStringLower(nonce);

        lock (_gate)
        {
            if (_pending.Remove(key, out var waiting) is false)
                return null;

            _answered[key] = clock.UtcNow;
            return waiting;
        }
    }

    private GroupRecord? Admitting(byte[] code)
        => book.All.FirstOrDefault(group => CanAdmit(group) && group.Policy!.Code.AsSpan().SequenceEqual(code));

    private bool CanAdmit(GroupRecord group)
        => group is { OwnerKey: not null, Policy: { Dissolved: false } policy }
           && (policy.Attestation.Admission == AdmissionMode.Password
               || GroupGovernance.RoleOf(group, ourIdentityKey()) is not GroupRole.Member);

    private static AdmissionOutbound Welcome(AdmissionRequest request, GroupRecord group, ECDiffieHellman ephemeral)
    {
        var memberPoint = CryptoPrimitives.ExportPublicPoint(ephemeral);
        var key = AdmissionSealing.Key(ephemeral, request.Ephemeral, request.Nonce, AdmissionSealing.Welcome);
        var grant = GroupGrantCodec.Encode(new GroupGrant(group.Id, group.Secret, group.OwnerKey!, group.Name));
        var sealedGrant = AdmissionSealing.Seal(key, grant, AdmissionSealing.Associated(AdmissionKind.Welcome, request.Nonce, memberPoint));

        return new AdmissionOutbound(
            request.CharacterName, request.WorldId, group.Rendezvous,
            AdmissionCodec.Encode(new AdmissionWelcome(request.Nonce, memberPoint, sealedGrant)));
    }

    private static AdmissionOutbound Refuse(AdmissionRequest request, GroupRecord group, byte reason)
        => new(request.CharacterName, request.WorldId, group.Rendezvous, AdmissionCodec.Encode(new AdmissionRefusal(request.Nonce, reason)));

    private void Prune()
    {
        var now = clock.UtcNow;

        foreach (var (key, challenge) in _challenges.ToList())
        {
            if (now - challenge.Created <= ChallengeLifetime)
                continue;

            challenge.Ephemeral.Dispose();
            _challenges.Remove(key);
        }

        foreach (var (key, waiting) in _pending.ToList())
            if (now - waiting.LastSeen > ValidationSilence)
                _pending.Remove(key);

        foreach (var (key, at) in _answered.ToList())
            if (now - at > AnswerMemory)
                _answered.Remove(key);

        var window = MailboxAddress.IndexAt(now);

        foreach (var (candidate, failures) in _failures.ToList())
            if (failures.Window != window)
                _failures.Remove(candidate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var challenge in _challenges.Values)
                challenge.Ephemeral.Dispose();

            _challenges.Clear();
        }
    }
}
