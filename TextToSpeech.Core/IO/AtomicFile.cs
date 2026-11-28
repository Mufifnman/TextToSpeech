using System.Text;

namespace TextToSpeech.Core.IO;

/// <summary>
/// Writes a file so a crash or power loss mid-write cannot leave a half-written (corrupt) file behind:
/// the text goes to a uniquely named temp file in the same folder first, then replaces the target.
/// The final move is as atomic as the file system allows; on Windows it can fail if another program
/// holds the target open. Exceptions are not swallowed: callers decide what a failed save means.
/// </summary>
public static class AtomicFile
{
    /// <summary>UTF-8 without a byte order mark, the same as <see cref="File.WriteAllText(string, string)"/>.</summary>
    private static readonly UTF8Encoding NoBom = new(false);

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>, creating the folder if needed.</summary>
    public static void WriteAllText(string path, string text, Encoding? encoding = null)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // The ".tmp" ending keeps leftovers out of "*.json" folder scans.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, text, encoding ?? NoBom);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
