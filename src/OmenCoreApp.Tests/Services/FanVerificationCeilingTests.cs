using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// A 100% request that settles just short of the table ceiling is the firmware's own maximum
    /// (8E35 at 48/55, 88F7 at 46/55); one stuck at the 60% step's level (88F8, 33/55) is not.
    /// </summary>
    public class FanVerificationCeilingTests
    {
        private static FanApplyResult Result(int requested, int expected, int actual, RpmSource source = RpmSource.Estimated, int rpm = 0) => new()
        {
            RequestedPercent = requested,
            ExpectedLevel = expected,
            ActualLevelAfter = actual,
            RpmSource = source,
            ActualRpmAfter = rpm == 0 ? actual * 100 : rpm
        };

        [Theory]
        [InlineData(48)] // 8E35 (#195)
        [InlineData(46)] // 88F7 (#215)
        [InlineData(44)] // exactly 80%
        public void NearCeilingPlateauAt100_IsTheBoardsMaximum(int actual)
        {
            FanVerificationService.IsFirmwareCeilingPlateau(Result(100, 55, actual)).Should().BeTrue();
        }

        [Fact]
        public void MaxStuckAtThe60PercentLevel_StillFails()
        {
            // 88F8 (#207): Max never rose past 33/55.
            FanVerificationService.IsFirmwareCeilingPlateau(Result(100, 55, 33)).Should().BeFalse();
        }

        [Fact]
        public void BelowOneHundredPercentRequests_AreNeverRelaxed()
        {
            FanVerificationService.IsFirmwareCeilingPlateau(Result(60, 33, 30)).Should().BeFalse();
        }

        [Fact]
        public void PhysicalTachometerReadingZero_Contradicts_AndFails()
        {
            var r = Result(100, 55, 48, RpmSource.EcDirect);
            r.ActualRpmAfter = 0;
            FanVerificationService.IsFirmwareCeilingPlateau(r).Should().BeFalse();
        }
    }
}
