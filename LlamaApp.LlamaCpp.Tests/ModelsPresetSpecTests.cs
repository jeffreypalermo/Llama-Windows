using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// Tests for the spec-decoding entries of the <c>--models-preset</c> INI
/// (issue #97): the router's INI parser accepts sections named by the model
/// id with keys spelled as the CLI long options, and the child needs
/// <c>spec-type</c> to fetch and wire the draft sidecar. These tests pin the
/// exact rendered shape — including how a model with both a context length
/// and a spec type merges into ONE section (a duplicate section would
/// overwrite, not merge, in a naive INI parser).
/// </summary>
public sealed class ModelsPresetSpecTests
{
    private static readonly IReadOnlyDictionary<string, int> NoContexts =
        new Dictionary<string, int>();

    [Fact]
    public void SpecOnlyModel_GetsItsOwnSection()
    {
        var ini = Render(
            contexts: NoContexts,
            specs: new Dictionary<string, string>
            {
                ["ggml-org/Qwen3.8-27B-GGUF:Q4_0"] = "draft-dflash",
            });

        Assert.Equal(
            "[ggml-org/Qwen3.8-27B-GGUF:Q4_0]\n" +
            "spec-type = draft-dflash\n\n",
            ini);
    }

    [Fact]
    public void ContextAndSpec_MergeIntoOneSection()
    {
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/repo:Q4_0"] = 8192 },
            specs: new Dictionary<string, string> { ["org/repo:Q4_0"] = "draft-mtp" });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 8192\n" +
            "spec-type = draft-mtp\n\n",
            ini);
    }

    [Fact]
    public void NegativeCacheEntries_AreNotRendered()
    {
        // "" means "checked, the repo ships no head" — never an INI entry
        // (an empty spec-type would fault the child's arg parsing).
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/repo:Q4_0"] = 4096 },
            specs: new Dictionary<string, string>
            {
                ["org/repo:Q4_0"] = "",
                ["org/other:Q8_0"] = "",
            });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 4096\n\n",
            ini);
    }

    [Fact]
    public void NoEntriesAtAll_RendersEmpty()
    {
        Assert.Equal("", Render(contexts: NoContexts, specs: new Dictionary<string, string>()));
        Assert.Equal("", Render(contexts: NoContexts,
            specs: new Dictionary<string, string> { ["x"] = "" }));
    }

    [Fact]
    public void InvalidContextEntries_AreSkippedLikeBefore()
    {
        var ini = Render(
            contexts: new Dictionary<string, int>
            {
                ["org/repo:Q4_0"] = 0,
                ["org/repo2:Q4_0"] = -5,
                [""] = 100,
            },
            specs: new Dictionary<string, string> { ["org/repo:Q4_0"] = "draft-dspark" });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "spec-type = draft-dspark\n\n",
            ini);
    }

    [Fact]
    public void SectionNamesWithBrackets_AreSkipped()
    {
        // A .gguf filename may legally contain ']' / '[' — rendering one raw
        // would end the section early ("[x]junk]") and silently apply the
        // preset to the wrong model. The whole entry is dropped instead.
        var ini = Render(
            contexts: new Dictionary<string, int>
            {
                ["org/x]y:Q4_0"] = 8192,
                ["org/[z:Q4_0"] = 4096,
            },
            specs: new Dictionary<string, string> { ["org/x]y:Q4_0"] = "draft-mtp" });

        Assert.Equal("", ini);
    }

    [Fact]
    public void SectionNamesWithControlCharacters_AreSkipped()
    {
        // A newline would inject whole INI lines — settings entries are
        // hand-editable even though filenames can't carry CR/LF.
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/x\ny:Q4_0"] = 8192 },
            specs: new Dictionary<string, string> { ["org/x\r\ny:Q4_0"] = "draft-mtp" });

        Assert.Equal("", ini);
    }

    [Fact]
    public void SpecValuesWithControlCharacters_AreDroppedButContextKept()
    {
        // The value must not be able to append arbitrary keys; the model's
        // ctx-size entry is still rendered, just without the spec line.
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/repo:Q4_0"] = 4096 },
            specs: new Dictionary<string, string>
            {
                ["org/repo:Q4_0"] = "draft-mtp\nctx-size = 999999",
            });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 4096\n\n",
            ini);
    }

    [Fact]
    public void ExtraArguments_MergeIntoThatModelsSingleSection()
    {
        // llama-server resets a section when the header repeats, so context,
        // spec type, enrolled paths, and extra arguments must be one section.
        // A reserved flag is not written; the model's ctx-size still is.
        var ini = Render(
            contexts: new Dictionary<string, int>
            {
                ["org/repo:Q4_0"] = 4096,
                ["local"] = 131072,
            },
            specs: new Dictionary<string, string> { ["local"] = "draft-mtp" },
            enrolled:
            [
                new Llama.EnrolledFolderModel(
                    "local",
                    @"D:\models\local.gguf",
                    @"D:\models\mmproj.gguf",
                    @"D:\models\mtp.gguf"),
            ],
            serveArguments: new Dictionary<string, string>
            {
                ["org/repo:Q4_0"] = "--host 0.0.0.0",
                ["local"] = "-ncmoe 44 -b 4096 -ub 2048 -ot per_layer_token_embd.weight=CPU -np 3 -kvu",
            });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 4096\n\n" +
            "[local]\n" +
            "ctx-size = 131072\n" +
            "spec-type = draft-mtp\n" +
            "model = D:\\models\\local.gguf\n" +
            "mmproj = D:\\models\\mmproj.gguf\n" +
            "spec-draft-model = D:\\models\\mtp.gguf\n" +
            "ncmoe = 44\n" +
            "b = 4096\n" +
            "ub = 2048\n" +
            "ot = per_layer_token_embd.weight=CPU\n" +
            "np = 3\n" +
            "kvu = true\n\n",
            ini);
        Assert.Equal(1, ini.Split("[local]").Length - 1);
    }

    [Fact]
    public void ExtraArgumentsAlone_GetTheirOwnSection()
    {
        var ini = Render(
            contexts: NoContexts,
            specs: new Dictionary<string, string>(),
            serveArguments: new Dictionary<string, string>
            {
                ["ggml-org/Qwen3.8-GGUF:Q4_K_M"] = "--n-cpu-moe 44 --batch-size=4096 -np -1",
            });

        Assert.Equal(
            "[ggml-org/Qwen3.8-GGUF:Q4_K_M]\n" +
            "n-cpu-moe = 44\n" +
            "batch-size = 4096\n" +
            "np = -1\n\n",
            ini);
    }

    [Fact]
    public void RepeatedArgument_KeepsTheLastValueInPlace()
    {
        var ini = Render(
            contexts: NoContexts,
            specs: new Dictionary<string, string>(),
            serveArguments: new Dictionary<string, string>
            {
                ["m"] = "-b 1 -ub 32 -b 4096 --alias \"My Model\"",
            });

        Assert.Equal(
            "[m]\n" +
            "b = 4096\n" +
            "ub = 32\n" +
            "alias = My Model\n\n",
            ini);
    }

    [Fact]
    public void UnsafeExtraArguments_AreOmittedButContextKept()
    {
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/repo:Q4_0"] = 8192 },
            specs: new Dictionary<string, string>(),
            serveArguments: new Dictionary<string, string>
            {
                ["org/repo:Q4_0"] = "--alias \"a;b\"",
                ["org/other:Q8_0"] = "--port=9",
                ["only-bad"] = "-c 131072",
            });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 8192\n\n",
            ini);
    }

    private static string Render(
        IReadOnlyDictionary<string, int> contexts,
        IReadOnlyDictionary<string, string> specs,
        IEnumerable<Llama.EnrolledFolderModel>? enrolled = null,
        IReadOnlyDictionary<string, string>? serveArguments = null) =>
        Llama.LlamaManager.RenderModelsPresetIni(contexts, specs, enrolled, serveArguments);
}
