using System.Numerics;
using System.Security.Cryptography;

namespace Linkpearl.Core.Crypto;

/// <summary>
/// Les primitives du protocole, toutes tirées de System.Security.Cryptography.
/// </summary>
/// <remarks>
/// P-256, AES-256-GCM, HKDF-SHA256 et SHA-256 sont intégrés au runtime,
/// accélérés matériellement (AES-NI, SHA-NI) et disponibles dès Windows 10.
///
/// Ni Ed25519 ni X25519 ne sont dans le runtime en .NET 10, et leurs
/// implémentations managées sont lentes : BouncyCastle chiffre en C# scalaire,
/// entre 150 et 400 Mo/s, là où AesGcm dépasse le gigaoctet par seconde. Sur un
/// transfert de plusieurs centaines de mégaoctets dans le processus du jeu, la
/// différence se voit en chute de fréquence d'affichage.
///
/// ChaCha20-Poly1305 est bien dans le runtime mais exige Windows 10 build
/// 20142, soit Windows 11 en pratique. Ses tests passeraient sous Linux et
/// échoueraient chez un quart des joueurs.
/// </remarks>
public static class CryptoPrimitives
{
    /// <summary>Point P-256 non compressé : préfixe 0x04, puis X et Y sur 32 octets.</summary>
    public const int PublicPointLength = 65;

    /// <summary>Point P-256 compressé : préfixe de parité, puis X sur 32 octets.</summary>
    public const int CompressedPointLength = 33;

    /// <summary>Signature au format concaténé r||s, de taille fixe.</summary>
    public const int SignatureLength = 64;

    public const int KeyLength = 32;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    private const int CoordinateLength = 32;

    private static readonly ECCurve Curve = ECCurve.NamedCurves.nistP256;

    public static ECDsa GenerateIdentity() => ECDsa.Create(Curve);

    public static ECDiffieHellman GenerateEphemeral() => ECDiffieHellman.Create(Curve);

    public static byte[] ExportPublicPoint(ECDsa key) => Encode(key.ExportParameters(false));

    public static byte[] ExportPublicPoint(ECDiffieHellman key) => Encode(key.ExportParameters(false));

    /// <summary>
    /// Relit un point public venant du réseau.
    /// </summary>
    /// <remarks>
    /// Lève si le point n'est pas sur la courbe : c'est le runtime qui le
    /// vérifie à l'import, et c'est exactement ce qu'on veut face à un pair qui
    /// enverrait n'importe quoi.
    /// </remarks>
    public static ECDsa ImportVerifier(ReadOnlySpan<byte> point) => ECDsa.Create(Decode(point));

    public static ECDiffieHellman ImportAgreer(ReadOnlySpan<byte> point) => ECDiffieHellman.Create(Decode(point));

    public static byte[] Sign(ECDsa identity, ReadOnlySpan<byte> message)
        => identity.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(ECDsa verifier, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureLength)
            return false;

        try
        {
            return verifier.VerifyData(
                message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Secret brut de l'accord ECDH, sans dérivation : HKDF s'en charge ensuite.</summary>
    public static byte[] Agree(ECDiffieHellman ours, ReadOnlySpan<byte> theirPoint)
    {
        using var theirs = ImportAgreer(theirPoint);
        return ours.DeriveRawSecretAgreement(theirs.PublicKey);
    }

    public static byte[] Seal(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associated)
    {
        var output = new byte[plaintext.Length + TagLength];

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associated);

        return output;
    }

    /// <summary>
    /// Ouvre un texte chiffré venant du réseau. Ne lève jamais : une étiquette
    /// invalide est un fait ordinaire, pas une exception.
    /// </summary>
    public static bool TryOpen(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> sealedData,
        ReadOnlySpan<byte> associated, out byte[] plaintext)
    {
        plaintext = [];

        if (sealedData.Length < TagLength)
            return false;

        var output = new byte[sealedData.Length - TagLength];

        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, sealedData[..output.Length], sealedData[output.Length..], output, associated);
        }
        catch (CryptographicException)
        {
            return false;
        }

        plaintext = output;
        return true;
    }

    /// <summary>
    /// Ramène un point à sa forme compressée.
    /// </summary>
    /// <remarks>
    /// L'ordonnée d'un point de la courbe se retrouve à partir de son abscisse,
    /// au signe près. Un seul bit de parité suffit donc à lever l'ambiguïté, et
    /// le code d'invitation passe de cent quarante à quatre-vingt-huit
    /// caractères. Sur quelque chose que l'on colle dans un salon de discussion,
    /// cela compte.
    /// </remarks>
    public static byte[] Compress(ReadOnlySpan<byte> point)
    {
        if (point.Length != PublicPointLength || point[0] != 0x04)
            throw new CryptographicException("point public P-256 malformé");

        var compressed = new byte[CompressedPointLength];
        compressed[0] = (byte)(0x02 | (point[^1] & 1));
        point.Slice(1, CoordinateLength).CopyTo(compressed.AsSpan(1));
        return compressed;
    }

    /// <summary>
    /// Retrouve l'ordonnée à partir de l'abscisse et du bit de parité.
    /// </summary>
    /// <remarks>
    /// Le module de P-256 vaut 3 modulo 4, donc la racine carrée modulaire
    /// s'obtient par une simple exponentiation à la puissance (p+1)/4. Il faut
    /// ensuite vérifier que le résultat est bien une racine : une abscisse sur
    /// deux n'appartient à aucun point de la courbe, et rendre un point faux
    /// serait pire que refuser.
    /// </remarks>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length != CompressedPointLength || compressed[0] is not (0x02 or 0x03))
            throw new CryptographicException("point public P-256 compressé malformé");

        var x = new BigInteger(compressed[1..], isUnsigned: true, isBigEndian: true);

        if (x >= P)
            throw new CryptographicException("abscisse hors du corps de P-256");

        var square = (BigInteger.ModPow(x, 3, P) - (3 * x) + B) % P;
        if (square.Sign < 0)
            square += P;

        var y = BigInteger.ModPow(square, (P + 1) / 4, P);

        if (BigInteger.ModPow(y, 2, P) != square)
            throw new CryptographicException("abscisse n'appartenant à aucun point de la courbe");

        if ((y.IsEven ? 0 : 1) != (compressed[0] & 1))
            y = P - y;

        var point = new byte[PublicPointLength];
        point[0] = 0x04;
        WriteCoordinate(x, point.AsSpan(1));
        WriteCoordinate(y, point.AsSpan(1 + CoordinateLength));
        return point;
    }

    private static void WriteCoordinate(BigInteger value, Span<byte> destination)
    {
        destination.Clear();
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        bytes.CopyTo(destination[(CoordinateLength - bytes.Length)..]);
    }

    private static byte[] Encode(ECParameters parameters)
    {
        var point = new byte[PublicPointLength];
        point[0] = 0x04;
        parameters.Q.X.AsSpan().CopyTo(point.AsSpan(1));
        parameters.Q.Y.AsSpan().CopyTo(point.AsSpan(1 + CoordinateLength));
        return point;
    }

    private static ECParameters Decode(ReadOnlySpan<byte> point)
    {
        if (point.Length != PublicPointLength || point[0] != 0x04)
            throw new CryptographicException("point public P-256 malformé");

        var x = point.Slice(1, CoordinateLength).ToArray();
        var y = point.Slice(1 + CoordinateLength, CoordinateLength).ToArray();

        EnsureOnCurve(x, y);

        return new ECParameters
        {
            Curve = Curve,
            Q = new ECPoint { X = x, Y = y },
        };
    }

    // Paramètres de P-256, FIPS 186-4. a vaut p - 3.
    private static readonly BigInteger P = Parse("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
    private static readonly BigInteger B = Parse("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");

    /// <summary>
    /// Vérifie que le point appartient bien à la courbe.
    /// </summary>
    /// <remarks>
    /// Le runtime ne le fait pas à l'import : un point arbitraire venant du
    /// réseau y passe sans bruit. C'est la porte d'entrée des attaques par
    /// courbe invalide, où l'on fait calculer un accord sur un point d'un
    /// sous-groupe de petit ordre pour extraire la clé privée morceau par
    /// morceau. Nos clés d'accord sont éphémères, ce qui limite beaucoup la
    /// portée, mais le contrôle coûte une multiplication modulaire et ferme la
    /// question.
    ///
    /// On vérifie y² ≡ x³ - 3x + b (mod p), et que les deux coordonnées sont
    /// bien dans le corps. Le point à l'infini n'a pas de représentation en
    /// forme non compressée, il n'y a donc rien à écarter de ce côté.
    /// </remarks>
    private static void EnsureOnCurve(byte[] x, byte[] y)
    {
        var bx = new BigInteger(x, isUnsigned: true, isBigEndian: true);
        var by = new BigInteger(y, isUnsigned: true, isBigEndian: true);

        if (bx >= P || by >= P)
            throw new CryptographicException("coordonnée hors du corps de P-256");

        var left = BigInteger.ModPow(by, 2, P);
        var right = (BigInteger.ModPow(bx, 3, P) - (3 * bx) + B) % P;

        if (right.Sign < 0)
            right += P;

        if (left != right)
            throw new CryptographicException("point public hors de la courbe P-256");
    }

    private static BigInteger Parse(string hex)
        => new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
}
