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
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    /// <summary>工单管理 —— 绑定生产批次/产线/操作员，测试记录可关联工单用于统计与追溯</summary>
    public partial class WorkOrderViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly IDialogService _dialog;
        private readonly IAuthService _auth;

        public ObservableCollection<WorkOrder> Orders { get; } = new();
        public List<EnumDisplayItem<WorkOrderStatus>> StatusItems { get; } = EnumHelper.GetItems<WorkOrderStatus>();

        [ObservableProperty] private WorkOrder _selectedOrder;

        public IAsyncRelayCommand LoadCommand { get; }
        public IRelayCommand NewOrderCommand { get; }
        public IAsyncRelayCommand SaveOrderCommand { get; }
        public IAsyncRelayCommand DeleteOrderCommand { get; }

        public WorkOrderViewModel(IDataService data, IDialogService dialog, IAuthService auth)
        {
            _data = data;
            _dialog = dialog;
            _auth = auth;

            LoadCommand = new AsyncRelayCommand(LoadAsync);
            NewOrderCommand = new RelayCommand(NewOrder);
            SaveOrderCommand = new AsyncRelayCommand(SaveAsync, () => SelectedOrder != null);
            DeleteOrderCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedOrder != null);

            _ = LoadAsync();
        }

        partial void OnSelectedOrderChanged(WorkOrder value)
        {
            SaveOrderCommand.NotifyCanExecuteChanged();
            DeleteOrderCommand.NotifyCanExecuteChanged();
        }

        private async Task LoadAsync()
        {
            try
            {
                var list = await _data.GetWorkOrdersAsync();
                Orders.Clear();
                foreach (var o in list) Orders.Add(o);
                SelectedOrder = Orders.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"加载工单失败: {ex.Message}");
            }
        }

        private void NewOrder()
        {
            var order = new WorkOrder
            {
                WorkOrderNo = $"WO{DateTime.Now:yyMMddHHmmss}",
                Operator = _auth.CurrentSession.IsAuthenticated ? _auth.CurrentSession.DisplayName : string.Empty,
            };
            Orders.Insert(0, order);
            SelectedOrder = order;
        }

        private async Task SaveAsync()
        {
            if (SelectedOrder == null) return;
            try
            {
                if (SelectedOrder.Status == WorkOrderStatus.Completed && SelectedOrder.CompletedTime == null)
                    SelectedOrder.CompletedTime = DateTime.Now;

                await _data.SaveWorkOrderAsync(SelectedOrder);
                await LogAsync("Update", "WorkOrder", SelectedOrder.Id, $"保存工单: {SelectedOrder.WorkOrderNo}");
                await LoadAsync();
                _dialog.ShowMessage("工单已保存");
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"保存失败: {ex.Message}");
            }
        }

        private async Task DeleteAsync()
        {
            if (SelectedOrder == null) return;
            if (!_dialog.Confirm($"确定删除工单 '{SelectedOrder.WorkOrderNo}' 吗？")) return;
            try
            {
                var id = SelectedOrder.Id;
                var no = SelectedOrder.WorkOrderNo;
                await _data.DeleteWorkOrderAsync(id);
                await LogAsync("Delete", "WorkOrder", id, $"删除工单: {no}");
                Orders.Remove(SelectedOrder);
                SelectedOrder = Orders.FirstOrDefault();
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
