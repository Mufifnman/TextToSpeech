using TextToSpeech.Speech;

namespace TextToSpeech.Core.Tests
{
    public class SilenceTrimmerTests
    {
        // 1000 Hz sample rate keeps the numbers readable: 100 samples = 0.1 s.
        private static float[] Clip(int silenceBefore, int sound, int silenceAfter) =>
            new float[silenceBefore].Concat(Enumerable.Repeat(0.5f, sound)).Concat(new float[silenceAfter]).ToArray();

        [Fact]
        public void LongSilenceAtBothEnds_IsCutDownToTheKeptAmount()
        {
            var result = SilenceTrimmer.Trim(Clip(300, 200, 400), 1000, leading: true, trailing: true, keepSeconds: 0.06);

            Assert.Equal(60 + 200 + 60, result.Length);
        }

        [Fact]
        public void OnlyTheRequestedEndIsTrimmed()
        {
            var clip = Clip(300, 200, 400);

            Assert.Equal(60 + 200 + 400, SilenceTrimmer.Trim(clip, 1000, leading: true, trailing: false, keepSeconds: 0.06).Length);
            Assert.Equal(300 + 200 + 60, SilenceTrimmer.Trim(clip, 1000, leading: false, trailing: true, keepSeconds: 0.06).Length);
            Assert.Same(clip, SilenceTrimmer.Trim(clip, 1000, leading: false, trailing: false));
        }

        [Fact]
        public void ShortSilence_IsLeftAlone()
        {
            var clip = Clip(30, 200, 20);

            Assert.Same(clip, SilenceTrimmer.Trim(clip, 1000, leading: true, trailing: true, keepSeconds: 0.06));
        }

        [Fact]
        public void AClipWithNoSound_IsReturnedUnchanged_AndEmptyIsSafe()
        {
            var quiet = new float[500];
            Assert.Same(quiet, SilenceTrimmer.Trim(quiet, 1000, true, true));
            Assert.Empty(SilenceTrimmer.Trim(Array.Empty<float>(), 1000, true, true));
        }

        [Fact]
        public void TheSoundItselfIsNeverCut()
        {
            var result = SilenceTrimmer.Trim(Clip(300, 200, 400), 1000, true, true, keepSeconds: 0);

            Assert.Equal(200, result.Length);
            Assert.All(result, s => Assert.Equal(0.5f, s));
        }
    }
}
