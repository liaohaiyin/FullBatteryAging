using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class CellHeatmapPage : Page
    {
        public CellHeatmapPage(CellHeatmapViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
