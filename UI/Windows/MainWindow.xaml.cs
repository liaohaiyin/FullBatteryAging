using System.Windows;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Windows
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
