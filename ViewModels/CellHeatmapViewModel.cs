using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BatteryAging.ViewModels
{
    /// <summary>
    /// 多通道单体电压真热力图：以通道为行、单体序号为列，按电压着色，
    /// 用于横向对比多个 PACK 之间及内部单体的一致性，区别于执行页仅显示当前选中通道的单体电压墙。
    /// </summary>
    public partial class CellHeatmapViewModel : ObservableObject, IDisposable
    {
        private readonly TestExecutionViewModel _exec;
        private readonly DispatcherTimer _timer;

        public ObservableCollection<HeatmapRow> Rows { get; } = new();

        [ObservableProperty] private double _minScaleVoltage = 3.0;
        [ObservableProperty] private double _maxScaleVoltage = 4.2;
        [ObservableProperty] private string _statusText = "";

        public IRelayCommand RefreshCommand { get; }

        public CellHeatmapViewModel(TestExecutionViewModel exec)
        {
            _exec = exec;
            RefreshCommand = new RelayCommand(RefreshNow);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RefreshNow();
            _timer.Start();
            RefreshNow();
        }

        partial void OnMinScaleVoltageChanged(double value) => RefreshNow();
        partial void OnMaxScaleVoltageChanged(double value) => RefreshNow();

        private void RefreshNow()
        {
            var channels = _exec.Channels
                .Where(c => c.Cells.Count > 0)
                .OrderBy(c => c.ChannelIndex)
                .ToList();

            while (Rows.Count < channels.Count) Rows.Add(new HeatmapRow());
            while (Rows.Count > channels.Count) Rows.RemoveAt(Rows.Count - 1);

            for (int r = 0; r < channels.Count; r++)
            {
                var ch = channels[r];
                var row = Rows[r];
                row.ChannelIndex = ch.ChannelIndex;
                row.ChannelLabel = $"{ch.ShortLabel} ({ch.ChannelName})";
                row.MaxCellVoltage = ch.MaxCellVoltage;
                row.MinCellVoltage = ch.MinCellVoltage;
                row.CellVoltageDelta = ch.CellVoltageDelta;

                while (row.Cells.Count < ch.Cells.Count)
                    row.Cells.Add(new HeatmapCellItem { Index = row.Cells.Count + 1 });
                while (row.Cells.Count > ch.Cells.Count)
                    row.Cells.RemoveAt(row.Cells.Count - 1);

                for (int c = 0; c < ch.Cells.Count; c++)
                {
                    var v = ch.Cells[c].Voltage;
                    row.Cells[c].Voltage = v;
                    row.Cells[c].CellBrush = ComputeHeatColor(v, MinScaleVoltage, MaxScaleVoltage);
                }
            }

            var totalCells = channels.Sum(c => c.Cells.Count);
            StatusText = channels.Count == 0
                ? "暂无带 BMS 单体数据的通道"
                : $"{channels.Count} 个通道参与热力图，共 {totalCells} 节单体";
        }

        /// <summary>蓝(低) → 绿(中) → 红(高) 渐变，越界钳位到端点色</summary>
        private static Brush ComputeHeatColor(double v, double lo, double hi)
        {
            if (hi <= lo) return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            var t = Math.Clamp((v - lo) / (hi - lo), 0.0, 1.0);

            byte r, g, b;
            if (t < 0.5)
            {
                var k = t / 0.5;
                r = (byte)(0x21 + (0x4C - 0x21) * k);
                g = (byte)(0x96 + (0xAF - 0x96) * k);
                b = (byte)(0xF3 + (0x50 - 0xF3) * k);
            }
            else
            {
                var k = (t - 0.5) / 0.5;
                r = (byte)(0x4C + (0xE5 - 0x4C) * k);
                g = (byte)(0xAF + (0x39 - 0xAF) * k);
                b = (byte)(0x50 + (0x35 - 0x50) * k);
            }
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        public void Dispose() => _timer.Stop();
    }

    public partial class HeatmapRow : ObservableObject
    {
        [ObservableProperty] private int _channelIndex;
        [ObservableProperty] private string _channelLabel;
        [ObservableProperty] private double _maxCellVoltage;
        [ObservableProperty] private double _minCellVoltage;
        [ObservableProperty] private double _cellVoltageDelta;
        public ObservableCollection<HeatmapCellItem> Cells { get; } = new();
    }

    public partial class HeatmapCellItem : ObservableObject
    {
        public int Index { get; set; }
        [ObservableProperty] private double _voltage;
        [ObservableProperty] private Brush _cellBrush = Brushes.Gray;
    }
}
