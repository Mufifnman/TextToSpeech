using System.Text.Json;
using System.Text.Json.Nodes;

namespace TextToSpeech.Core.IO;

/// <summary>
/// Loads and saves one settings-like object as a JSON file.
/// <list type="bullet">
/// <item>Saving is atomic (<see cref="AtomicFile"/>) and writes a <c>"version"</c> field first in the file.</item>
/// <item>A file that cannot be parsed is copied to <c>&lt;file&gt;.bad</c> (replacing an older copy) and defaults are
/// returned, so the next save cannot silently destroy the only copy of what the user had.</item>
/// <item>A file written by a newer version of the app (higher <c>version</c>) is still read, and is copied to
/// <c>&lt;file&gt;.v&lt;n&gt;.bak</c> once before this version first overwrites it.</item>
/// <item>A file that exists but cannot be read (locked, no permission) is left alone and defaults are returned.</item>
/// </list>
/// A missing version means 0 (files written before versions existed); the current version is written on the next save.
/// </summary>
public sealed class JsonFileStore<T> where T : class, new()
{
    public const string VersionKey = "version";

    private readonly string path;
    private readonly int currentVersion;
    private readonly JsonSerializerOptions options;
    private int loadedVersion;
    private bool newerBackupDone;
    private bool readFailed;   // the file exists but could not be read: do not overwrite it without keeping a copy

    public JsonFileStore(string path, int currentVersion = 1, JsonSerializerOptions? options = null)
    {
        this.path = path;
        this.currentVersion = currentVersion;
        this.options = options ?? new JsonSerializerOptions { WriteIndented = true };
    }

    public string FilePath => path;

    /// <summary>The version found in the file by the last <see cref="Load"/> (0 when missing or no file).</summary>
    public int LoadedVersion => loadedVersion;

    /// <summary>What went wrong in the last <see cref="Load"/>, for a log or a message; null when it went fine.</summary>
    public string? LastProblem { get; private set; }

    public T Load()
    {
        LastProblem = null;
        loadedVersion = 0;
        readFailed = false;
        string text;
        try
        {
            if (!File.Exists(path)) return new T();
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastProblem = "Could not read " + path + ": " + ex.Message;
            readFailed = true;
            return new T();
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject node) throw new JsonException("The file does not hold a JSON object.");
            loadedVersion = node[VersionKey] is JsonValue v && v.TryGetValue<int>(out int n) ? n : 0;
            return node.Deserialize<T>(options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            loadedVersion = 0;
            LastProblem = "Could not parse " + path + " (" + ex.Message + "); a copy was kept as .bad.";
            try { File.Copy(path, path + ".bad", overwrite: true); }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                LastProblem += " The copy failed: " + copyEx.Message;
            }
            return new T();
        }
    }

    /// <summary>Writes the value atomically. Exceptions are not swallowed; callers decide what a failed save means.</summary>
    public void Save(T value)
    {
        if (readFailed && File.Exists(path))
        {
            // The last Load could not read this file, so what is about to be written is built from defaults.
            // Keep the real file first; if it is still unreadable this throws and the file is left untouched.
            File.Copy(path, path + ".unread.bak", overwrite: true);
            readFailed = false;
        }
        if (loadedVersion > currentVersion && !newerBackupDone && File.Exists(path))
        {
            File.Copy(path, path + ".v" + loadedVersion + ".bak", overwrite: true);
            newerBackupDone = true;
        }

        var node = JsonSerializer.SerializeToNode(value, options) as JsonObject
            ?? throw new InvalidOperationException(typeof(T).Name + " does not serialize to a JSON object.");
        var properties = node.ToArray();
        node.Clear();
        node[VersionKey] = currentVersion;
        foreach (var property in properties)
        {
            if (property.Key != VersionKey) node[property.Key] = property.Value;
        }
        AtomicFile.WriteAllText(path, node.ToJsonString(options));
    }
}
