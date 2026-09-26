using FluentAssertions;
using OmenCore.Services;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// Pins the NT SYSTEM_MEMORY_LIST_COMMAND values. EmptyWorkingSets was declared 0 (which is
    /// MemoryCaptureAccessedBits), so the default working-set trim reported success and did nothing.
    /// </summary>
    public class MemoryOptimizerNativeCommandTests
    {
        [Theory]
        [InlineData("EmptyWorkingSets", 2)]
        [InlineData("FlushModifiedList", 3)]
        [InlineData("PurgeStandbyList", 4)]
        [InlineData("PurgeLowPriorityStandbyList", 5)]
        public void MemoryListCommands_MatchTheNtEnum(string operation, int expected)
        {
            MemoryOptimizerService.MemoryListCommandValue(operation).Should().Be(expected);
        }
    }
}
