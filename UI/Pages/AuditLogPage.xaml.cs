using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class AuditLogPage : Page
    {
        public AuditLogPage(AuditLogViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
