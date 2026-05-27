using BatteryAging.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BatteryAging.UI.Windows
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_MinButton") is Button btnMin)
                btnMin.Click += (_, _) => WindowState = WindowState.Minimized;

            if (GetTemplateChild("PART_MaxButton") is Button btnMax)
                btnMax.Click += (_, _) =>
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;

            if (GetTemplateChild("PART_CloseButton") is Button btnClose)
                btnClose.Click += (_, _) => Close();
        }
    }
}
