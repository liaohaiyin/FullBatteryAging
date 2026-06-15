using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using ScottPlot;
using BatteryAging.ViewModels;

namespace BatteryAging.UI.Pages
{
    public partial class DataQueryPage : Page
    {
        private readonly DataQueryViewModel _vm;

        private readonly List<double> _t = new();
        private readonly List<double> _v = new();
        private readonly List<double> _i = new();
        private readonly List<double> _temp = new();
        private readonly List<double> _cycX = new();
        private readonly List<double> _cycY = new();

        private readonly ScottPlot.Plottables.Scatter _voltage;
        private readonly ScottPlot.Plottables.Scatter _current;
        private readonly ScottPlot.Plottables.Scatter _temperature;
        private readonly ScottPlot.Plottables.Scatter _cycleSeries;

        public DataQueryPage(DataQueryViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // ── 历史曲线（电压/电流/温度，三轴）──
            var p = HistoryPlot.Plot;
            p.FigureBackground.Color = Colors.Transparent;
            p.DataBackground.Color = Colors.Transparent;
            p.Legend.BackgroundColor = Color.FromHex("#404040");
            p.Legend.FontColor = Color.FromHex("#d7d7d7");
            p.Legend.OutlineColor = Color.FromHex("#d7d7d7");

            var labelColor = Color.FromHex("#FFF1F1F1");
            p.Axes.Bottom.Label.Text = "时间 (s)";
            p.Axes.Bottom.Label.FontSize = 14;
            p.Axes.Bottom.Label.Bold = false;
            p.Axes.Bottom.Label.ForeColor = labelColor;
            p.Axes.Left.Label.Text = "电压 (V)";
            p.Axes.Left.Label.FontSize = 14;
            p.Axes.Left.Label.Bold = false;
            p.Axes.Left.Label.ForeColor = labelColor;

            var iAxis = p.Axes.AddRightAxis();
            iAxis.Label.Text = "电流 (A)";
            iAxis.Label.FontSize = 14;
            iAxis.Label.Bold = false;
            iAxis.Label.ForeColor = labelColor;
            var tAxis = p.Axes.AddRightAxis();
            tAxis.Label.Text = "温度 (°C)";
            tAxis.Label.FontSize = 14;
            tAxis.Label.Bold = false;
            tAxis.Label.ForeColor = labelColor;

            _voltage = p.Add.Scatter(_t, _v);
            _voltage.Color = Color.FromHex("#00796B");
            _voltage.LineWidth = 1.4f; _voltage.MarkerSize = 0;
            _voltage.LegendText = "电压";
            _voltage.Axes.YAxis = p.Axes.Left;

            _current = p.Add.Scatter(_t, _i);
            _current.Color = Color.FromHex("#FF6F00");
            _current.LineWidth = 1.4f; _current.MarkerSize = 0;
            _current.LegendText = "电流";
            _current.Axes.YAxis = iAxis;

            _temperature = p.Add.Scatter(_t, _temp);
            _temperature.Color = Color.FromHex("#E53935");
            _temperature.LineWidth = 1.0f; _temperature.MarkerSize = 0;
            _temperature.LinePattern = LinePattern.Dashed;
            _temperature.LegendText = "温度";
            _temperature.Axes.YAxis = tAxis;

            p.ShowLegend();
            HistoryPlot.Refresh();

            // ── 循环衰减 ──
            var cp = CyclePlot.Plot;
            cp.FigureBackground.Color = Colors.Transparent;
            cp.DataBackground.Color = Colors.Transparent;
            cp.Legend.BackgroundColor = Color.FromHex("#404040");
            cp.Legend.FontColor = Color.FromHex("#d7d7d7");
            cp.Legend.OutlineColor = Color.FromHex("#d7d7d7");
            cp.Axes.Bottom.Label.Text = "循环次数";
            cp.Axes.Bottom.Label.ForeColor = labelColor;
            cp.Axes.Bottom.Label.FontSize = 14;
            cp.Axes.Bottom.Label.Bold = false;
            cp.Axes.Left.Label.Text = "放电容量 (Ah)";
            cp.Axes.Left.Label.ForeColor = labelColor;
            cp.Axes.Left.Label.FontSize = 14;
            cp.Axes.Left.Label.Bold = false;
            _cycleSeries = cp.Add.Scatter(_cycX, _cycY);
            _cycleSeries.Color = Color.FromHex("#00796B");
            _cycleSeries.LineWidth = 1.6f;
            _cycleSeries.MarkerSize = 5;
            _cycleSeries.LegendText = "容量衰减";
            CyclePlot.Refresh();

            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.DataPoints).CollectionChanged += OnDataChanged;
            ((INotifyCollectionChanged)_vm.CycleData).CollectionChanged += OnCycleChanged;
            Unloaded += OnPageUnloaded;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DataQueryViewModel.SelectedRecord) && _vm.SelectedRecord == null)
            {
                if (Dispatcher.CheckAccess()) ClearAllSeries();
                else Dispatcher.BeginInvoke((Action)ClearAllSeries);
            }
        }

        private void OnDataChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _t.Clear(); _v.Clear(); _i.Clear(); _temp.Clear();
            foreach (var pt in _vm.DataPoints)
            {
                _t.Add(pt.ElapsedSeconds);
                _v.Add(pt.Voltage);
                _i.Add(pt.Current);
                _temp.Add(pt.Temperature);
            }
            HistoryPlot.Plot.Axes.AutoScale();
            HistoryPlot.Refresh();
        }

        private void OnCycleChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _cycX.Clear(); _cycY.Clear();
            foreach (var c in _vm.CycleData)
            {
                _cycX.Add(c.CycleIndex);
                _cycY.Add(c.DischargeCapacity);
            }
            CyclePlot.Plot.Axes.AutoScale();
            CyclePlot.Refresh();
        }

        private void ClearAllSeries()
        {
            _t.Clear(); _v.Clear(); _i.Clear(); _temp.Clear();
            HistoryPlot.Refresh();
            _cycX.Clear(); _cycY.Clear();
            CyclePlot.Refresh();
        }

        private void OnPageUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.DataPoints).CollectionChanged -= OnDataChanged;
            ((INotifyCollectionChanged)_vm.CycleData).CollectionChanged -= OnCycleChanged;
        }
    }
}