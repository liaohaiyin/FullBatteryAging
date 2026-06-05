using BatteryAging.Services;
using BatteryAging.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace BatteryAging.UI.Windows
{
    public partial class MainWindow : Window
    {
        private readonly IThemeService _theme;
        public MainWindow(MainWindowViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            _theme = App.Services?.GetService<IThemeService>();
            if (_theme != null)
            {
                ThemeCombo.ItemsSource = _theme.GetAvailableThemes();
                ThemeCombo.SelectedValue = _theme.CurrentTheme;
            }
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
        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_theme != null && ThemeCombo.SelectedValue is string code)
                _theme.ApplyTheme(code);   // 与当前主题相同时内部已忽略
        }
    }
}
