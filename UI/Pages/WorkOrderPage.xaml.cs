using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class WorkOrderPage : Page
    {
        public WorkOrderPage(WorkOrderViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
