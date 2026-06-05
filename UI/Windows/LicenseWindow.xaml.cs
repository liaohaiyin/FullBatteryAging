using System.Windows;
using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Windows
{
    public partial class LicenseWindow : Window
    {
        public LicenseWindow(LicenseWindowViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (GetTemplateChild("PART_MinButton") is Button btnMin)
                btnMin.Click += (_, _) => WindowState = WindowState.Minimized;
            if (GetTemplateChild("PART_CloseButton") is Button btnClose)
                btnClose.Click += (_, _) => { DialogResult = false; };
        }

        private void EnterButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void ExitButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}