using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class CalibrationPage : Page
    {
        public CalibrationPage(CalibrationViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
