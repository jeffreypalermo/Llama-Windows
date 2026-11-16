using LlamaApp.Common;
using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// Per-model arguments are command-line text, but llama-server reads them as
/// preset INI keys (leading dashes stripped, one value per key). These tests
/// pin that translation and the rejection of flags the app already owns —
/// the same reserved set as global custom arguments, plus the preset keys
/// the renderer writes itself.
/// </summary>
public sealed class ModelPresetArgumentsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    public void Empty_IsAccepted_AndRendersNothing(string? text)
    {
        Assert.Null(ModelPresetArguments.Validate(text));
        Assert.True(ModelPresetArguments.TryParse(text, out var lines, out var error));
        Assert.Null(error);
        Assert.Empty(lines);
    }

    [Fact]
    public void MoeFlags_BecomePresetKeysTheServerAccepts()
    {
        // Qwen3.8 Flash-Next on a 16 GB GPU: short options, one tensor
        // override, a bare boolean. preset.cpp maps both the short and long
        // names, and a truthy valueless flag is emitted as the flag itself.
        const string text =
            "-ncmoe 44 -b 4096 -ub 2048 -ot per_layer_token_embd.weight=CPU -np 3 -kvu";

        Assert.Null(ModelPresetArguments.Validate(text));
        Assert.True(ModelPresetArguments.TryParse(text, out var lines, out _));
        Assert.Equal(
            [
                ("ncmoe", "44"),
                ("b", "4096"),
                ("ub", "2048"),
                ("ot", "per_layer_token_embd.weight=CPU"),
                ("np", "3"),
                ("kvu", "true"),
            ],
            lines);
    }

    [Fact]
    public void LongFormAndEqualsForm_StripDashesOnce()
    {
        Assert.True(ModelPresetArguments.TryParse(
            "--n-gpu-layers 20 --ubatch-size=2048", out var lines, out _));
        Assert.Equal([("n-gpu-layers", "20"), ("ubatch-size", "2048")], lines);
    }

    [Theory]
    [InlineData("--host 0.0.0.0")]
    [InlineData("--port=8080")]
    [InlineData("--models-preset C:\\preset.ini")]
    [InlineData("--models-dir C:\\models")]
    [InlineData("--api-key secret")]
    [InlineData("--HOST")]
    public void ReservedFlags_AreRejected(string text)
    {
        var error = ModelPresetArguments.Validate(text);
        Assert.NotNull(error);
        Assert.Contains("managed by Llama", error);
        Assert.False(ModelPresetArguments.TryParse(text, out var lines, out _));
        Assert.Empty(lines);
    }

    [Theory]
    [InlineData("-c 131072")]
    [InlineData("--ctx-size=131072")]
    [InlineData("--spec-type draft-mtp")]
    [InlineData("-m C:\\model.gguf")]
    [InlineData("--model=C:\\model.gguf")]
    [InlineData("-mm C:\\mmproj.gguf")]
    [InlineData("--spec-draft-model C:\\draft.gguf")]
    [InlineData("-md C:\\draft.gguf")]
    public void AppManagedPresetKeys_AreRejected(string text)
    {
        var error = ModelPresetArguments.Validate(text);
        Assert.NotNull(error);
        Assert.Contains("model arguments", error);
    }

    [Fact]
    public void RepeatedKey_KeepsTheLastValue()
    {
        Assert.True(ModelPresetArguments.TryParse("-b 1 -ub 8 -b 4096", out var lines, out _));
        Assert.Equal([("b", "4096"), ("ub", "8")], lines);
    }

    [Fact]
    public void NegativeNumber_IsAValueNotAFlag()
    {
        Assert.True(ModelPresetArguments.TryParse("-np -1", out var lines, out _));
        Assert.Equal([("np", "-1")], lines);
    }

    [Theory]
    [InlineData("--alias \"a;b\"")]
    [InlineData("--alias \"say # hi\"")]
    [InlineData("--alias \"line\nbreak\"")]
    [InlineData("--alias=")]
    public void ValuesTheIniParserWouldTruncate_AreRejected(string text)
    {
        Assert.NotNull(ModelPresetArguments.Validate(text));
    }

    [Fact]
    public void UnterminatedQuote_IsRejected()
    {
        var error = ModelPresetArguments.Validate("--alias \"My Model");
        Assert.NotNull(error);
        Assert.Contains("quote", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LooseValue_IsRejected()
    {
        Assert.NotNull(ModelPresetArguments.Validate("44 -ncmoe"));
    }
}
