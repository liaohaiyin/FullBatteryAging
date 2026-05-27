using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class RecipeEditorPage : Page
    {
        public RecipeEditorPage(RecipeEditorViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
