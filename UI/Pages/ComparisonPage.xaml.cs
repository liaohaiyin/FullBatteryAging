using System;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class ComparisonPage : Page
    {
        private readonly ComparisonViewModel _vm;
        private readonly PlotModel _model;

        // 多曲线配色
        private static readonly OxyColor[] Palette =
        {
            OxyColor.FromRgb(0x00, 0xE5, 0xFF), OxyColor.FromRgb(0xFF, 0xC1, 0x07),
            OxyColor.FromRgb(0x4C, 0xAF, 0x50), OxyColor.FromRgb(0xE5, 0x39, 0x35),
            OxyColor.FromRgb(0xAB, 0x47, 0xBC), OxyColor.FromRgb(0xFF, 0x70, 0x43),
            OxyColor.FromRgb(0x29, 0xB6, 0xF6), OxyColor.FromRgb(0xD4, 0xE1, 0x57)
        };

        public ComparisonPage(ComparisonViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // 对比维度下拉直接用枚举值填充（无需改 ViewModel）
            ModeCombo.ItemsSource = Enum.GetValues(typeof(CompareMode));

            _model = new PlotModel
            {
                DefaultFont = "微软雅黑",
                Background = OxyColors.Transparent,
                TextColor = OxyColor.FromRgb(0x8F, 0xA8, 0xC0),
                PlotAreaBorderColor = OxyColor.FromArgb(80, 110, 160, 200),
                PlotMargins = new OxyThickness(56, 8, 16, 32)
            };
            _model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendTextColor = OxyColor.FromRgb(0xE0, 0xF2, 0xF1),
                LegendFontSize = 11
            });
            _model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "X",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(60, 110, 160, 200)
            });
            _model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Y",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(60, 110, 160, 200)
            });
            ComparePlot.Model = _model;

            _vm.SeriesChanged += OnSeriesChanged;
            Unloaded += (_, _) => _vm.SeriesChanged -= OnSeriesChanged;
        }

        private void OnSeriesChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke((Action)(() => OnSeriesChanged(sender, e))); return; }

            _model.Series.Clear();
            var (xt, yt) = _vm.Mode switch
            {
                CompareMode.CapacityFade => ("循环次数", "放电容量 (Ah)"),
                CompareMode.VoltageCurve => ("时间 (s)", "电压 (V)"),
                _ => ("X", "内阻 (Ω)")
            };
            _model.Axes[0].Title = xt;
            _model.Axes[1].Title = yt;

            int i = 0;
            foreach (var s in _vm.Series)
            {
                var ls = new LineSeries
                {
                    Title = _vm.Mode == CompareMode.CapacityFade ? $"{s.Name} ({s.RetentionPercent}%)" : s.Name,
                    Color = Palette[i % Palette.Length],
                    StrokeThickness = 1.6,
                    MarkerType = _vm.Mode == CompareMode.CapacityFade ? MarkerType.Circle : MarkerType.None,
                    MarkerSize = 3
                };
                foreach (var (x, y) in s.Points) ls.Points.Add(new DataPoint(x, y));
                _model.Series.Add(ls);
                i++;
            }
            _model.InvalidatePlot(true);
        }
    }
}
