using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlainRules;

/// <summary>Reads and writes rule sets as hand-editable JSON (comments and trailing commas allowed).</summary>
public static class RuleSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static string ToJson(RuleSet set) => JsonSerializer.Serialize(set, Options);

    /// <summary>Parse JSON; throws <see cref="RuleFormatException"/> with a readable message on bad input.</summary>
    public static RuleSet FromJson(string json)
    {
        try
        {
            var set = JsonSerializer.Deserialize<RuleSet>(json, Options)
                      ?? throw new RuleFormatException("The file is empty.");
            set.Rules ??= new();
            foreach (var r in set.Rules) r.Value ??= "";
            return set;
        }
        catch (JsonException ex)
        {
            throw new RuleFormatException($"Not a valid filter file: {ex.Message}", ex);
        }
    }

    public static bool TryFromJson(string json, out RuleSet? set, out string? error)
    {
        try { set = FromJson(json); error = null; return true; }
        catch (RuleFormatException ex) { set = null; error = ex.Message; return false; }
    }

    public static RuleSet LoadFile(string path) => FromJson(File.ReadAllText(path));

    public static void SaveFile(RuleSet set, string path) => File.WriteAllText(path, ToJson(set));

    /// <summary>A file-system-safe file name (without extension) for a set name.</summary>
    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Trim().Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim('.', ' ');
        return clean.Length == 0 ? "filter" : clean;
    }
}

public sealed class RuleFormatException : Exception
{
    public RuleFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
