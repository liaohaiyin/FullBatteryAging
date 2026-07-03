using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    /// <summary>设备校准记录管理 —— 周期性电压/电流标准源比对校准的登记、查询与到期提醒</summary>
    public partial class CalibrationViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly IDialogService _dialog;
        private readonly IAuthService _auth;

        public ObservableCollection<CalibrationRecord> Records { get; } = new();
        public ObservableCollection<Cabinet> Cabinets { get; } = new();

        [ObservableProperty] private CalibrationRecord _selectedRecord;
        [ObservableProperty] private int _dueSoonCount;
        [ObservableProperty] private int _overdueCount;

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewRecordCommand { get; }
        public IAsyncRelayCommand SaveRecordCommand { get; }
        public IAsyncRelayCommand DeleteRecordCommand { get; }

        public CalibrationViewModel(IDataService data, IDialogService dialog, IAuthService auth)
        {
            _data = data;
            _dialog = dialog;
            _auth = auth;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewRecordCommand = new RelayCommand(NewRecord);
            SaveRecordCommand = new AsyncRelayCommand(SaveAsync, () => SelectedRecord != null);
            DeleteRecordCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedRecord != null);

            _ = LoadAsync();
        }

        partial void OnSelectedRecordChanged(CalibrationRecord value)
        {
            SaveRecordCommand.NotifyCanExecuteChanged();
            DeleteRecordCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadAsync()
        {
            try
            {
                var cabs = await _data.GetAllCabinetsAsync();
                Cabinets.Clear();
                foreach (var c in cabs) Cabinets.Add(c);

                var list = await _data.GetCalibrationsAsync();
                Records.Clear();
                foreach (var r in list) Records.Add(r);

                DueSoonCount = Records.Count(r => r.IsDueSoon);
                OverdueCount = Records.Count(r => r.IsOverdue);

                SelectedRecord = Records.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"加载校准记录失败: {ex.Message}");
            }
        }

        private void NewRecord()
        {
            var cab = Cabinets.FirstOrDefault();
            var rec = new CalibrationRecord
            {
                CabinetId = cab?.Id,
                CabinetName = cab?.Name,
                ChannelIndex = cab?.ChannelStartIndex ?? 1,
                CalibrationDate = DateTime.Now,
                NextDueDate = DateTime.Today.AddMonths(6),
                Technician = _auth.CurrentSession.IsAuthenticated ? _auth.CurrentSession.DisplayName : string.Empty,
            };
            Records.Insert(0, rec);
            SelectedRecord = rec;
        }

        private async Task SaveAsync()
        {
            if (SelectedRecord == null) return;
            try
            {
                var cab = Cabinets.FirstOrDefault(c => c.Id == SelectedRecord.CabinetId);
                if (cab != null) SelectedRecord.CabinetName = cab.Name;

                await _data.SaveCalibrationAsync(SelectedRecord);
                await LogAsync("Update", "CalibrationRecord", SelectedRecord.Id,
                    $"校准 {SelectedRecord.CabinetName} CH{SelectedRecord.ChannelIndex}：{(SelectedRecord.IsPassed ? "合格" : "不合格")}");
                await LoadAsync();
                _dialog.ShowMessage("校准记录已保存");
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"保存失败: {ex.Message}");
            }
        }

        private async Task DeleteAsync()
        {
            if (SelectedRecord == null) return;
            if (!_dialog.Confirm($"确定删除该校准记录吗？")) return;
            try
            {
                var id = SelectedRecord.Id;
                var detail = $"删除校准记录: {SelectedRecord.CabinetName} CH{SelectedRecord.ChannelIndex}";
                await _data.DeleteCalibrationAsync(id);
                await LogAsync("Delete", "CalibrationRecord", id, detail);
                Records.Remove(SelectedRecord);
                SelectedRecord = Records.FirstOrDefault();
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
