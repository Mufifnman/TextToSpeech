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

    /// <summary>Newest file version this library understands. Version 2 adds blocks, scopes and the removeBlock kind.</summary>
    public const int SupportedVersion = 2;

    /// <summary>True when the set uses a feature that older versions of this library would silently get wrong.</summary>
    public static bool UsesBlockFeatures(RuleSet set) =>
        set.Blocks is { Count: > 0 } || set.Rules.Any(r => r.Kind == RuleKind.RemoveBlock || r.Scope != RuleScope.Everywhere || r.Block != null);

    /// <summary>Writes version 2 only when the set needs it, so ordinary filters stay readable by older builds.</summary>
    public static string ToJson(RuleSet set)
    {
        var copy = new RuleSet
        {
            Version = UsesBlockFeatures(set) ? Math.Max(set.Version, 2) : set.Version,
            Name = set.Name, Group = set.Group, Description = set.Description, Rules = set.Rules,
            Blocks = set.Blocks is { Count: > 0 } ? set.Blocks : null,
        };
        return JsonSerializer.Serialize(copy, Options);
    }

    /// <summary>Parse JSON; throws <see cref="RuleFormatException"/> with a readable message on bad input.</summary>
    public static RuleSet FromJson(string json)
    {
        try
        {
            var set = JsonSerializer.Deserialize<RuleSet>(json, Options)
                      ?? throw new RuleFormatException("The file is empty.");
            if (set.Version > SupportedVersion)
                throw new RuleFormatException($"This filter file is version {set.Version}, made by a newer version of the app (this one reads up to version {SupportedVersion}). Update the app to use it.");
            // A hand-edited file can hold "rules": null or a null entry; neither may crash the loader.
            set.Rules = (set.Rules ?? new()).Where(r => r != null).ToList();
            foreach (var r in set.Rules) r.Value ??= "";
            if (set.Blocks != null) set.Blocks = set.Blocks.Where(b => b != null).ToList();
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
