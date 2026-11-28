using System.IO;
using System.Formats.Tar;
using System.Net.Http;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;

namespace TextToSpeech.Speech
{
    public sealed record DownloadableVoice(string Name, string ArchiveName, string SizeLabel, string Sha256, long ArchiveBytes)
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

        // GUESS: a starter set picked for English reading. Each entry pins the SHA-256 and size of the archive
        // as downloaded on 2026-11-28 (trust on first use), so a replaced or tampered release asset is refused.
        // To add or update a voice: download it once, run `sha256sum`, and add the values here.
        public static readonly IReadOnlyList<DownloadableVoice> Catalog = new[]
        {
            new DownloadableVoice("Amy, US English, quick to start", "vits-piper-en_US-amy-low", "67 MB",
                "c70f5284a09a7fd4ed203b39b2ff51cac1432b422b852eb647b481dade3cf639", 67095344),
            new DownloadableVoice("Lessac, US English, clearer", "vits-piper-en_US-lessac-medium", "67 MB",
                "9e3febfacf0abf4270172d2958bcec246032b7e88efc2720840cc80c93de334e", 67230653),
            new DownloadableVoice("Kokoro, 11 US and UK voices, most natural", "kokoro-en-v0_19", "320 MB",
                "912804855a04745fa77a30be545b3f9a5d15c4d66db00b88cbcd4921df605ac7", 319625534),
        };

        /// <summary>Most the unpacked archive may total. The largest voice unpacks to about 370 MB.</summary>
        internal const long MaxExtractedBytes = 1L << 30;

        public static bool IsInstalled(DownloadableVoice voice) =>
            Directory.Exists(Path.Combine(NeuralSpeechEngine.UserVoicesFolder, voice.ArchiveName));

        /// <summary>Throws when the size or SHA-256 of the downloaded archive is not what the catalog pins.</summary>
        internal static void VerifyDownload(DownloadableVoice voice, long bytes, string sha256Hex)
        {
            if (bytes != voice.ArchiveBytes ||
                !string.Equals(sha256Hex, voice.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"The download for {voice.Name} does not match the expected checksum, so it was discarded. " +
                    "Try again; if it keeps failing the voice file at the source has changed.");
        }

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
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    while ((n = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        read += n;
                        if (read > voice.ArchiveBytes)
                            throw new InvalidDataException($"The download for {voice.Name} is larger than expected; it was discarded.");
                        hash.AppendData(buffer, 0, n);
                        await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
                        if (total > 0) progress?.Report(0.9 * read / total.Value);
                    }
                    VerifyDownload(voice, read, Convert.ToHexString(hash.GetHashAndReset()));
                }

                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                Directory.CreateDirectory(staging);
                await Task.Run(() =>
                {
                    using var file = File.OpenRead(archive);
                    using var bz = new BZip2InputStream(file);
                    using var capped = new CappedReadStream(bz, MaxExtractedBytes);
                    TarFile.ExtractToDirectory(capped, staging, overwriteFiles: true);
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

    /// <summary>Read-only wrapper that throws once more than <c>limit</c> bytes have been read (decompression bomb guard).</summary>
    internal sealed class CappedReadStream : Stream
    {
        private readonly Stream inner;
        private readonly long limit;
        private long total;

        public CappedReadStream(Stream inner, long limit) { this.inner = inner; this.limit = limit; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            Count(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            Count(n);
            return n;
        }

        private void Count(int n)
        {
            total += n;
            if (total > limit) throw new InvalidDataException("The voice archive unpacks to more than the allowed size; it was discarded.");
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
