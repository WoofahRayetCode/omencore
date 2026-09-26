using FluentAssertions;
using OmenCore.Views;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>Ctrl+1..9 counts visible navigation pages only.</summary>
    public class NavShortcutTests
    {
        [Fact]
        public void AllVisible_NumberMapsToSameIndex()
        {
            MainWindow.ResolveNavShortcutIndex(new[] { true, true, true }, 2).Should().Be(1);
        }

        [Fact]
        public void HiddenPages_AreSkipped()
        {
            // General, (hidden Tuning), (hidden Diagnostics), Monitoring
            MainWindow.ResolveNavShortcutIndex(new[] { true, false, false, true }, 2).Should().Be(3);
        }

        [Fact]
        public void NumberBeyondVisibleCount_DoesNothing()
        {
            MainWindow.ResolveNavShortcutIndex(new[] { true, false, true }, 3).Should().Be(-1);
        }
    }
}
