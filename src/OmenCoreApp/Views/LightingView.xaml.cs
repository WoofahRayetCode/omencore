using System.Windows;
using System.Windows.Controls;
using OmenCore.ViewModels;

namespace OmenCore.Views
{
    public partial class LightingView : UserControl
    {
        public LightingView()
        {
            InitializeComponent();

            // The likeliest reason someone leaves this page is to change the Windows Dynamic
            // Lighting setting the banner told them about; re-read it when they come back.
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is true && DataContext is LightingViewModel vm)
                    vm.RefreshKeyboardDynamicLighting();
            };
        }
    }
}
