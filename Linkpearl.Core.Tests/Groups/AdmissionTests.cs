using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class AdmissionTests : IDisposable
{
    private readonly ECDsa _owner = CryptoPrimitives.GenerateIdentity();
    private readonly ECDsa _candidateIdentity = CryptoPrimitives.GenerateIdentity();
    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        _owner.Dispose();
        _candidateIdentity.Dispose();

        foreach (var disposable in _disposables)
            disposable.Dispose();
    }

    private static readonly PlayerFingerprint Candidate = PlayerFingerprint.Of("jhalen tavari", 21);

    private byte[] OwnerPoint => CryptoPrimitives.ExportPublicPoint(_owner);
    private byte[] CandidatePoint => CryptoPrimitives.ExportPublicPoint(_candidateIdentity);

    /// <summary>Un membre qui tient le groupe, et son hôte.</summary>
    private (AdmissionHost Host, GroupBook Book, CreatedGroup Created) Member(string password = "lune", bool asOwner = false)
    {
        var created = GroupGovernance.Create("Compagnie", password, PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([asOwner ? created.Record : created.Record with { SigningKey = null }]);

        using var stranger = CryptoPrimitives.GenerateIdentity();
        var ours = asOwner ? OwnerPoint : CryptoPrimitives.ExportPublicPoint(stranger);
        var host = new AdmissionHost(book, () => ours, _clock);
        _disposables.Add(host);
        return (host, book, created);
    }

    private AdmissionCandidate NewCandidate()
    {
        var candidate = new AdmissionCandidate(_clock);
        _disposables.Add(candidate);
        return candidate;
    }

    private static T Decode<T>(byte[] payload) where T : AdmissionMessage
    {
        Assert.True(AdmissionCodec.TryDecode(payload, out var message, out var why), why);
        return Assert.IsType<T>(message);
    }

    private byte[] Start(AdmissionCandidate candidate, CreatedGroup created, string password)
        => candidate.Start(created.Code, PolicyFixture.Service, password, CandidatePoint, "Jhalen Tavari", 21);

    [Fact]
    public void Le_bon_mot_de_passe_fait_entrer()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();

        var request = Decode<AdmissionRequest>(Start(candidate, created, "lune"));
        var challenge = Assert.Single(host.OnRequest(request, Candidate));
        Assert.Equal("Jhalen Tavari", challenge.CharacterName);
        Assert.Equal([PolicyFixture.Service], challenge.Via);

        var proof = candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload));
        Assert.Equal(CandidacyState.Proving, candidate.State);

        var welcome = Assert.Single(host.OnProof(Decode<AdmissionProof>(proof!)));
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome.Payload)));
        Assert.Equal(CandidacyState.Joined, candidate.State);

        var record = candidate.TakeJoined(_clock.UtcNow);
        Assert.Equal(created.Record.Id, record!.Id);
        Assert.Equal(created.Record.Secret, record.Secret);
        Assert.Equal(created.Record.OwnerKey, record.OwnerKey);
        Assert.Equal([PolicyFixture.Service], record.Rendezvous);
        Assert.Null(record.Policy);
        Assert.Equal(CandidacyState.Idle, candidate.State);
        Assert.Equal("Compagnie", candidate.LastJoinedName);
    }

    [Fact]
    public void Un_mauvais_mot_de_passe_est_refuse_puis_le_sixieme_essai_est_bloque()
    {
        var (host, _, created) = Member();

        for (var attempt = 1; attempt <= AdmissionHost.MaxFailures + 1; attempt++)
        {
            var candidate = NewCandidate();
            var request = Decode<AdmissionRequest>(Start(candidate, created, "soleil"));
            var challenge = Assert.Single(host.OnRequest(request, Candidate));
            var proof = candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload));
            var refusal = Decode<AdmissionRefusal>(Assert.Single(host.OnProof(Decode<AdmissionProof>(proof!))).Payload);

            candidate.OnRefusal(refusal);
            Assert.Equal(CandidacyState.Refused, candidate.State);
            Assert.Equal(
                attempt <= AdmissionHost.MaxFailures ? RefusalReason.WrongPassword : RefusalReason.TooManyAttempts,
                refusal.Reason);
        }
    }

    [Fact]
    public void Un_candidat_banni_ne_recoit_aucune_reponse()
    {
        var created = GroupGovernance.Create("Compagnie", "lune", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([created.Record]);
        Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(created.Record.Id,
            GroupGovernance.Ban(created.Record, new GroupBan(null, Candidate), null)));

        using var host = new AdmissionHost(book, () => OwnerPoint, _clock);
        var request = Decode<AdmissionRequest>(Start(NewCandidate(), created, "lune"));

        Assert.Empty(host.OnRequest(request, Candidate));
    }

    [Fact]
    public void Une_demande_redeposee_ne_redefie_pas()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, "lune"));

        Assert.Single(host.OnRequest(request, Candidate));
        Assert.Empty(host.OnRequest(request, Candidate));
    }

    [Fact]
    public void En_validation_la_demande_attend_un_moderateur()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, ""));

        Assert.Empty(host.OnRequest(request, Candidate));
        var pending = Assert.Single(host.Pending);
        Assert.Equal("Jhalen Tavari", pending.CharacterName);
        Assert.Equal("Compagnie", pending.GroupName);

        var welcome = host.Approve(pending.Nonce);
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome!.Payload)));
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void En_validation_un_simple_membre_ne_voit_rien()
    {
        var (host, _, created) = Member(password: "");
        var request = Decode<AdmissionRequest>(Start(NewCandidate(), created, ""));

        Assert.Empty(host.OnRequest(request, Candidate));
        Assert.Empty(host.Pending);
        Assert.Empty(host.AdmissionCodes);
    }

    [Fact]
    public void Une_demande_refusee_ne_revient_pas()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        var candidate = NewCandidate();
        var request = Decode<AdmissionRequest>(Start(candidate, created, ""));

        host.OnRequest(request, Candidate);
        var refusal = host.Decline(Assert.Single(host.Pending).Nonce);
        candidate.OnRefusal(Decode<AdmissionRefusal>(refusal!.Payload));

        Assert.Equal(CandidacyState.Refused, candidate.State);
        Assert.Equal(RefusalReason.Declined, candidate.RefusalReason);

        // Le candidat redépose une minute plus tard, avant d'avoir lu le refus.
        _clock.Advance(TimeSpan.FromMinutes(1));
        host.OnRequest(request, Candidate);
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void Une_demande_qui_n_est_plus_redeposee_disparait()
    {
        var (host, _, created) = Member(password: "", asOwner: true);
        host.OnRequest(Decode<AdmissionRequest>(Start(NewCandidate(), created, "")), Candidate);
        Assert.Single(host.Pending);

        _clock.Advance(AdmissionHost.ValidationSilence + TimeSpan.FromSeconds(1));
        Assert.Empty(host.Pending);
    }

    [Fact]
    public void Le_candidat_redepose_chaque_minute_puis_abandonne()
    {
        var (_, _, created) = Member();
        var candidate = NewCandidate();
        Start(candidate, created, "lune");

        Assert.Null(candidate.DueRedeposit());
        _clock.Advance(AdmissionCandidate.RedepositInterval);
        Assert.NotNull(candidate.DueRedeposit());
        Assert.Null(candidate.DueRedeposit());

        _clock.Advance(AdmissionCandidate.Lifetime);
        Assert.Null(candidate.DueRedeposit());
        Assert.Equal(CandidacyState.Expired, candidate.State);
    }

    [Fact]
    public void Sans_mot_de_passe_un_defi_demande_d_en_saisir_un()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();
        var challenge = Assert.Single(host.OnRequest(Decode<AdmissionRequest>(Start(candidate, created, "")), Candidate));

        Assert.Null(candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload)));
        Assert.Equal(CandidacyState.NeedsPassword, candidate.State);
    }

    [Fact]
    public void Un_defi_d_une_autre_demande_est_ignore()
    {
        var (host, _, created) = Member();
        var first = NewCandidate();
        var other = NewCandidate();
        Start(first, created, "lune");
        var otherChallenge = Assert.Single(host.OnRequest(Decode<AdmissionRequest>(Start(other, created, "lune")), Candidate));

        Assert.Null(first.OnChallenge(Decode<AdmissionChallenge>(otherChallenge.Payload)));
        Assert.Equal(CandidacyState.Waiting, first.State);
    }

    [Fact]
    public void Un_groupe_dissous_n_admet_plus()
    {
        var created = GroupGovernance.Create("Compagnie", "lune", PolicyFixture.Service, OwnerPoint, _clock.UtcNow);
        var book = new GroupBook(_clock);
        book.Load([created.Record]);
        Assert.Equal(PolicyOffer.Adopted, book.OfferPolicy(created.Record.Id, GroupGovernance.Dissolve(created.Record)));

        using var host = new AdmissionHost(book, () => OwnerPoint, _clock);

        Assert.Empty(host.OnRequest(Decode<AdmissionRequest>(Start(NewCandidate(), created, "lune")), Candidate));
        Assert.Empty(host.AdmissionCodes);
    }

    [Fact]
    public void Un_mot_de_passe_normalise_differemment_donne_la_meme_etiquette()
    {
        // « café » composé (NFC, un seul point de code pour le é) côté
        // candidat, et décomposé (NFD, e + accent combinant) côté membre :
        // deux séquences d'octets UTF-8 différentes pour le même mot lu.
        const string composed = "café";
        const string decomposed = "café";
        Assert.NotEqual(composed, decomposed);

        var (host, _, created) = Member(password: decomposed);
        var candidate = NewCandidate();

        var request = Decode<AdmissionRequest>(Start(candidate, created, composed));
        var challenge = Assert.Single(host.OnRequest(request, Candidate));
        var proof = candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload));

        var welcome = Assert.Single(host.OnProof(Decode<AdmissionProof>(proof!)));
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome.Payload)));
        Assert.Equal(CandidacyState.Joined, candidate.State);
    }

    [Fact]
    public void Un_code_de_preuve_modifie_en_route_est_verifie_contre_le_groupe_de_la_demande()
    {
        // Le groupe vient de l'aléa du défi (Challenge.Group), jamais du
        // proof.Code en clair : le modifier en route ne doit ni égarer la
        // vérification vers un autre groupe, ni faire échouer une preuve dont
        // l'étiquette est par ailleurs valide.
        var (host, _, created) = Member();
        var candidate = NewCandidate();

        var request = Decode<AdmissionRequest>(Start(candidate, created, "lune"));
        var challenge = Assert.Single(host.OnRequest(request, Candidate));
        var proof = Decode<AdmissionProof>(candidate.OnChallenge(Decode<AdmissionChallenge>(challenge.Payload))!);

        var tamperedCode = new byte[proof.Code.Length];
        Array.Fill(tamperedCode, (byte)0xff);
        var tampered = proof with { Code = tamperedCode };

        var welcome = Assert.Single(host.OnProof(tampered));
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome.Payload)));
        Assert.Equal(CandidacyState.Joined, candidate.State);
    }

    [Fact]
    public void Deux_essais_successifs_utilisent_chacun_un_ephemere_et_un_alea_neufs()
    {
        var (host, _, created) = Member();
        var candidate = NewCandidate();

        var firstRequest = Decode<AdmissionRequest>(Start(candidate, created, "soleil"));
        var firstChallenge = Assert.Single(host.OnRequest(firstRequest, Candidate));
        var firstProof = candidate.OnChallenge(Decode<AdmissionChallenge>(firstChallenge.Payload));
        var refusal = Decode<AdmissionRefusal>(Assert.Single(host.OnProof(Decode<AdmissionProof>(firstProof!))).Payload);
        candidate.OnRefusal(refusal);
        Assert.Equal(CandidacyState.Refused, candidate.State);

        var secondRequest = Decode<AdmissionRequest>(Start(candidate, created, "lune"));

        Assert.NotEqual(firstRequest.Nonce, secondRequest.Nonce);
        Assert.NotEqual(firstRequest.Ephemeral, secondRequest.Ephemeral);

        var secondChallenge = Assert.Single(host.OnRequest(secondRequest, Candidate));
        var secondProof = candidate.OnChallenge(Decode<AdmissionChallenge>(secondChallenge.Payload));
        var welcome = Assert.Single(host.OnProof(Decode<AdmissionProof>(secondProof!)));
        Assert.True(candidate.OnWelcome(Decode<AdmissionWelcome>(welcome.Payload)));
        Assert.Equal(CandidacyState.Joined, candidate.State);
    }
}
