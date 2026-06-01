using Microsoft.Win32;
using System.Windows;
using System.Windows.Media.Media3D;

namespace BatteryAging.Services
{
    public interface IDialogService
    {
        void ShowMessage(string message, string title = "提示");
        void ShowError(string message, string title = "错误");
        void ShowWarning(string message, string title = "警告");
        bool Confirm(string message, string title = "确认");
        string OpenFileDialog(string filter, string title = "打开");
        string SaveFileDialog(string filter, string defaultFileName = "", string title = "保存");
        string InputDialog(string prompt, string title = "输入", string defaultValue = "");
    }

    public class DialogService : IDialogService
    {
        public void ShowMessage(string message, string title = "提示") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowError(string message, string title = "错误") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public void ShowWarning(string message, string title = "警告") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public bool Confirm(string message, string title = "确认") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        public string OpenFileDialog(string filter, string title = "打开")
        {
            var dialog = new OpenFileDialog { Filter = filter, Title = title };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string SaveFileDialog(string filter, string defaultFileName = "", string title = "保存")
        {
            var dialog = new SaveFileDialog { Filter = filter, Title = title, FileName = defaultFileName };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string InputDialog(string prompt, string title = "输入", string defaultValue = "")
        {
            var win = new UI.Windows.InputDialog
            {
                Title = title,
                Prompt = prompt,
                InputText = defaultValue,
                MinHeight = 230,
                Owner = Application.Current?.MainWindow
            };
            return win.ShowDialog() == true ? win.InputText : null;
        }
    }
}
