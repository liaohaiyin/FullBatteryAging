using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public partial class DataQueryViewModel : ObservableObject
    {
        private readonly IDataService _dataService;
        private readonly IDialogService _dialogService;
        private readonly IAuthService _auth;
        private readonly IBatteryAnalyticsService _analytics;
        private readonly IReportExportService _reportExport;

        public ObservableCollection<TestRecord> Records { get; } = new();
        public ObservableCollection<DataPoint> DataPoints { get; } = new();
        public ObservableCollection<CycleData> CycleData { get; } = new();

        [ObservableProperty] private TestRecord _selectedRecord;
        [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime _endDate = DateTime.Today.AddDays(1);
        [ObservableProperty] private string _barCode;
        [ObservableProperty] private int? _channelFilter;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _rulSummaryText = "";

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IAsyncRelayCommand ExportExcelCommand { get; }
        public IAsyncRelayCommand ExportPdfCommand { get; }
        public IAsyncRelayCommand LoadDataPointsCommand { get; }

        /// <summary>
        /// 分页相关属性和命令
        /// </summary>
        [ObservableProperty] private int _totalRecords;
        [ObservableProperty] private int _totalPages = 1;

        private int _currentPage = 1;
        private const int PageSize = 50;
        private readonly RelayCommand _firstPageCommand;
        private readonly RelayCommand _previousPageCommand;
        private readonly RelayCommand _nextPageCommand;
        private readonly RelayCommand _lastPageCommand;

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                    _ = QueryAsync(keepPage: true);
            }
        }

        public IRelayCommand FirstPageCommand => _firstPageCommand;
        public IRelayCommand PreviousPageCommand => _previousPageCommand;
        public IRelayCommand NextPageCommand => _nextPageCommand;
        public IRelayCommand LastPageCommand => _lastPageCommand;


        public DataQueryViewModel(IDataService dataService, IDialogService dialogService, IAuthService auth,
            IBatteryAnalyticsService analytics, IReportExportService reportExport)
        {
            _dataService = dataService;
            _dialogService = dialogService;
            _auth = auth;
            _analytics = analytics;
            _reportExport = reportExport;

            QueryCommand = new AsyncRelayCommand(QueryAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync,() => _auth.HasPermission(Permission.DataQuery_Export));
            ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync, () => _auth.HasPermission(Permission.DataQuery_Export));
            ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => _auth.HasPermission(Permission.DataQuery_Report));
            LoadDataPointsCommand = new AsyncRelayCommand(LoadDataPointsAsync);

            _firstPageCommand = new RelayCommand(() => CurrentPage = 1, () => CurrentPage > 1);
            _previousPageCommand = new RelayCommand(() => CurrentPage = Math.Max(1, CurrentPage - 1), () => CurrentPage > 1);
            _nextPageCommand = new RelayCommand(() => CurrentPage = Math.Min(TotalPages, CurrentPage + 1), () => CurrentPage < TotalPages);
            _lastPageCommand = new RelayCommand(() => CurrentPage = TotalPages, () => CurrentPage < TotalPages);

            _ = QueryAsync();
        }

        partial void OnSelectedRecordChanged(TestRecord value)
        {
            if (value != null)
            {
                _ = LoadDataPointsAsync();
            }
            else
            {
                DataPoints.Clear();
                CycleData.Clear();
                RulSummaryText = "";
            }
        }

        private async Task QueryAsync() => await QueryAsync(keepPage: false);

        private async Task QueryAsync(bool keepPage)
        {
            try
            {
                if (!keepPage && _currentPage != 1)
                {
                    // 重置到第一页(不触发递归查询)
                    SetProperty(ref _currentPage, 1, nameof(CurrentPage));
                }

                var (list, total) = await _dataService.QueryRecordsPagedAsync(
                    StartDate, EndDate, ChannelFilter, BarCode, _currentPage, PageSize);

                Records.Clear();
                foreach (var r in list) Records.Add(r);

                TotalRecords = total;
                TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
                UpdatePagingCommands();
                StatusText = $"共 {TotalRecords} 条记录，第 {CurrentPage}/{TotalPages} 页";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"查询失败: {ex.Message}");
            }
        }

        private async Task LoadDataPointsAsync()
        {
            if (SelectedRecord == null) return;
            try
            {
                var pts = await _dataService.GetDataPointsAsync(SelectedRecord.Id);
                DataPoints.Clear();
                foreach (var p in pts) DataPoints.Add(p);
                var cycles = await _dataService.GetCycleDataAsync(SelectedRecord.Id);
                CycleData.Clear();
                foreach (var c in cycles) CycleData.Add(c);
                StatusText = $"加载 {DataPoints.Count} 个数据点，{CycleData.Count} 个循环数据";

                var nominal = SelectedRecord.NominalCapacity > 0 ? SelectedRecord.NominalCapacity : MaxCapacityOr(cycles);
                var rul = _analytics.EstimateRul(cycles, nominal);
                RulSummaryText = nominal > 0 ? rul.Summary : "标称容量未知，无法预测寿命";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"加载数据失败: {ex.Message}");
            }
        }

        private async Task ExportAsync()
        {
            if (SelectedRecord == null)
            {
                _dialogService.ShowWarning("请先选择一条记录");
                return;
            }
            var path = _dialogService.SaveFileDialog(
                "CSV 文件|*.csv",
                $"BatteryData_CH{SelectedRecord.ChannelIndex}_{SelectedRecord.StartTime:yyyyMMdd_HHmmss}.csv",
                "导出 CSV");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var pts = await _dataService.GetDataPointsAsync(SelectedRecord.Id);
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Channel,Step,StepType,LoopIndex,ElapsedSec,Voltage_V,Current_A,Capacity_Ah,Energy_Wh,Temperature_C,SOC_%");
                foreach (var p in pts)
                {
                    sb.AppendLine($"{p.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{p.ChannelIndex},{p.StepSequence},{p.StepType},{p.LoopIndex},{p.ElapsedSeconds:F2},{p.Voltage:F4},{p.Current:F4},{p.Capacity:F5},{p.Energy:F5},{p.Temperature:F2},{p.Soc:F2}");
                }
                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
                _dialogService.ShowMessage($"已导出 {pts.Count} 个数据点到\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导出失败: {ex.Message}");
            }
        }

        private async Task ExportExcelAsync()
        {
            if (SelectedRecord == null) { _dialogService.ShowWarning("请先选择一条记录"); return; }
            var path = _dialogService.SaveFileDialog(
                "Excel 文件|*.xlsx",
                $"BatteryReport_CH{SelectedRecord.ChannelIndex}_{SelectedRecord.StartTime:yyyyMMdd_HHmmss}.xlsx",
                "导出 Excel 报表");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var pts = await _dataService.GetDataPointsAsync(SelectedRecord.Id);
                var cycles = await _dataService.GetCycleDataAsync(SelectedRecord.Id);
                await Task.Run(() => _reportExport.ExportRecordToExcel(SelectedRecord, pts, cycles, path));
                _dialogService.ShowMessage($"已导出 Excel 报表到\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导出失败: {ex.Message}");
            }
        }

        private async Task ExportPdfAsync()
        {
            if (SelectedRecord == null) { _dialogService.ShowWarning("请先选择一条记录"); return; }
            var path = _dialogService.SaveFileDialog(
                "PDF 文件|*.pdf",
                $"BatteryReport_CH{SelectedRecord.ChannelIndex}_{SelectedRecord.StartTime:yyyyMMdd_HHmmss}.pdf",
                "导出 PDF 报表");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var pts = await _dataService.GetDataPointsAsync(SelectedRecord.Id);
                var cycles = await _dataService.GetCycleDataAsync(SelectedRecord.Id);
                await Task.Run(() => _reportExport.ExportRecordToPdf(SelectedRecord, pts, cycles, path));
                _dialogService.ShowMessage($"已导出 PDF 报表到\n{path}");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"导出失败: {ex.Message}");
            }
        }

        /// <summary>记录未冗余标称容量时的兜底：取历史循环中出现过的最大放电容量近似标称值</summary>
        private static double MaxCapacityOr(System.Collections.Generic.IEnumerable<CycleData> cycles)
            => cycles.Select(c => c.DischargeCapacity).DefaultIfEmpty(0).Max();

        partial void OnTotalPagesChanged(int value) => UpdatePagingCommands();

        private void UpdatePagingCommands()
        {
            _firstPageCommand.NotifyCanExecuteChanged();
            _previousPageCommand.NotifyCanExecuteChanged();
            _nextPageCommand.NotifyCanExecuteChanged();
            _lastPageCommand.NotifyCanExecuteChanged();
        }
    }
}
