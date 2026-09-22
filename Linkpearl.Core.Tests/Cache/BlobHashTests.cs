using System.Text;
using Linkpearl.Core.Cache;
using Xunit;

namespace Linkpearl.Core.Tests.Cache;

public class BlobHashTests
{
    // Vecteur de reference FIPS 180-4 : SHA-256("abc").
    private const string Sha256OfAbc =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Le_hash_d_un_contenu_est_bien_un_sha256()
    {
        var hash = BlobHash.OfContent("abc"u8);
        Assert.Equal(Sha256OfAbc, hash.ToHex());
    }

    [Fact]
    public async Task Le_hash_d_un_flux_donne_le_meme_resultat_que_celui_d_un_tampon()
    {
        var content = Encoding.UTF8.GetBytes(new string('m', 300_000));
        using var stream = new MemoryStream(content);

        Assert.Equal(BlobHash.OfContent(content), await BlobHash.OfStreamAsync(stream, default));
    }

    [Fact]
    public void L_aller_retour_hexadecimal_conserve_la_valeur()
    {
        var hash = BlobHash.OfContent("abc"u8);
        Assert.True(BlobHash.TryParseHex(hash.ToHex(), out var relu));
        Assert.Equal(hash, relu);
    }

    [Fact]
    public void L_hexadecimal_en_majuscules_est_accepte_et_rendu_en_minuscules()
    {
        Assert.True(BlobHash.TryParseHex(Sha256OfAbc.ToUpperInvariant(), out var hash));
        Assert.Equal(Sha256OfAbc, hash.ToHex());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ba7816bf")]                                                          // trop court
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015adff")] // trop long
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]   // hors hexa
    public void Un_hexadecimal_invalide_est_refuse(string hex)
    {
        Assert.False(BlobHash.TryParseHex(hex, out _));
    }

    [Fact]
    public void Deux_contenus_differents_donnent_deux_hash_differents()
    {
        Assert.NotEqual(BlobHash.OfContent("abc"u8), BlobHash.OfContent("abd"u8));
    }

    [Fact]
    public void Deux_hash_du_meme_contenu_sont_egaux_et_ont_le_meme_code_de_hachage()
    {
        var a = BlobHash.OfContent("abc"u8);
        var b = BlobHash.OfContent("abc"u8);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
    }

    [Fact]
    public void Les_deux_niveaux_de_repertoire_du_cache_viennent_des_premiers_octets()
    {
        // 256 x 256 feuilles : a 100 000 blobs, environ 1,5 fichier par feuille.
        var hash = BlobHash.OfContent("abc"u8);
        Assert.Equal("ba", hash.CacheLevel1);
        Assert.Equal("78", hash.CacheLevel2);
    }

    [Fact]
    public void Le_hash_se_reserialise_octet_pour_octet()
    {
        var hash = BlobHash.OfContent("abc"u8);
        Span<byte> buffer = stackalloc byte[BlobHash.SizeInBytes];

        Assert.True(hash.TryWriteTo(buffer));
        Assert.Equal(hash, BlobHash.FromBytes(buffer));
        Assert.Equal(Convert.FromHexString(Sha256OfAbc), buffer.ToArray());
    }

    [Fact]
    public void Un_tampon_de_mauvaise_taille_est_refuse()
    {
        Assert.Throws<ArgumentException>(() => BlobHash.FromBytes(new byte[31]));
        Assert.False(BlobHash.OfContent("abc"u8).TryWriteTo(new byte[31]));
    }
}
