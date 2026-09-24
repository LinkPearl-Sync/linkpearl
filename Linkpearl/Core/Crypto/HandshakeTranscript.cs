using System.Security.Cryptography;

namespace Linkpearl.Core.Crypto;

/// <summary>Calculs communs aux deux côtés du handshake.</summary>
internal static class HandshakeTranscript
{
    public static byte[] First(ReadOnlySpan<byte> message1, ReadOnlySpan<byte> message2Clear)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(HandshakeFormat.Prologue);
        sha.AppendData(message1);
        sha.AppendData(message2Clear);
        return sha.GetHashAndReset();
    }

    public static byte[] Second(ReadOnlySpan<byte> transcript1, ReadOnlySpan<byte> sealedMessage2)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(transcript1);
        sha.AppendData(sealedMessage2);
        return sha.GetHashAndReset();
    }

    /// <summary>
    /// Assemble identité, signature et liaison, puis scelle le tout.
    /// </summary>
    /// <remarks>
    /// La liaison est un HMAC de l'identité sur le transcript, sous une clé
    /// dérivée du secret d'accord. Le chiffrement authentifié suffirait déjà à
    /// lier les deux, c'est une ceinture par-dessus une bretelle, mais elle
    /// coûte trente-deux octets une fois par session et ferme explicitement
    /// l'attaque par relais d'identité.
    /// </remarks>
    public static byte[] SealAuthentication(
        ECDsa identity, ReadOnlySpan<byte> signatureContext, ReadOnlySpan<byte> transcript,
        ReadOnlySpan<byte> sealingKey, ReadOnlySpan<byte> bindingKey)
    {
        var publicPoint = CryptoPrimitives.ExportPublicPoint(identity);
        var signature = CryptoPrimitives.Sign(identity, Concat(signatureContext, transcript));
        var binding = Bind(bindingKey, publicPoint, transcript);

        var payload = new byte[HandshakeFormat.AuthenticationLength];
        publicPoint.CopyTo(payload.AsSpan());
        signature.CopyTo(payload.AsSpan(CryptoPrimitives.PublicPointLength));
        binding.CopyTo(payload.AsSpan(CryptoPrimitives.PublicPointLength + CryptoPrimitives.SignatureLength));

        return CryptoPrimitives.Seal(sealingKey, HandshakeFormat.HandshakeNonce, payload, transcript);
    }

    /// <summary>
    /// Ouvre l'authentification du correspondant et décide s'il est admis.
    /// </summary>
    /// <remarks>
    /// <paramref name="isAuthorized"/> n'est appelé qu'une fois la signature et
    /// la liaison vérifiées, donc seulement pour une clé dont le correspondant a
    /// prouvé détenir la partie privée. Ce contrat compte : l'admission d'un
    /// membre de groupe épingle la clé qu'on lui passe.
    /// </remarks>
    public static bool TryOpenAuthentication(
        ReadOnlySpan<byte> sealedPayload, ReadOnlySpan<byte> signatureContext, ReadOnlySpan<byte> transcript,
        ReadOnlySpan<byte> sealingKey, ReadOnlySpan<byte> bindingKey,
        Func<byte[], bool> isAuthorized, out byte[] peerPublicKey, out string? rejection)
    {
        peerPublicKey = [];

        if (CryptoPrimitives.TryOpen(sealingKey, HandshakeFormat.HandshakeNonce, sealedPayload, transcript, out var payload) is false)
        {
            rejection = "authentification illisible : mauvaise clé, ou transcript divergent";
            return false;
        }

        if (payload.Length != HandshakeFormat.AuthenticationLength)
        {
            rejection = "authentification de taille inattendue";
            return false;
        }

        var point = payload[..CryptoPrimitives.PublicPointLength];
        var signature = payload.AsSpan(CryptoPrimitives.PublicPointLength, CryptoPrimitives.SignatureLength);
        var binding = payload.AsSpan(CryptoPrimitives.PublicPointLength + CryptoPrimitives.SignatureLength, 32);

        // La preuve cryptographique d'abord, l'autorisation ensuite, jamais
        // l'inverse. Pour une paire directe, l'autorisation est une simple
        // comparaison avec le carnet et l'ordre serait indifférent ; mais pour
        // un membre de groupe elle épingle la clé présentée et la persiste. La
        // consulter avant la signature laisserait n'importe quel correspondant
        // épingler une clé dont il ne détient pas la partie privée, rien qu'en
        // atteignant ce point du handshake.
        ECDsa verifier;
        try
        {
            verifier = CryptoPrimitives.ImportVerifier(point);
        }
        catch (CryptographicException e)
        {
            rejection = $"identité malformée : {e.Message}";
            return false;
        }

        using (verifier)
        {
            if (CryptoPrimitives.Verify(verifier, Concat(signatureContext, transcript), signature) is false)
            {
                rejection = "signature du transcript invalide";
                return false;
            }
        }

        if (CryptographicOperations.FixedTimeEquals(binding, Bind(bindingKey, point, transcript)) is false)
        {
            rejection = "liaison entre identité et secret partagé invalide";
            return false;
        }

        // L'autorisation vient toujours du carnet local : une identité prouvée
        // mais inconnue reste refusée, et un rendez-vous malveillant ne peut pas
        // remplacer une clé déjà épinglée.
        if (isAuthorized(point) is false)
        {
            rejection = "identité absente du carnet, ou différente de celle attendue";
            return false;
        }

        peerPublicKey = point;
        rejection = null;
        return true;
    }

    private static byte[] Bind(ReadOnlySpan<byte> bindingKey, ReadOnlySpan<byte> publicPoint, ReadOnlySpan<byte> transcript)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, bindingKey);
        hmac.AppendData(publicPoint);
        hmac.AppendData(transcript);
        return hmac.GetHashAndReset();
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var joined = new byte[a.Length + b.Length];
        a.CopyTo(joined);
        b.CopyTo(joined.AsSpan(a.Length));
        return joined;
    }
}
