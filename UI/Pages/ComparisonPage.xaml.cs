using System;
using System.Windows.Controls;
using ScottPlot;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class ComparisonPage : Page
    {
        private readonly ComparisonViewModel _vm;

        private static readonly Color[] Palette =
        {
            Color.FromHex("#00E5FF"), Color.FromHex("#FFC107"),
            Color.FromHex("#4CAF50"), Color.FromHex("#E53935"),
            Color.FromHex("#AB47BC"), Color.FromHex("#FF7043"),
            Color.FromHex("#29B6F6"), Color.FromHex("#D4E157"),
        };

        public ComparisonPage(ComparisonViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            ModeCombo.ItemsSource = Enum.GetValues(typeof(CompareMode));

            var plot = ComparePlot.Plot;
            plot.FigureBackground.Color = Colors.Transparent;
            plot.DataBackground.Color = Colors.Transparent;
            plot.Legend.BackgroundColor = Color.FromHex("#404040");
            plot.Legend.FontColor = Color.FromHex("#d7d7d7");
            plot.Legend.OutlineColor = Color.FromHex("#d7d7d7");
            var labelColor = Color.FromHex("#FFF1F1F1");
            plot.Axes.Bottom.Label.Text = "X";
            plot.Axes.Bottom.Label.FontSize = 14;
            plot.Axes.Bottom.Label.Bold = false;
            plot.Axes.Bottom.Label.ForeColor = labelColor;
            plot.Axes.Left.Label.Text = "Y";
            plot.Axes.Left.Label.FontSize = 14;
            plot.Axes.Left.Label.Bold = false;
            plot.Axes.Left.Label.ForeColor = labelColor;
            plot.ShowLegend(Alignment.UpperRight);
            ComparePlot.Refresh();

            _vm.SeriesChanged += OnSeriesChanged;
            Unloaded += (_, _) => _vm.SeriesChanged -= OnSeriesChanged;
        }

        private void OnSeriesChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)(() => OnSeriesChanged(sender, e)));
                return;
            }

            var plot = ComparePlot.Plot;
            plot.Clear();

            var (xt, yt) = _vm.Mode switch
            {
                CompareMode.CapacityFade => ("循环次数", "放电容量 (Ah)"),
                CompareMode.VoltageCurve => ("时间 (s)", "电压 (V)"),
                _ => ("X", "内阻 (Ω)")
            };
            plot.Axes.Bottom.Label.Text = xt;
            plot.Axes.Left.Label.Text = yt;

            int i = 0;
            foreach (var s in _vm.Series)
            {
                var xs = new double[s.Points.Count];
                var ys = new double[s.Points.Count];
                for (int k = 0; k < s.Points.Count; k++)
                {
                    xs[k] = s.Points[k].X;
                    ys[k] = s.Points[k].Y;
                }

                var scatter = plot.Add.Scatter(xs, ys);
                scatter.Color = Palette[i % Palette.Length];
                scatter.LineWidth = 1.6f;
                scatter.MarkerSize = _vm.Mode == CompareMode.CapacityFade ? 5 : 0;
                scatter.LegendText = _vm.Mode == CompareMode.CapacityFade
                    ? $"{s.Name} ({s.RetentionPercent}%)" : s.Name;
                i++;
            }

            plot.Axes.AutoScale();
            ComparePlot.Refresh();
        }
    }
}