using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ConnectionInfoTests
    {
        [Fact]
        public void Write_read_delete_round_trip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tts-api-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "api.json");
            try
            {
                var info = new ConnectionInfo(47821, "abc", 1234);
                info.Write(path);
                Assert.Equal(info, ConnectionInfo.Read(path));
                ConnectionInfo.Delete(path);
                Assert.Null(ConnectionInfo.Read(path));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Read_of_a_corrupt_file_is_null()
        {
            string path = Path.GetTempFileName();
            File.WriteAllText(path, "{not json");
            try { Assert.Null(ConnectionInfo.Read(path)); }
            finally { File.Delete(path); }
        }
    }
}
