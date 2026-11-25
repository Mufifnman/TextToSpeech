using System.Text;
using PlainRules;

namespace TextToSpeech.Core.CustomFilters;

/// <summary>One file in the custom filters folder, loaded (or failed to load).</summary>
public sealed class CustomFilterEntry
{
    /// <summary>Stable id used for the on/off setting: "custom:" plus the file name without extension.</summary>
    public string Id { get; init; } = "";
    public string FilePath { get; init; } = "";
    /// <summary>The set's name, or the file name when the set has none.</summary>
    public string DisplayName { get; init; } = "";
    public RuleSet? Set { get; init; }
    public CompiledRuleSet? Compiled { get; init; }
    /// <summary>Why the file could not be used. Null when it loaded fine.</summary>
    public string? Error { get; init; }
    public bool IsUsable => Error == null && Compiled != null;
}

/// <summary>
/// Custom filters stored as one hand-editable JSON file each in a folder
/// (default %LocalAppData%\TextToSpeech\filters). Loading never throws: a bad file becomes an
/// entry with an <see cref="CustomFilterEntry.Error"/> and is skipped by the app.
/// </summary>
public sealed class CustomFilterLibrary
{
    /// <summary>Files larger than this are refused (a filter file is a few KB at most).</summary>
    public const long MaxFileBytes = 256 * 1024;
    /// <summary>Only the first this-many files are read.</summary>
    public const int MaxFiles = 200;
    /// <summary>Sets with more rules than this are refused.</summary>
    public const int MaxRules = 200;
    public const string IdPrefix = "custom:";

    public CustomFilterLibrary(string folder)
    {
        Folder = Path.GetFullPath(folder);
    }

    public string Folder { get; }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "filters");

    /// <summary>Read every *.json file in the folder (not sub-folders), in file-name order.</summary>
    public IReadOnlyList<CustomFilterEntry> Load()
    {
        var result = new List<CustomFilterEntry>();
        string[] files;
        try
        {
            if (!Directory.Exists(Folder)) return result;
            files = Directory.EnumerateFiles(Folder, "*.json", SearchOption.TopDirectoryOnly)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(MaxFiles).ToArray();
        }
        catch (Exception)
        {
            return result;
        }

        foreach (var path in files)
            result.Add(LoadOne(path));
        return result;
    }

    private static CustomFilterEntry LoadOne(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string id = IdPrefix + stem.ToLowerInvariant();
        CustomFilterEntry Fail(string error, RuleSet? set = null) => new()
        {
            Id = id, FilePath = path, DisplayName = string.IsNullOrWhiteSpace(set?.Name) ? stem : set!.Name.Trim(), Set = set, Error = error,
        };

        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return Fail("File is too large to be a filter.");
            var set = RuleSerializer.FromJson(File.ReadAllText(path, Encoding.UTF8));
            if (string.IsNullOrWhiteSpace(set.Name)) set.Name = stem;
            if (set.Rules.Count > MaxRules) return Fail($"Too many rules (limit {MaxRules}).", set);

            var issues = RuleLinter.Check(set);
            var firstError = issues.FirstOrDefault(i => i.Severity == LintSeverity.Error);
            if (firstError != null) return Fail(firstError.ToString(), set);

            return new CustomFilterEntry
            {
                Id = id, FilePath = path, DisplayName = set.Name.Trim(), Set = set, Compiled = RuleEngine.Compile(set),
            };
        }
        catch (Exception ex)
        {
            // Loading must never throw (it runs in the main window's constructor): any problem with one file just skips that file.
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Save a set. Pass the existing file path to overwrite that file (keeps its id and on/off state),
    /// or null to create a new uniquely named file. Returns the path written. Throws on I/O problems.
    /// </summary>
    public string Save(RuleSet set, string? existingPath = null)
    {
        Directory.CreateDirectory(Folder);
        string path = existingPath != null ? Contain(existingPath) : NewPath(set.Name);
        string temp = path + ".tmp";
        File.WriteAllText(temp, RuleSerializer.ToJson(set), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Delete a filter file that lives in this folder.</summary>
    public void Delete(string path)
    {
        string full = Contain(path);
        if (File.Exists(full)) File.Delete(full);
    }

    private string NewPath(string name)
    {
        string stem = RuleSerializer.SafeFileName(name);
        string path = Path.Combine(Folder, stem + ".json");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(Folder, $"{stem}-{n}.json");
        return Contain(path);
    }

    /// <summary>Returns the full path, refusing anything that is not a .json file directly inside the folder.</summary>
    private string Contain(string path)
    {
        string full = Path.GetFullPath(path);
        bool inside = string.Equals(Path.GetDirectoryName(full), Folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        if (!inside || !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a filter file in the filters folder.", nameof(path));
        return full;
    }
}
