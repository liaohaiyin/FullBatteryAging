using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers;
using BatteryAging.Drivers.Adapters;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public partial class CabinetManagerViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly IDialogService _dialog;
        private readonly IAuthService _auth;

        public ObservableCollection<Cabinet> Cabinets { get; } = new();
        public List<EnumDisplayItem<DriverType>> DriverTypes { get; }
            = EnumHelper.GetItems<DriverType>();
        public List<EnumDisplayItem<ConnectionType>> ConnectionTypes { get; }
            = EnumHelper.GetItems<ConnectionType>();
        public List<EnumDisplayItem<CabinetType>> CabinetTypes { get; } =
            EnumHelper.GetItems<CabinetType>();
        public List<EnumDisplayItem<BmsDriverType>> BmsDriverTypes { get; }
            = EnumHelper.GetItems<BmsDriverType>();

        /// <summary>标准化驱动适配层中所有支持主设备的品牌/协议适配器，供设备配置界面选择</summary>
        public List<DeviceAdapterDescriptor> Adapters { get; }
            = DeviceAdapterRegistry.All.Where(a => a.SupportsMainDriver).ToList();

        [ObservableProperty] private Cabinet _selectedCabinet;
        [ObservableProperty] private string _connectionTestResult;
        [ObservableProperty] private bool _connectionTestOk;
        [ObservableProperty] private bool _isTestingConnection;

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewCabinetCommand { get; }
        public IAsyncRelayCommand SaveCabinetCommand { get; }
        public IAsyncRelayCommand DeleteCabinetCommand { get; }
        public IAsyncRelayCommand TestConnectionCommand { get; }

        public CabinetManagerViewModel(IDataService data, IDialogService dialog, IAuthService auth)
        {
            _data = data;
            _dialog = dialog;
            _auth = auth;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewCabinetCommand = new RelayCommand(NewCabinet);
            SaveCabinetCommand = new AsyncRelayCommand(SaveAsync, () => SelectedCabinet != null);
            DeleteCabinetCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedCabinet != null);
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => SelectedCabinet != null && !IsTestingConnection);

            _ = LoadAsync();
        }

        partial void OnSelectedCabinetChanged(Cabinet value)
        {
            SaveCabinetCommand.NotifyCanExecuteChanged();
            DeleteCabinetCommand.NotifyCanExecuteChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            ConnectionTestResult = null;
        }

        /// <summary>
        /// 按当前机柜选中的适配器，短接创建一次驱动并尝试连接/心跳，验证参数是否正确 —— 无需重启整个程序。
        /// </summary>
        private async Task TestConnectionAsync()
        {
            if (SelectedCabinet == null) return;

            IsTestingConnection = true;
            TestConnectionCommand.NotifyCanExecuteChanged();
            ConnectionTestResult = null;
            IDeviceDriver driver = null;
            try
            {
                var adapter = DeviceAdapterRegistry.ResolveMain(SelectedCabinet);
                var sampleMs = SelectedCabinet.SamplingIntervalMs > 0 ? SelectedCabinet.SamplingIntervalMs : 1000;
                driver = adapter.CreateDriver(SelectedCabinet, sampleMs);

                var connected = await driver.ConnectAsync();
                if (connected) connected = await driver.PingAsync();

                ConnectionTestOk = connected;
                ConnectionTestResult = connected
                    ? $"[{adapter.DisplayName}] 连接成功"
                    : $"[{adapter.DisplayName}] 连接失败，请检查参数";
            }
            catch (Exception ex)
            {
                ConnectionTestOk = false;
                ConnectionTestResult = $"连接测试异常: {ex.Message}";
            }
            finally
            {
                if (driver != null)
                {
                    try { await driver.DisconnectAsync(); } catch { }
                    try { driver.Dispose(); } catch { }
                }
                IsTestingConnection = false;
                TestConnectionCommand.NotifyCanExecuteChanged();
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                var list = await _data.GetAllCabinetsAsync();
                Cabinets.Clear();
                foreach (var c in list)
                {
                    // 历史数据没有 AdapterId：按 DriverType/ConnectionType 自动回填，使适配器下拉框正确回显选中项
                    if (string.IsNullOrEmpty(c.AdapterId))
                        c.AdapterId = DeviceAdapterRegistry.ResolveMain(c).Id;
                    Cabinets.Add(c);
                }
                if (Cabinets.Count == 0)
                {
                    // 首次：插入一个默认模拟机柜
                    var def = new Cabinet
                    {
                        Name = "机柜1",
                        CabinetIndex = 1,
                        DriverType = DriverType.Simulator,
                        ConnectionType = ConnectionType.Tcp,
                        AdapterId = DeviceAdapterRegistry.SimulatorId,
                        ChannelStartIndex = 1,
                        ChannelCount = 8,
                        IsEnabled = true
                    };
                    await _data.SaveCabinetAsync(def);
                    Cabinets.Add(def);
                }
                SelectedCabinet = Cabinets.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"加载机柜失败: {ex.Message}");
            }
        }

        private void NewCabinet()
        {
            var cab = new Cabinet
            {
                Name = $"机柜{Cabinets.Count + 1}",
                CabinetIndex = Cabinets.Count + 1,
                AdapterId = DeviceAdapterRegistry.SimulatorId,
                ChannelStartIndex = Cabinets.Sum(c => c.ChannelCount) + 1,
                ChannelCount = 8
            };
            Cabinets.Add(cab);
            SelectedCabinet = cab;
        }

        private async Task SaveAsync()
        {
            if (SelectedCabinet == null) return;
            try
            {
                await _data.SaveCabinetAsync(SelectedCabinet);
                await LogAsync("Update", "Cabinet", SelectedCabinet.Id, $"保存机柜配置: {SelectedCabinet.Name}");
                _dialog.ShowMessage("机柜配置已保存\n重启程序后生效");
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"保存失败: {ex.Message}");
            }
        }

        private async Task DeleteAsync()
        {
            if (SelectedCabinet == null) return;
            if (!_dialog.Confirm($"确定删除机柜 '{SelectedCabinet.Name}' 吗?")) return;
            try
            {
                var name = SelectedCabinet.Name;
                var id = SelectedCabinet.Id;
                await _data.DeleteCabinetAsync(id);
                await LogAsync("Delete", "Cabinet", id, $"删除机柜: {name}");
                Cabinets.Remove(SelectedCabinet);
                SelectedCabinet = Cabinets.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"删除失败: {ex.Message}");
            }
        }

        private async Task LogAsync(string action, string entityType, string entityId, string detail)
        {
            try
            {
                var s = _auth.CurrentSession;
                await _data.LogAuditAsync(new AuditLog
                {
                    UserId = s.UserId,
                    Username = s.IsAuthenticated ? s.Username : "system",
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Detail = detail
                });
            }
            catch { /* 审计日志失败不应阻断主业务操作 */ }
        }
    }
}
