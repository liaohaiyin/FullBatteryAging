using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public partial class CabinetManagerViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly IDialogService _dialog;

        public ObservableCollection<Cabinet> Cabinets { get; } = new();
        public List<EnumDisplayItem<DriverType>> DriverTypes { get; }
            = EnumHelper.GetItems<DriverType>();
        public List<EnumDisplayItem<ConnectionType>> ConnectionTypes { get; }
            = EnumHelper.GetItems<ConnectionType>();
        public List<EnumDisplayItem<CabinetType>> CabinetTypes { get; } = 
            EnumHelper.GetItems<CabinetType>();

        [ObservableProperty] private Cabinet _selectedCabinet;

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewCabinetCommand { get; }
        public IAsyncRelayCommand SaveCabinetCommand { get; }
        public IAsyncRelayCommand DeleteCabinetCommand { get; }

        public CabinetManagerViewModel(IDataService data, IDialogService dialog)
        {
            _data = data;
            _dialog = dialog;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewCabinetCommand = new RelayCommand(NewCabinet);
            SaveCabinetCommand = new AsyncRelayCommand(SaveAsync, () => SelectedCabinet != null);
            DeleteCabinetCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedCabinet != null);

            _ = LoadAsync();
        }

        partial void OnSelectedCabinetChanged(Cabinet value)
        {
            SaveCabinetCommand.NotifyCanExecuteChanged();
            DeleteCabinetCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadAsync()
        {
            try
            {
                var list = await _data.GetAllCabinetsAsync();
                Cabinets.Clear();
                foreach (var c in list) Cabinets.Add(c);
                if (Cabinets.Count == 0)
                {
                    // 首次：插入一个默认模拟机柜
                    var def = new Cabinet
                    {
                        Name = "机柜1",
                        CabinetIndex = 1,
                        DriverType = DriverType.Simulator,
                        ConnectionType = ConnectionType.Simulation,
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
                Name = $"机柜 {Cabinets.Count + 1}",
                CabinetIndex = Cabinets.Count + 1,
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
                await _data.DeleteCabinetAsync(SelectedCabinet.Id);
                Cabinets.Remove(SelectedCabinet);
                SelectedCabinet = Cabinets.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"删除失败: {ex.Message}");
            }
        }
    }
}
