using System.Text.Json;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class HeelsSanitizerTests
{
    private const string Full =
        "{\"DefaultOffset\":0.035,\"EmotePosition\":{\"X\":101.5,\"Y\":2,\"Z\":-33,\"R\":0,\"Pitch\":0,\"Roll\":0},"
      + "\"MinionPosition\":{\"X\":1,\"Y\":2,\"Z\":3,\"R\":0,\"Pitch\":0,\"Roll\":0},\"Tags\":{\"autre\":\"secret\"},"
      + "\"E\":true,\"PluginVersion\":\"1.2.3.4\",\"Version\":2}";

    [Fact]
    public void Les_champs_qui_revelent_quelque_chose_disparaissent()
    {
        var clean = HeelsSanitizer.Sanitize(Full)!;
        using var doc = JsonDocument.Parse(clean);

        foreach (var removed in HeelsSanitizer.Removed)
            Assert.False(doc.RootElement.TryGetProperty(removed, out _), removed);
    }

    [Fact]
    public void Le_decalage_reste()
    {
        using var doc = JsonDocument.Parse(HeelsSanitizer.Sanitize(Full)!);

        Assert.Equal(0.035, doc.RootElement.GetProperty("DefaultOffset").GetDouble(), 6);
        Assert.Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("[1,2]")]
    public void Une_donnee_illisible_donne_null(string input)
        => Assert.Null(HeelsSanitizer.Sanitize(input));

    [Fact]
    public void Un_json_trop_profond_n_est_pas_un_objet_acceptable()
        => Assert.False(JsonShape.IsObject("{\"a\":{\"b\":{\"c\":{}}}}", maxDepth: 2));

    [Fact]
    public void Un_objet_simple_est_acceptable()
        => Assert.True(JsonShape.IsObject("{\"a\":1}", maxDepth: 8));
}
