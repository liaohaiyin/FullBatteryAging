using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    public class GradeBucket
    {
        public CapacityGrade Grade { get; set; }
        public int Count { get; set; }
        public double MinCap { get; set; }
        public double MaxCap { get; set; }
        public string GradeName => Grade.ToString();
        public string RangeText => $"{MinCap:F3} ~ {MaxCap:F3} Ah";
    }

    public class HistogramBin
    {
        public double Lower { get; set; }
        public double Upper { get; set; }
        public int Count { get; set; }
        public string Label => $"{Lower:F3}";
    }

    public partial class BatchAnalysisViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly IBatteryAnalyticsService _analytics;
        private readonly IDialogService _dialog;

        public ObservableCollection<TestRecord> Records { get; } = new();
        public ObservableCollection<GradeBucket> GradeBuckets { get; } = new();
        public ObservableCollection<HistogramBin> Histogram { get; } = new();

        [ObservableProperty] private string _barCodePrefix = "";
        [ObservableProperty] private double _nominalCapacity = 2.6;
        [ObservableProperty] private string _statusText = "";

        // 一致性分析结果
        [ObservableProperty] private int _sampleCount;
        [ObservableProperty] private double _meanCapacity;
        [ObservableProperty] private double _stdDevCapacity;
        [ObservableProperty] private double _minCapacity;
        [ObservableProperty] private double _maxCapacity;
        [ObservableProperty] private double _rangeCapacity;
        [ObservableProperty] private double _cvPercent;
        [ObservableProperty] private bool _isConsistent;
        [ObservableProperty] private double _avgSoh;

        public IAsyncRelayCommand AnalyzeCommand { get; }

        public BatchAnalysisViewModel(IDataService data, IBatteryAnalyticsService analytics, IDialogService dialog)
        {
            _data = data;
            _analytics = analytics;
            _dialog = dialog;
            AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        }

        private async Task AnalyzeAsync()
        {
            try
            {
                var list = await _data.GetRecordsByBarCodePrefixAsync(BarCodePrefix);
                // 仅取完成或停止状态的记录
                var valid = list.Where(r => r.TotalDischargeCapacity > 0).ToList();

                Records.Clear();
                foreach (var r in valid) Records.Add(r);

                // 一致性
                var report = _analytics.AnalyzeBatchConsistency(valid);
                SampleCount = report.SampleCount;
                MeanCapacity = report.MeanCapacity;
                StdDevCapacity = report.StdDevCapacity;
                MinCapacity = report.MinCapacity;
                MaxCapacity = report.MaxCapacity;
                RangeCapacity = report.RangeCapacity;
                CvPercent = report.CvPercent;
                IsConsistent = report.IsConsistent;

                // SOH 均值
                AvgSoh = valid.Count > 0
                    ? Math.Round(valid.Average(r => r.SohEstimate), 4)
                    : 0;

                // 分档统计
                var boundaries = _analytics.GetDefaultGradeBoundaries(NominalCapacity);
                GradeBuckets.Clear();
                foreach (var b in boundaries.OrderByDescending(x => x.MinCapacity))
                {
                    var cnt = valid.Count(r => _analytics.GradeCapacity(r.TotalDischargeCapacity, NominalCapacity, boundaries) == b.Grade);
                    GradeBuckets.Add(new GradeBucket
                    {
                        Grade = b.Grade,
                        MinCap = b.MinCapacity,
                        MaxCap = b.MaxCapacity,
                        Count = cnt
                    });
                }

                // 直方图（10 个 bin）
                Histogram.Clear();
                if (valid.Count > 0)
                {
                    var caps = valid.Select(r => r.TotalDischargeCapacity).OrderBy(x => x).ToList();
                    var lo = caps.First();
                    var hi = caps.Last();
                    if (hi - lo < 0.001) { hi = lo + 0.01; }
                    var step = (hi - lo) / 10;
                    for (int i = 0; i < 10; i++)
                    {
                        var l = lo + step * i;
                        var u = lo + step * (i + 1);
                        var cnt = caps.Count(c => c >= l && (i == 9 ? c <= u : c < u));
                        Histogram.Add(new HistogramBin { Lower = l, Upper = u, Count = cnt });
                    }
                }

                StatusText = $"已分析 {SampleCount} 条记录: {report.Summary}";
            }
            catch (Exception ex)
            {
                _dialog.ShowError($"分析失败: {ex.Message}");
            }
        }
    }
}
