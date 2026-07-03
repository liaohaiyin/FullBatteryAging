using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BatteryAging.Core.Models;
using BatteryAging.Drivers.Adapters;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class CabinetManagerPage : Page
    {
        private readonly CabinetManagerViewModel _vm;

        public CabinetManagerPage(CabinetManagerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _vm.PropertyChanged += Vm_PropertyChanged;
            if (_vm.SelectedCabinet != null)
                ApplyAdapterVisibility(DeviceAdapterRegistry.ResolveMain(_vm.SelectedCabinet));
        }

        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CabinetManagerViewModel.SelectedCabinet)) return;
            var cab = _vm.SelectedCabinet;
            if (cab != null) ApplyAdapterVisibility(DeviceAdapterRegistry.ResolveMain(cab));
        }

        /// <summary>
        /// 用户切换"设备适配器"下拉时：同步派生出旧字段(DriverType/ConnectionType/Protocol)供下层驱动工厂使用，
        /// 并刷新 TCP/串口/CAN 参数分区的可见性，实现"选协议即见对应参数"的可视化配置体验。
        /// </summary>
        private void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo) return;
            if (combo.SelectedItem is not DeviceAdapterDescriptor adapter) return;
            if (combo.DataContext is not Cabinet cab) return;

            cab.DriverType = adapter.DriverType;
            cab.ConnectionType = adapter.ConnectionType;
            cab.Protocol = adapter.Protocol;

            // Cabinet 不实现 INotifyPropertyChanged，直接刷新只读展示框，使其跟随适配器联动
            BindingOperations.GetBindingExpression(DriverTypeCombo, ComboBox.SelectedValueProperty)?.UpdateTarget();
            BindingOperations.GetBindingExpression(ConnectionTypeCombo, ComboBox.SelectedValueProperty)?.UpdateTarget();

            ApplyAdapterVisibility(adapter);
        }

        private void ApplyAdapterVisibility(DeviceAdapterDescriptor adapter)
        {
            if (adapter == null) return;
            TcpParamsPanel.Visibility = adapter.RequiresIp ? Visibility.Visible : Visibility.Collapsed;
            SerialParamsPanel.Visibility = adapter.RequiresSerial ? Visibility.Visible : Visibility.Collapsed;
            CanParamsPanel.Visibility = adapter.RequiresCan ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
