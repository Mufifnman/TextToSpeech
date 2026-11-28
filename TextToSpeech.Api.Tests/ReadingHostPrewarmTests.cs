using TextToSpeech.Api;
using Xunit;

namespace TextToSpeech.Api.Tests
{
    public class ReadingHostPrewarmTests
    {
        private readonly HostRig rig = new();

        private Task<PrewarmResponse> Hint(string text, string? agent = null) =>
            rig.Host.PrewarmAsync(new PrewarmRequest(text, agent == null ? null : new TextToSpeech.Api.Arbitration.AgentRef(agent, null, null, null, null)));

        private Task<SpeakResponse> SpeakAs(string text, string? agent = null) =>
            rig.Host.SpeakAsync(new SpeakRequest(text, "claude-code", null, null, null, null, null, null,
                Agent: agent == null ? null : new TextToSpeech.Api.Arbitration.AgentRef(agent, null, null, null, null)));

        [Fact]
        public async Task AHint_HandsTheSentencesAsTheyWillBeSpoken_ToTheEngine()
        {
            var response = await Hint("First one. Second one.");

            Assert.Equal(new[] { "First one.", "Second one." }, rig.Engine.Hints.Last().Texts);
            Assert.Equal("default", rig.Engine.Hints.Last().Key);
            Assert.Equal(1, response.Stats.Sent);
            Assert.Equal("active", Assert.Single(response.Hints).State);
        }

        [Fact]
        public async Task ANewerHint_ReplacesTheOldOne()
        {
            await Hint("Old text here.");
            var response = await Hint("New text here.");

            Assert.Equal(new[] { "New text here." }, rig.Engine.Hints.Last().Texts);
            Assert.Equal(1, response.Stats.Replaced);
            Assert.Single(response.Hints);
        }

        [Fact]
        public async Task HintsFromDifferentCallers_AreSeparate()
        {
            await Hint("From window one.", "w1");
            var response = await Hint("From window two.", "w2");

            Assert.Equal(2, response.Hints.Count);
            Assert.Equal(0, response.Stats.Replaced);
        }

        [Fact]
        public async Task Reading_TheHintedText_ConfirmsIt()
        {
            await Hint("Build finished. Three tests failed.");
            await SpeakAs("Build finished. Three tests failed.");

            var status = await rig.Host.GetPrewarmAsync();
            Assert.Equal(1, status.Stats.Confirmed);
            Assert.Equal("used", Assert.Single(status.Hints).State);
        }

        [Fact]
        public async Task Reading_MoreThanTheHint_StillConfirmsIt()
        {
            await Hint("Build finished.");
            await SpeakAs("Build finished. Then more text follows.");

            Assert.Equal(1, (await rig.Host.GetPrewarmAsync()).Stats.Confirmed);
        }

        [Fact]
        public async Task Reading_DifferentText_ReplacesTheHintWithTheFinalVersion()
        {
            await Hint("Build finished. Three tests failed.");
            await SpeakAs("Build finished. Two tests failed.");

            var status = await rig.Host.GetPrewarmAsync();
            Assert.Equal(1, status.Stats.Replaced);
            Assert.Equal(0, status.Stats.Confirmed);
            Assert.Equal(new[] { "Build finished.", "Two tests failed." }, rig.Engine.Hints.Last().Texts);   // the shared first sentence is kept
        }

        [Fact]
        public async Task ACancelledHint_IsWithdrawnFromTheEngine()
        {
            await Hint("Maybe later.");
            var response = await rig.Host.CancelPrewarmAsync(new CancelPrewarmRequest());

            Assert.Empty(rig.Engine.Hints.Last().Texts);
            Assert.Equal(1, response.Stats.Cancelled);
            Assert.Empty(response.Hints);
        }

        [Fact]
        public async Task AHintNobodyReads_TimesOutAfterTwoMinutes()
        {
            await Hint("Nobody reads this.");

            rig.Now += ReadingHost.HintLife - TimeSpan.FromSeconds(1);
            Assert.Equal(0, (await rig.Host.GetPrewarmAsync()).Stats.TimedOut);

            rig.Now += TimeSpan.FromSeconds(2);
            var status = await rig.Host.GetPrewarmAsync();
            Assert.Equal(1, status.Stats.TimedOut);
            Assert.Empty(status.Hints);
            Assert.Empty(rig.Engine.Hints.Last().Texts);
        }

        [Fact]
        public async Task OnlyTheFirstSentences_AreHinted()
        {
            var many = string.Join(" ", Enumerable.Range(1, 30).Select(i => $"Sentence number {i}."));
            await Hint(many);

            Assert.Equal(ReadingHost.MaxHintSentences, rig.Engine.Hints.Last().Texts.Length);
        }

        [Fact]
        public async Task TooManyCallers_PushOutTheHintThatExpiresFirst()
        {
            for (int i = 0; i < ReadingHost.MaxHintCallers; i++)
            {
                await Hint($"Text from caller {i}.", "w" + i);
                rig.Now += TimeSpan.FromSeconds(1);
            }
            var response = await Hint("One caller too many.", "extra");

            Assert.Equal(ReadingHost.MaxHintCallers, response.Hints.Count);
            Assert.DoesNotContain(response.Hints, h => h.Key == "w0");   // the oldest went
            Assert.Contains(response.Hints, h => h.Key == "extra");
            Assert.Equal(1, response.Stats.Cancelled);
            Assert.Empty(rig.Engine.Hints.Where(h => h.Key == "w0").Last().Texts);   // and its audio was withdrawn
        }

        [Fact]
        public async Task AVeryLongText_IsOnlyLookedAtAtTheStart()
        {
            var huge = string.Join(" ", Enumerable.Range(1, 5000).Select(i => $"Sentence number {i}."));
            await Hint(huge);

            Assert.Equal(ReadingHost.MaxHintSentences, rig.Engine.Hints.Last().Texts.Length);
        }

        [Fact]
        public async Task AHintWithNothingToSay_IsRejected()
        {
            var e = await Assert.ThrowsAsync<ApiException>(() => Hint("   "));
            Assert.Equal("no_speakable_text", e.Code);
        }
    }
}
