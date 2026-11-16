namespace LlamaApp.Common;

/// <summary>
/// Turns a per-model extra-argument string into llama.cpp preset lines.
/// The text uses the same command-line syntax as the global custom serve
/// arguments (<see cref="ArgumentTokenizer"/>). llama-server does not take
/// those tokens directly: <c>common/preset.cpp</c> stores one string per
/// INI key, and the key is the CLI name with leading dashes removed
/// (<c>rm_leading_dashes</c>), so <c>-ncmoe 44</c> has to become
/// <c>ncmoe = 44</c>. A bare flag becomes <c>key = true</c> — preset.cpp
/// treats a truthy value with no value-hint as the flag itself.
///
/// <para>Keys must match the parser's ident grammar
/// (<c>[A-Za-z_][A-Za-z0-9_.-]*</c>). A value ends at the first
/// <c>#</c> or <c>;</c> (a comment, even with no preceding space) and at
/// a newline, so those characters are rejected rather than silently
/// truncated. Reserved flags and the keys this app already writes into
/// the section (<c>ctx-size</c>, <c>spec-type</c>, model paths) are
/// rejected: the parser keeps a single value per option, and a second
/// spelling of the same option would fight the app's own line.</para>
/// </summary>
public static class ModelPresetArguments
{
    /// <summary>
    /// Truthy token preset.cpp accepts for a flag that takes no value
    /// (<c>common_arg_utils::is_truthy</c> includes <c>true</c>). A falsey
    /// value would suppress the flag or emit its <c>--no-</c> form.
    /// </summary>
    public const string BareFlagValue = "true";

    /// <summary>
    /// Flags the preset renderer already writes for a model, in every CLI
    /// spelling llama.cpp registers for that option. A per-model argument
    /// using one of them would be a second value for an option the app owns
    /// (context length has its own control; paths come from the folder scan).
    /// </summary>
    public static readonly IReadOnlyList<string> AppManagedFlags =
    [
        "--ctx-size", "-c",
        "--spec-type",
        "--model", "-m",
        "--mmproj", "-mm",
        "--spec-draft-model", "-md", "--model-draft",
    ];

    /// <summary>
    /// Null when <paramref name="text"/> can be stored and rendered. Otherwise
    /// a user-facing error: an open quote, a reserved or app-managed flag, or
    /// a key/value the INI parser would drop or misread.
    /// </summary>
    public static string? Validate(string? text)
        => TryParse(text, out _, out var error) ? null : error;

    /// <summary>
    /// Parses <paramref name="text"/> into preset lines. Empty input succeeds
    /// with no lines. On failure <paramref name="lines"/> is empty and
    /// <paramref name="error"/> is the same message <see cref="Validate"/>
    /// returns — callers that render a hand-edited settings file omit the
    /// arguments instead of writing a preset the server would reject.
    /// A repeated key keeps the last value, matching the parser's map.
    /// </summary>
    public static bool TryParse(
        string? text,
        out IReadOnlyList<(string Key, string Value)> lines,
        out string? error)
    {
        lines = [];
        IReadOnlyList<string> tokens;
        try
        {
            tokens = ArgumentTokenizer.Tokenize(text);
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        if (ServeArgumentPolicy.Validate(tokens) is { } reserved)
        {
            error = reserved;
            return false;
        }

        if (ServeArgumentPolicy.FindFlag(tokens, AppManagedFlags) is { } managed)
        {
            error = $"{managed} is managed by Llama and can't be used in model arguments.";
            return false;
        }

        var parsed = new List<(string Key, string Value)>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!IsFlagToken(token))
            {
                error = $"Unexpected value \"{token}\". Model arguments are flags such as -ncmoe 44.";
                return false;
            }

            string key;
            string value;
            var eq = token.IndexOf('=');
            if (eq >= 0)
            {
                key = StripDashes(token[..eq]);
                value = token[(eq + 1)..];
            }
            else if (i + 1 < tokens.Count && !IsFlagToken(tokens[i + 1]))
            {
                key = StripDashes(token);
                value = tokens[++i];
            }
            else
            {
                key = StripDashes(token);
                value = BareFlagValue;
            }

            if (!IsIdent(key))
            {
                error = $"\"{token}\" is not a valid llama-server preset key.";
                return false;
            }

            if (!IsSafeValue(value))
            {
                error = $"The value for --{key} can't be stored in the model preset.";
                return false;
            }

            var replaced = false;
            for (var j = 0; j < parsed.Count; j++)
            {
                if (string.Equals(parsed[j].Key, key, StringComparison.Ordinal))
                {
                    parsed[j] = (key, value);
                    replaced = true;
                    break;
                }
            }
            if (!replaced) parsed.Add((key, value));
        }

        lines = parsed;
        error = null;
        return true;
    }

    /// <summary>
    /// A flag token starts with <c>-</c> and is not a negative number
    /// (<c>-1</c>, <c>-.5</c> are values). <c>--flag=value</c> is one token.
    /// </summary>
    private static bool IsFlagToken(string token)
    {
        if (token.Length < 2 || token[0] != '-') return false;
        var i = 1;
        if (token[i] == '-')
        {
            if (token.Length == 2) return true;
            i++;
        }

        var c = token[i];
        if (char.IsDigit(c)) return false;
        if (c == '.' && i + 1 < token.Length && char.IsDigit(token[i + 1])) return false;
        return true;
    }

    private static string StripDashes(string flag)
    {
        var i = 0;
        while (i < flag.Length && flag[i] == '-') i++;
        return flag[i..];
    }

    /// <summary>preset.cpp ident: <c>[A-Za-z_][A-Za-z0-9_.-]*</c>.</summary>
    private static bool IsIdent(string key)
    {
        if (key.Length == 0 || !IsIdentStart(key[0])) return false;
        for (var i = 1; i < key.Length; i++)
            if (!IsIdentChar(key[i])) return false;
        return true;
    }

    private static bool IsIdentStart(char c)
        => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';

    private static bool IsIdentChar(char c)
        => IsIdentStart(c) || char.IsDigit(c) || c is '.' or '-';

    /// <summary>
    /// A value the INI parser would keep whole. <c>#</c> and <c>;</c> start a
    /// comment even with no preceding space (<c>ws</c> may be empty), and a
    /// control character would break the line.
    /// </summary>
    private static bool IsSafeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var c in value)
            if (char.IsControl(c) || c is '#' or ';') return false;
        return true;
    }
}
