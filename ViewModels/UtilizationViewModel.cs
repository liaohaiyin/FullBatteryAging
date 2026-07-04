using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    /// <summary>设备稼动率（简化 OEE）+ EMS 回馈能耗/电费成本统计</summary>
    public partial class UtilizationViewModel : ObservableObject
    {
        private readonly IDataService _dataService;
        private readonly IBatteryAnalyticsService _analytics;
        private readonly IDialogService _dialogService;

        public ObservableCollection<ChannelUtilization> ChannelStats { get; } = new();

        [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime _endDate = DateTime.Today.AddDays(1);
        [ObservableProperty] private double _electricityPrice = 0.8;   // 元/kWh，可按当地电价调整
        [ObservableProperty] private double _feedbackEfficiency = 0.85;
        [ObservableProperty] private string _statusText = "";

        // 汇总结果
        [ObservableProperty] private double _overallUtilizationPercent;
        [ObservableProperty] private double _overallPassRatePercent;
        [ObservableProperty] private double _totalChargeEnergyWh;
        [ObservableProperty] private double _totalDischargeEnergyWh;
        [ObservableProperty] private double _feedbackEnergyWh;
        [ObservableProperty] private double _feedbackSavingPercent;
        [ObservableProperty] private double _netEnergyWh;
        [ObservableProperty] private double _estimatedCost;

        public IAsyncRelayCommand AnalyzeCommand { get; }

        public UtilizationViewModel(IDataService dataService, IBatteryAnalyticsService analytics, IDialogService dialogService)
        {
            _dataService = dataService;
            _analytics = analytics;
            _dialogService = dialogService;
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        }

        private async Task AnalyzeAsync()
        {
            try
            {
                var records = await _dataService.GetRecordsInRangeAsync(StartDate, EndDate);

                var util = _analytics.ComputeUtilization(records, StartDate, EndDate);
                ChannelStats.Clear();
                foreach (var c in util.Channels) ChannelStats.Add(c);
                OverallUtilizationPercent = util.OverallUtilizationPercent;
                OverallPassRatePercent = util.OverallPassRatePercent;

                var energy = _analytics.ComputeEnergyCost(records, ElectricityPrice, FeedbackEfficiency);
                TotalChargeEnergyWh = energy.TotalChargeEnergyWh;
                TotalDischargeEnergyWh = energy.TotalDischargeEnergyWh;
                FeedbackEnergyWh = Math.Round(energy.FeedbackEnergyWh, 4);
                FeedbackSavingPercent = Math.Round(energy.FeedbackSavingPercent, 2);
                NetEnergyWh = Math.Round(energy.NetEnergyWh, 4);
                EstimatedCost = Math.Round(energy.EstimatedCost, 2);

                StatusText = $"共 {records.Count} 条测试记录，{ChannelStats.Count} 个通道参与统计";
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"统计失败: {ex.Message}");
            }
        }
    }
}
