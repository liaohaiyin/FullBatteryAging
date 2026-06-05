using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BatteryAging.Core.Models;
using BatteryAging.Services;

namespace BatteryAging.ViewModels
{
    /// <summary>一条可叠加对比的曲线（点集 + 标签）</summary>
    public class ComparisonSeries
    {
        public string Name { get; set; }
        public int RecordId { get; set; }
        // (x, y) 点；容量衰减图 x=循环次数 y=放电容量；电压曲线 x=时间 y=电压
        public List<(double X, double Y)> Points { get; set; } = new();
        public double FirstValue => Points.Count > 0 ? Points[0].Y : 0;
        public double LastValue => Points.Count > 0 ? Points[^1].Y : 0;
        public double RetentionPercent => FirstValue > 0 ? Math.Round(LastValue / FirstValue * 100, 1) : 0;
    }

    public enum CompareMode { CapacityFade, VoltageCurve, Dcir }

    /// <summary>
    /// 多记录对比分析：把不同电芯/不同条件的测试结果叠加到同一坐标系。
    /// 页面只需订阅 SeriesChanged 后把 Series 画到 OxyPlot 即可。
    /// </summary>
    public partial class ComparisonViewModel : ObservableObject
    {
        private readonly IDataService _data;
        private readonly ILanguageService _languageService;

        public ObservableCollection<TestRecord> Candidates { get; } = new();   // 可选记录
        public ObservableCollection<TestRecord> Selected { get; } = new();     // 已选记录
        public ObservableCollection<ComparisonSeries> Series { get; } = new(); // 叠加曲线

        [ObservableProperty] private CompareMode _mode = CompareMode.CapacityFade;
        [ObservableProperty] private string _barCodePrefix = "";
        [ObservableProperty] private string _statusText = "";

        public IAsyncRelayCommand LoadCandidatesCommand { get; }
        public IAsyncRelayCommand BuildComparisonCommand { get; }
        public IRelayCommand<TestRecord> AddCommand { get; }
        public IRelayCommand<TestRecord> RemoveCommand { get; }

        public event EventHandler SeriesChanged;

        public ComparisonViewModel(IDataService data, ILanguageService languageService  )
        {
            _data = data;
            _languageService = languageService;
            LoadCandidatesCommand = new AsyncRelayCommand(LoadCandidatesAsync);
            BuildComparisonCommand = new AsyncRelayCommand(BuildAsync);
            AddCommand = new RelayCommand<TestRecord>(r => { if (r != null && !Selected.Contains(r)) Selected.Add(r); });
            RemoveCommand = new RelayCommand<TestRecord>(r => { if (r != null) Selected.Remove(r); });            
        }

        private async Task LoadCandidatesAsync()
        {
            var list = await _data.GetRecordsByBarCodePrefixAsync(BarCodePrefix);
            Candidates.Clear();
            foreach (var r in list) Candidates.Add(r);
            StatusText = $"找到 {Candidates.Count} 条记录";
        }

        private async Task BuildAsync()
        {
            Series.Clear();
            var palette = Selected.ToList();
            foreach (var rec in palette)
            {
                var s = new ComparisonSeries { Name = $"{_languageService.GetString("Compare_Col_CH")}{rec.ChannelIndex} {rec.BarCode}", RecordId = rec.Id };
                switch (Mode)
                {
                    case CompareMode.CapacityFade:
                        var cycles = await _data.GetCycleDataAsync(rec.Id);
                        s.Points = cycles.Select(c => ((double)c.CycleIndex, c.DischargeCapacity)).ToList();
                        break;
                    case CompareMode.VoltageCurve:
                        var pts = await _data.GetDataPointsAsync(rec.Id);
                        s.Points = pts.Select(p => (p.TotalElapsedSeconds, p.Voltage)).ToList();
                        break;
                    case CompareMode.Dcir:
                        // 若已实现 DcirResult 持久化，可在 IDataService 增加 GetDcirAsync 后在此填充
                        break;
                }
                if (s.Points.Count > 0) Series.Add(s);
            }
            StatusText = Mode == CompareMode.CapacityFade
                ? $"对比 {Series.Count} 条曲线，保持率: " + string.Join(" / ", Series.Select(x => $"{x.RetentionPercent}%"))
                : $"对比 {Series.Count} 条曲线";
            SeriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
