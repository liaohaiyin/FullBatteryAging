using BatteryAging.ViewModels;
using ScottPlot;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SamplePoint = BatteryAging.Core.Models.DataPoint;

namespace BatteryAging.UI.Pages
{
    public partial class TestExecutionPage : Page
    {
        private readonly TestExecutionViewModel _vm;

        private readonly List<double> _time = new();
        private readonly List<double> _volt = new();
        private readonly List<double> _curr = new();

        private readonly ScottPlot.Plottables.Scatter _voltageSeries;
        private readonly ScottPlot.Plottables.Scatter _currentSeries;
        private readonly IYAxis _currentAxis;

        private ChannelViewModel _currentChannel;
        private GridLength _savedRightWidth = new GridLength(1.2, GridUnitType.Star);

        public TestExecutionPage(TestExecutionViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            var plot = LivePlot.Plot;
            plot.FigureBackground.Color = Colors.Transparent;
            plot.DataBackground.Color = Colors.Transparent;
            plot.Legend.BackgroundColor = Color.FromHex("#404040");
            plot.Legend.FontColor = Color.FromHex("#d7d7d7");
            plot.Legend.OutlineColor = Color.FromHex("#d7d7d7");

            var labelColor = Color.FromHex("#FFF1F1F1");
            plot.Axes.Bottom.Label.Text = "时间 (s)";
            plot.Axes.Bottom.Label.ForeColor = labelColor;
            plot.Axes.Left.Label.Text = "电压 (V)";
            plot.Axes.Left.Label.ForeColor = labelColor;
            _currentAxis = plot.Axes.AddRightAxis();
            _currentAxis.Label.Text = "电流 (A)";
            _currentAxis.Label.ForeColor = labelColor;

            _voltageSeries = plot.Add.Scatter(_time, _volt);
            _voltageSeries.Color = Color.FromHex("#00E5FF");
            _voltageSeries.LineWidth = 1.8f; _voltageSeries.MarkerSize = 0;
            _voltageSeries.LegendText = "电压";
            _voltageSeries.Axes.YAxis = plot.Axes.Left;

            _currentSeries = plot.Add.Scatter(_time, _curr);
            _currentSeries.Color = Color.FromHex("#FFC107");
            _currentSeries.LineWidth = 1.8f; _currentSeries.MarkerSize = 0;
            _currentSeries.LegendText = "电流";
            _currentSeries.Axes.YAxis = _currentAxis;

            plot.ShowLegend(Alignment.UpperRight);
            LivePlot.Refresh();

            _vm.PropertyChanged += OnVmPropertyChanged;
            AttachChannel(_vm.SelectedChannel);
            Unloaded += OnPageUnloaded;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestExecutionViewModel.SelectedChannel))
                AttachChannel(_vm.SelectedChannel);
        }

        private void AttachChannel(ChannelViewModel ch)
        {
            if (_currentChannel != null)
                _currentChannel.RecentSamples.CollectionChanged -= OnSamplesChanged;
            _currentChannel = ch;
            if (_currentChannel != null)
            {
                _currentChannel.RecentSamples.CollectionChanged += OnSamplesChanged;
                RebuildPlot();
            }
            else
            {
                ClearPlot();
            }
        }

        private void OnSamplesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is SamplePoint dp)
                    {
                        _time.Add(dp.ElapsedSeconds);
                        _volt.Add(dp.Voltage);
                        _curr.Add(dp.Current);
                    }
                }
                LivePlot.Plot.Axes.AutoScale();
                LivePlot.Refresh();
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                ClearPlot();
            }
            else
            {
                RebuildPlot();
            }
        }

        private void RebuildPlot()
        {
            _time.Clear(); _volt.Clear(); _curr.Clear();
            if (_currentChannel != null)
            {
                foreach (var dp in _currentChannel.RecentSamples)
                {
                    _time.Add(dp.ElapsedSeconds);
                    _volt.Add(dp.Voltage);
                    _curr.Add(dp.Current);
                }
            }
            LivePlot.Plot.Axes.AutoScale();
            LivePlot.Refresh();
        }

        private void ClearPlot()
        {
            _time.Clear(); _volt.Clear(); _curr.Clear();
            LivePlot.Refresh();
        }

        private void CollapseToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (RightPanel == null || RightColumn == null) return;
            RightColumn.MinWidth = 440;
            RightColumn.Width = _savedRightWidth;
            RightPanel.Visibility = Visibility.Visible;
        }

        private void CollapseToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (RightPanel == null || RightColumn == null) return;
            if (RightColumn.Width.Value > 0) _savedRightWidth = RightColumn.Width;
            RightPanel.Visibility = Visibility.Collapsed;
            RightColumn.MinWidth = 0;
            RightColumn.Width = new GridLength(0);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            if (_currentChannel != null)
            {
                _currentChannel.RecentSamples.CollectionChanged -= OnSamplesChanged;
                _currentChannel = null;
            }
        }
    }
}