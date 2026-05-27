using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class BatchAnalysisPage : Page
    {
        public BatchAnalysisPage(BatchAnalysisViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
