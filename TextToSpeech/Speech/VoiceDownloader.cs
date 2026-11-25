using System.IO;
using System.Formats.Tar;
using System.Net.Http;
using ICSharpCode.SharpZipLib.BZip2;

namespace TextToSpeech.Speech
{
    public sealed record DownloadableVoice(string Name, string ArchiveName, string SizeLabel)
    {
        public override string ToString() => $"{Name} ({SizeLabel})";
    }

    /// <summary>
    /// Fetches ready-to-use voice models from the sherpa-onnx "tts-models" GitHub release and unpacks them
    /// into <see cref="NeuralSpeechEngine.UserVoicesFolder"/>.
    /// </summary>
    public static class VoiceDownloader
    {
        private const string BaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/";

        // GUESS: a starter set picked for English reading. Any other archive from that release works too.
        public static readonly IReadOnlyList<DownloadableVoice> Catalog = new[]
        {
            new DownloadableVoice("Amy, US English, quick to start", "vits-piper-en_US-amy-low", "67 MB"),
            new DownloadableVoice("Lessac, US English, clearer", "vits-piper-en_US-lessac-medium", "67 MB"),
            new DownloadableVoice("Kokoro, 11 US and UK voices, most natural", "kokoro-en-v0_19", "320 MB"),
        };

        public static bool IsInstalled(DownloadableVoice voice) =>
            Directory.Exists(Path.Combine(NeuralSpeechEngine.UserVoicesFolder, voice.ArchiveName));

        public static async Task DownloadAsync(DownloadableVoice voice, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            string root = NeuralSpeechEngine.UserVoicesFolder;
            Directory.CreateDirectory(root);
            string archive = Path.Combine(root, voice.ArchiveName + ".tar.bz2.part");
            string staging = Path.Combine(root, ".staging-" + voice.ArchiveName);

            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
                using (var response = await http.GetAsync(BaseUrl + voice.ArchiveName + ".tar.bz2",
                           HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    long? total = response.Content.Headers.ContentLength;
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var file = File.Create(archive);
                    var buffer = new byte[1 << 16];
                    long read = 0;
                    int n;
                    while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                        read += n;
                        if (total > 0) progress?.Report(0.9 * read / total.Value);
                    }
                }

                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                await Task.Run(() =>
                {
                    using var file = File.OpenRead(archive);
                    using var bz = new BZip2InputStream(file);
                    TarFile.ExtractToDirectory(bz, staging, overwriteFiles: true);
                }, cancellationToken);

                // The archives contain a single top-level folder named like the archive.
                string unpacked = Path.Combine(staging, voice.ArchiveName);
                if (!Directory.Exists(unpacked)) unpacked = Directory.EnumerateDirectories(staging).Single();
                string target = Path.Combine(root, voice.ArchiveName);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(unpacked, target);
                progress?.Report(1.0);
            }
            finally
            {
                try { if (File.Exists(archive)) File.Delete(archive); } catch (IOException) { }
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (IOException) { }
            }
        }
    }
}
