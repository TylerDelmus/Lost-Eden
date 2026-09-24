using LostEden.Vehicles;
using N3Lite;
using Xunit;

namespace LostEden.Movement.Tests
{
    public class CharMovementRulesTests
    {
        [Theory]
        [InlineData(3, 1, 0f, 5f)]
        [InlineData(3, 1, 275f, 6f)]
        [InlineData(3, 1, 2200f, 13f)]
        [InlineData(3, 1, 10000f, 13f)]
        [InlineData(3, 2, 0f, 3f)]
        [InlineData(3, 2, 275f, 3.7f)]
        [InlineData(4, 1, 0f, 3f)]
        [InlineData(4, 1, 440f, 4f)]
        [InlineData(7, 1, 0f, 7f)]
        [InlineData(7, 1, 275f, 8f)]
        [InlineData(2, 1, 5000f, 1.5f)]
        [InlineData(5, 1, 5000f, 1f)]
        public void MaxSpeedFollowsTheStateCurve(int state, int direction, float stat, float expected)
            => Assert.Equal(expected, CharMovementRules.MaxSpeed(state, direction, stat), 3);

        [Theory]
        [InlineData(3, 0f, 2.5f)]
        [InlineData(3, 275f, 3f)]
        [InlineData(3, 10000f, 6.5f)]
        [InlineData(7, 0f, 3.5f)]
        [InlineData(2, 0f, 1.5f)]
        public void StrafeSpeedIsHalfTheForwardCurve(int state, float stat, float expected)
            => Assert.Equal(expected, CharMovementRules.StrafeSpeed(state, 1, stat), 3);

        [Fact]
        public void ReverseStrafeFollowsTheReverseCurve()
            => Assert.Equal(0.5f * 275f / (275f / 0.7f) + 1.5f, CharMovementRules.StrafeSpeed(3, 2, 275f), 4);

        [Theory]
        [InlineData(1, false)]
        [InlineData(8, false)]
        [InlineData(9, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        [InlineData(7, true)]
        public void APlayerRefusesDriveInStatesOneEightAndNine(int state, bool canDrive)
            => Assert.Equal(canDrive, CharMovementRules.Profile(state, 275f, isNpc: false).CanDrive);

        [Theory]
        [InlineData(1, false)]
        [InlineData(8, true)]
        [InlineData(9, true)]
        public void AnNpcRefusesDriveOnlyInStateOne(int state, bool canDrive)
            => Assert.Equal(canDrive, CharMovementRules.Profile(state, 275f, isNpc: true).CanDrive);

        [Fact]
        public void SitDropsInputAndFlyTurnsGravityOff()
        {
            Assert.False(CharMovementRules.Profile(8, 0f, false).AcceptsInput);
            Assert.True(CharMovementRules.Profile(3, 0f, false).AcceptsInput);
            Assert.True(CharMovementRules.Profile(7, 0f, false).Flying);
            Assert.False(CharMovementRules.Profile(3, 0f, false).Flying);
        }

        [Fact]
        public void TheRunProfileAtNoStatIsTheLibraryDefault()
        {
            MovementProfile run = CharMovementRules.Profile(3, 0f, false);
            MovementProfile lib = MovementProfile.Default;

            Assert.Equal(lib.ForwardSpeed, run.ForwardSpeed);
            Assert.Equal(lib.ReverseSpeed, run.ReverseSpeed);
            Assert.Equal(lib.StrafeSpeed, run.StrafeSpeed);
            Assert.Equal(lib.ReverseStrafeSpeed, run.ReverseStrafeSpeed);
        }

        [Theory]
        [InlineData(0, 0, 0, 1f)]
        [InlineData(300, 300, 0, 4f)]
        [InlineData(100, 50, 0, 1.75f)]
        [InlineData(600, 600, 0, 5f)]
        [InlineData(600, 600, 1, 7f)]
        [InlineData(-200, -200, 0, 0.5f)]
        public void JumpHeightFromStats(int strength, int agility, int gmLevel, float expected)
            => Assert.Equal(expected, CharMovementRules.JumpHeight(strength, agility, gmLevel), 5);

        [Fact]
        public void BodyHeightIsTwiceTheScale()
            => Assert.Equal(2.4f, CharMovementRules.BodyHeight(1.2f), 5);
    }
}
