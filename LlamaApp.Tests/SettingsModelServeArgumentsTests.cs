using System.Text.Json;
using Xunit;

namespace LlamaApp.Tests;

/// <summary>
/// Per-model server arguments persist beside the other per-model dictionaries.
/// A settings.json written before the field existed must still load, and the
/// text must round-trip exactly — the renderer, not the serializer, interprets it.
/// </summary>
public sealed class SettingsModelServeArgumentsTests
{
    [Fact]
    public void ModelServeArguments_DefaultsToEmpty()
    {
        var settings = new Settings();

        Assert.NotNull(settings.ModelServeArguments);
        Assert.Empty(settings.ModelServeArguments);
    }

    [Fact]
    public void ModelServeArguments_MissingFromSavedJson_DefaultsToEmpty()
    {
        const string json = """{"ServerPort":9931,"ModelContextLengths":{"org/repo:Q4_0":4096}}""";

        var settings = JsonSerializer.Deserialize<Settings>(json);

        Assert.NotNull(settings);
        Assert.Empty(settings.ModelServeArguments);
        Assert.Equal(4096, settings.ModelContextLengths["org/repo:Q4_0"]);
    }

    [Fact]
    public void ModelServeArguments_RoundTripsThroughJson()
    {
        const string text = "-ncmoe 44 -b 4096 -ub 2048 -ot per_layer_token_embd.weight=CPU -np 3 -kvu";
        var settings = new Settings
        {
            ModelServeArguments = new Dictionary<string, string>
            {
                ["ggml-org/Qwen3.8-GGUF:Q4_K_M"] = text,
            },
        };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<Settings>(json);

        Assert.NotNull(loaded);
        Assert.Equal(text, loaded.ModelServeArguments["ggml-org/Qwen3.8-GGUF:Q4_K_M"]);
    }
}
