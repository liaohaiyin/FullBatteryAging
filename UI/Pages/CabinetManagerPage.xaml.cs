using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class CabinetManagerPage : Page
    {
        public CabinetManagerPage(CabinetManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
