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

        public ObservableCollection<TestRecord> Records { get; } = new();
        public ObservableCollection<DataPoint> DataPoints { get; } = new();

        [ObservableProperty] private TestRecord _selectedRecord;
        [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime _endDate = DateTime.Today.AddDays(1);
        [ObservableProperty] private string _barCode;
        [ObservableProperty] private int? _channelFilter;
        [ObservableProperty] private string _statusText = "";

        public IAsyncRelayCommand QueryCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IAsyncRelayCommand LoadDataPointsCommand { get; }

        public DataQueryViewModel(IDataService dataService, IDialogService dialogService)
        {
            _dataService = dataService;
            _dialogService = dialogService;

            QueryCommand = new AsyncRelayCommand(QueryAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            LoadDataPointsCommand = new AsyncRelayCommand(LoadDataPointsAsync);

            _ = QueryAsync();
        }

        partial void OnSelectedRecordChanged(TestRecord value)
        {
            if (value != null) _ = LoadDataPointsAsync();
        }

        private async Task QueryAsync()
        {
            try
            {
                var list = await _dataService.QueryRecordsAsync(StartDate, EndDate, ChannelFilter, BarCode);
                Records.Clear();
                foreach (var r in list) Records.Add(r);
                StatusText = $"查询到 {Records.Count} 条记录";
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
                StatusText = $"加载 {DataPoints.Count} 个数据点";
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
    }
}
