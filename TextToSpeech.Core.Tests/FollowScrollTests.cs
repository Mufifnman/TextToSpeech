using TextToSpeech.Core.Shell;
using Xunit;

namespace TextToSpeech.Core.Tests
{
    public class FollowScrollTests
    {
        [Fact]
        public void InTheMiddleThird_TheViewStaysStill()
        {
            Assert.Null(FollowScroll.TargetOffset(top: 540, bottom: 560, offset: 200, viewport: 900));   // center 350 of 900
        }

        [Fact]
        public void NearTheBottomThird_TheSentenceIsCentered()
        {
            Assert.Equal(560.0, FollowScroll.TargetOffset(top: 1000, bottom: 1020, offset: 200, viewport: 900));
        }

        [Fact]
        public void BelowOrAboveTheView_IsBroughtBack_ButNeverBeforeTheStart()
        {
            Assert.Equal(4560.0, FollowScroll.TargetOffset(5000, 5020, 0, 900));
            Assert.Equal(0.0, FollowScroll.TargetOffset(100, 120, 800, 900));   // would be negative: clamps to the top
        }

        [Fact]
        public void ATallSentence_KeepsItsStartAQuarterFromTheTop()
        {
            Assert.Null(FollowScroll.TargetOffset(top: 1100, bottom: 1700, offset: 1000, viewport: 800));      // start 100 down: fine
            Assert.Equal(1800.0, FollowScroll.TargetOffset(top: 2000, bottom: 2600, offset: 1000, viewport: 800));
        }

        [Fact]
        public void NoViewHeight_DoesNothing()
        {
            Assert.Null(FollowScroll.TargetOffset(0, 10, 0, 0));
        }
    }
}
