using System.Text.Json;

namespace TextToSpeech.Api
{
    /// <summary>
    /// How local tools find the running app: <c>%LocalAppData%\TextToSpeech\api.json</c> holds the
    /// port, token and process id. Written when the API starts, deleted when it stops.
    /// </summary>
    public sealed record ConnectionInfo(int Port, string Token, int Pid)
    {
        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TextToSpeech", "api.json");

        public void Write(string? path = null)
        {
            path ??= DefaultPath;
            TextToSpeech.Core.IO.AtomicFile.WriteAllText(path, JsonSerializer.Serialize(this, ApiJson.Options));
        }

        public static ConnectionInfo? Read(string? path = null)
        {
            path ??= DefaultPath;
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<ConnectionInfo>(File.ReadAllText(path), ApiJson.Options)
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Delete(string? path = null)
        {
            try { File.Delete(path ?? DefaultPath); } catch (Exception) { /* best effort */ }
        }
    }
}
