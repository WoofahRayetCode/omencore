using FluentAssertions;
using OmenCore.Services.KeyboardLighting;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    public class KeyboardDynamicLightingWarningTests
    {
        [Fact]
        public void FourZone_DynamicLightingOwningDevice_ShowsBanner()
        {
            var state = new DynamicLightingState { GlobalEnabled = true, DeviceFound = true, DeviceEnabled = true };
            LightingViewModel.BuildKeyboardDynamicLightingWarning(state, isPerKey: false)
                .Should().Contain("Dynamic Lighting");
        }

        [Fact]
        public void DeviceNotRegisteredWithWindows_NoBanner()
        {
            var state = new DynamicLightingState { GlobalEnabled = true, DeviceFound = false };
            LightingViewModel.BuildKeyboardDynamicLightingWarning(state, isPerKey: false).Should().BeEmpty();
        }

        [Fact]
        public void DynamicLightingOff_NoBanner()
        {
            var state = new DynamicLightingState { GlobalEnabled = false, DeviceFound = true, DeviceEnabled = true };
            LightingViewModel.BuildKeyboardDynamicLightingWarning(state, isPerKey: false).Should().BeEmpty();
        }

        [Fact]
        public void PerKeyKeyboards_UseTheMapEditorBannerInstead()
        {
            var state = new DynamicLightingState { GlobalEnabled = true, DeviceFound = true, DeviceEnabled = true };
            LightingViewModel.BuildKeyboardDynamicLightingWarning(state, isPerKey: true).Should().BeEmpty();
        }

        [Fact]
        public void NoState_NoBanner()
        {
            LightingViewModel.BuildKeyboardDynamicLightingWarning(null, isPerKey: false).Should().BeEmpty();
        }
    }
}
