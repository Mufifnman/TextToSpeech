using PlainRules;
using TextToSpeech.Core.CustomFilters;

namespace TextToSpeech.Core.Tests
{
    public class CustomFilterLibraryTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "tts-customfilters-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        private static RuleSet Sample(string name = "Strip ads") => new()
        {
            Name = name,
            Group = "Mine",
            Rules = { new Rule { Kind = RuleKind.RemoveText, Value = "ADVERT" } },
        };

        [Fact]
        public void MissingFolder_LoadsEmpty() =>
            Assert.Empty(new CustomFilterLibrary(folder).Load());

        [Fact]
        public void SaveThenLoad_RoundTrips_AndFilterRuns()
        {
            var lib = new CustomFilterLibrary(folder);
            string path = lib.Save(Sample());
            var entry = Assert.Single(lib.Load());
            Assert.True(entry.IsUsable);
            Assert.Equal("Strip ads", entry.DisplayName);
            Assert.Equal("custom:strip ads", entry.Id);
            Assert.Equal(path, entry.FilePath);
            Assert.Equal("a  b", entry.Compiled!.Apply("a ADVERT b"));
        }

        [Fact]
        public void Save_SameNameTwice_GetsUniqueFiles()
        {
            var lib = new CustomFilterLibrary(folder);
            string a = lib.Save(Sample());
            string b = lib.Save(Sample());
            Assert.NotEqual(a, b);
            Assert.Equal(2, lib.Load().Count);
        }

        [Fact]
        public void Save_ToExistingPath_OverwritesAndKeepsId()
        {
            var lib = new CustomFilterLibrary(folder);
            string path = lib.Save(Sample());
            var renamed = Sample("Renamed");
            lib.Save(renamed, path);
            var entry = Assert.Single(lib.Load());
            Assert.Equal("Renamed", entry.DisplayName);
            Assert.Equal("custom:strip ads", entry.Id);
        }

        [Fact]
        public void Save_RefusesPathsOutsideTheFolder()
        {
            var lib = new CustomFilterLibrary(folder);
            Assert.Throws<ArgumentException>(() => lib.Save(Sample(), Path.Combine(folder, "..", "evil.json")));
            Assert.Throws<ArgumentException>(() => lib.Save(Sample(), Path.Combine(folder, "notjson.txt")));
            Assert.Throws<ArgumentException>(() => lib.Delete(Path.Combine(folder, "..", "x.json")));
        }

        [Fact]
        public void Save_NameWithPathCharacters_StaysInFolder()
        {
            var lib = new CustomFilterLibrary(folder);
            string path = lib.Save(Sample("..\\..\\evil/name"));
            Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(path));
        }

        [Fact]
        public void Delete_RemovesFile()
        {
            var lib = new CustomFilterLibrary(folder);
            string path = lib.Save(Sample());
            lib.Delete(path);
            Assert.Empty(lib.Load());
        }

        [Fact]
        public void BadFiles_BecomeErrorEntries_NotExceptions()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(folder, "badregex.json"),
                "{ \"name\": \"Bad\", \"rules\": [ { \"kind\": \"regex\", \"value\": \"(\" } ] }");
            File.WriteAllText(Path.Combine(folder, "big.json"), new string(' ', (int)CustomFilterLibrary.MaxFileBytes + 1));
            new CustomFilterLibrary(folder).Save(Sample("Good"));

            var entries = new CustomFilterLibrary(folder).Load();
            Assert.Equal(4, entries.Count);
            Assert.Equal(1, entries.Count(e => e.IsUsable));
            Assert.All(entries.Where(e => !e.IsUsable), e => Assert.False(string.IsNullOrWhiteSpace(e.Error)));
        }

        [Fact]
        public void NullRulesAndNullEntries_DoNotThrow()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "nullrules.json"), "{ \"name\": \"A\", \"rules\": null }");
            File.WriteAllText(Path.Combine(folder, "nullentry.json"), "{ \"name\": \"B\", \"rules\": [ null, { \"kind\": \"removeText\", \"value\": \"x\" } ] }");
            var entries = new CustomFilterLibrary(folder).Load();
            Assert.Equal(2, entries.Count);
            Assert.Equal("a", new CustomFilterLibrary(folder).Load().Single(e => e.DisplayName == "B").Compiled!.Apply("axa"));
        }

        [Fact]
        public void HandWrittenFile_WithCommentsAndNoName_UsesFileName()
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "my-filter.json"),
                "// hand made\n{ \"rules\": [ { \"kind\": \"removeText\", \"value\": \"x\", }, ], }");
            var entry = Assert.Single(new CustomFilterLibrary(folder).Load());
            Assert.True(entry.IsUsable);
            Assert.Equal("my-filter", entry.DisplayName);
        }

        [Fact]
        public void NonJsonAndSubfolderFiles_AreIgnored()
        {
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "hi");
            File.WriteAllText(Path.Combine(folder, "sub", "x.json"), "{}");
            Assert.Empty(new CustomFilterLibrary(folder).Load());
        }

        [Fact]
        public void TooManyRules_IsRefused()
        {
            var set = Sample();
            for (int i = 0; i < CustomFilterLibrary.MaxRules + 1; i++)
                set.Rules.Add(new Rule { Kind = RuleKind.RemoveText, Value = "x" + i });
            var lib = new CustomFilterLibrary(folder);
            lib.Save(set);
            var entry = Assert.Single(lib.Load());
            Assert.False(entry.IsUsable);
        }
    }
}
