using TextToSpeech.Core.IO;
using Xunit;

namespace TextToSpeech.Core.Tests;

public class JsonFileStoreTests : IDisposable
{
    public class Doc
    {
        public string Name { get; set; } = "default";
        public int Count { get; set; } = 3;
    }

    private readonly string folder = Path.Combine(Path.GetTempPath(), "tts-store-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(folder, "doc.json");

    public JsonFileStoreTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var store = new JsonFileStore<Doc>(FilePath);
        var doc = store.Load();
        Assert.Equal("default", doc.Name);
        Assert.Null(store.LastProblem);
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void SaveThenLoadRoundTripsAndWritesTheVersionFirst()
    {
        var store = new JsonFileStore<Doc>(FilePath, currentVersion: 2);
        store.Save(new Doc { Name = "kept", Count = 9 });
        string text = File.ReadAllText(FilePath);
        Assert.StartsWith("{", text.TrimStart());
        Assert.True(text.IndexOf("\"version\"", StringComparison.Ordinal) < text.IndexOf("\"Name\"", StringComparison.Ordinal));

        var loaded = new JsonFileStore<Doc>(FilePath, currentVersion: 2);
        var doc = loaded.Load();
        Assert.Equal("kept", doc.Name);
        Assert.Equal(9, doc.Count);
        Assert.Equal(2, loaded.LoadedVersion);
    }

    [Fact]
    public void OldFileWithoutVersionLoadsAsVersionZero()
    {
        File.WriteAllText(FilePath, "{\"Name\":\"old\"}");
        var store = new JsonFileStore<Doc>(FilePath);
        Assert.Equal("old", store.Load().Name);
        Assert.Equal(0, store.LoadedVersion);
        Assert.Null(store.LastProblem);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{\"Count\":\"many\"}")]
    public void CorruptFileIsKeptAsBadAndDefaultsReturned(string content)
    {
        File.WriteAllText(FilePath, content);
        var store = new JsonFileStore<Doc>(FilePath);
        var doc = store.Load();
        Assert.Equal("default", doc.Name);
        Assert.NotNull(store.LastProblem);
        Assert.Equal(content, File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void FileFromANewerVersionIsReadAndBackedUpBeforeBeingOverwritten()
    {
        File.WriteAllText(FilePath, "{\"version\":5,\"Name\":\"future\",\"Extra\":1}");
        var store = new JsonFileStore<Doc>(FilePath, currentVersion: 1);
        var doc = store.Load();
        Assert.Equal("future", doc.Name);
        Assert.Equal(5, store.LoadedVersion);

        store.Save(doc);
        Assert.Contains("Extra", File.ReadAllText(FilePath + ".v5.bak"));
        Assert.DoesNotContain("Extra", File.ReadAllText(FilePath));
        Assert.Contains("\"version\": 1", File.ReadAllText(FilePath));
    }

    [Fact]
    public void SaveLeavesNoTempFiles()
    {
        var store = new JsonFileStore<Doc>(FilePath);
        store.Save(new Doc());
        store.Save(new Doc { Name = "again" });
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(folder));
    }
}
