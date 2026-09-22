using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

/// <summary>
/// La forme compressée ramène le code d'invitation de cent quarante à
/// quatre-vingt-huit caractères. Ce qu'on colle dans un salon Discord, autant
/// que ce soit court.
/// </summary>
public class PointCompressionTests
{
    [Fact]
    public void Un_point_compresse_se_decompresse_a_l_identique()
    {
        for (var i = 0; i < 50; i++)
        {
            using var identity = CryptoPrimitives.GenerateIdentity();
            var full = CryptoPrimitives.ExportPublicPoint(identity);

            var compressed = CryptoPrimitives.Compress(full);
            Assert.Equal(CryptoPrimitives.CompressedPointLength, compressed.Length);
            Assert.True(compressed[0] is 0x02 or 0x03);

            Assert.Equal(full, CryptoPrimitives.Decompress(compressed));
        }
    }

    [Fact]
    public void Le_prefixe_porte_la_parite_de_l_ordonnee()
    {
        // Sans cette parité, la décompression rendrait l'un des deux points
        // symétriques au hasard, donc une identité sur deux serait fausse.
        using var identity = CryptoPrimitives.GenerateIdentity();
        var full = CryptoPrimitives.ExportPublicPoint(identity);
        var compressed = CryptoPrimitives.Compress(full);

        var expected = (full[^1] & 1) == 0 ? 0x02 : 0x03;
        Assert.Equal(expected, compressed[0]);
    }

    [Fact]
    public void Un_point_compresse_se_verifie_comme_une_identite()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var signature = CryptoPrimitives.Sign(identity, "message"u8);

        var compressed = CryptoPrimitives.Compress(CryptoPrimitives.ExportPublicPoint(identity));
        using var verifier = CryptoPrimitives.ImportVerifier(CryptoPrimitives.Decompress(compressed));

        Assert.True(CryptoPrimitives.Verify(verifier, "message"u8, signature));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(34)]
    [InlineData(65)]
    public void Une_forme_compressee_de_mauvaise_taille_est_refusee(int length)
    {
        Assert.Throws<CryptographicException>(() => CryptoPrimitives.Decompress(new byte[length]));
    }

    [Fact]
    public void Un_prefixe_inconnu_est_refuse()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var compressed = CryptoPrimitives.Compress(CryptoPrimitives.ExportPublicPoint(identity));
        compressed[0] = 0x04;

        Assert.Throws<CryptographicException>(() => CryptoPrimitives.Decompress(compressed));
    }

    [Fact]
    public void Une_abscisse_sans_point_sur_la_courbe_est_refusee()
    {
        // Une abscisse sur deux n'a pas d'ordonnée : x³ - 3x + b n'est pas
        // toujours un carré modulo p. Il faut le détecter, pas rendre n'importe
        // quoi.
        var refused = 0;

        for (var i = 1; i < 40; i++)
        {
            var compressed = new byte[CryptoPrimitives.CompressedPointLength];
            compressed[0] = 0x02;
            compressed[^1] = (byte)i;

            try
            {
                CryptoPrimitives.Decompress(compressed);
            }
            catch (CryptographicException)
            {
                refused++;
            }
        }

        Assert.True(refused > 0, "aucune abscisse invalide n'a été détectée");
    }
}
