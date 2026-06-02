using System.Windows;
using System.Windows.Controls;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class UserManagementPage : Page
    {
        private readonly UserManagementViewModel _vm;

        public UserManagementPage(UserManagementViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // 切换编辑状态时清空密码框
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(vm.IsEditing)) return;
                if (!vm.IsEditing)
                {
                    PwdBox.Password = string.Empty;
                    ConfirmPwdBox.Password = string.Empty;
                }
            };
        }

        // PasswordBox 不能直接双向绑定，手动同步到 ViewModel
        private void PwdBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_vm != null) _vm.EditPassword = PwdBox.Password;
        }

        private void ConfirmPwdBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_vm != null) _vm.EditConfirmPwd = ConfirmPwdBox.Password;
        }
    }
}
