using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class UtilizationPage : Page
    {
        public UtilizationPage(UtilizationViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
