using BatteryAging.ViewModels;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
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
        private readonly PlotModel _plotModel;
        private readonly LineSeries _voltageSeries;
        private readonly LineSeries _currentSeries;
        private readonly LinearAxis _voltageAxis;
        private readonly LinearAxis _currentAxis;
        private readonly LinearAxis _timeAxis;
        private ChannelViewModel _currentChannel;
        private GridLength _savedRightWidth = new GridLength(1.2, GridUnitType.Star);

        public TestExecutionPage(TestExecutionViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            // ── 深色主题 PlotModel ──
            _plotModel = new PlotModel
            {
                DefaultFont = "微软雅黑",
                Background = OxyColors.Transparent,
                PlotAreaBorderColor = OxyColor.FromArgb(80, 110, 160, 200),
                PlotAreaBorderThickness = new OxyThickness(1),
                TextColor = OxyColor.FromRgb(0x8F, 0xA8, 0xC0),
                TitleColor = OxyColor.FromRgb(0xE0, 0xF2, 0xF1),
                SubtitleColor = OxyColor.FromRgb(0xE0, 0xF2, 0xF1),
                PlotMargins = new OxyThickness(56, 8, 56, 32)
            };

            _plotModel.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopRight,
                LegendBackground = OxyColor.FromArgb(180, 15, 42, 69),
                LegendBorder = OxyColor.FromArgb(120, 110, 160, 200),
                LegendBorderThickness = 1,
                LegendTextColor = OxyColor.FromRgb(0xE0, 0xF2, 0xF1),
                LegendFontSize = 11
            });

            // ── X 轴：时间 ──
            _timeAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间 (s)",
                TitleColor = OxyColor.FromRgb(0x8F, 0xA8, 0xC0),
                TextColor = OxyColor.FromRgb(0x8F, 0xA8, 0xC0),
                TicklineColor = OxyColor.FromArgb(120, 110, 160, 200),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(60, 110, 160, 200),
                MinorGridlineStyle = LineStyle.None,
                MinorTickSize = 0,
                AxislineColor = OxyColor.FromArgb(120, 110, 160, 200),
                AxislineThickness = 1,
                AxislineStyle = LineStyle.Solid,
                FontSize = 11
            };
            _plotModel.Axes.Add(_timeAxis);

            // ── 左 Y 轴：电压 ──
            _voltageAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Key = "VoltageAxis",
                Title = "电压 (V)",
                TitleColor = OxyColor.FromRgb(0x00, 0xE5, 0xFF),
                TextColor = OxyColor.FromRgb(0x00, 0xE5, 0xFF),
                TicklineColor = OxyColor.FromArgb(120, 0, 229, 255),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(60, 110, 160, 200),
                AxislineColor = OxyColor.FromArgb(120, 110, 160, 200),
                AxislineThickness = 1,
                AxislineStyle = LineStyle.Solid,
                FontSize = 11
            };
            _plotModel.Axes.Add(_voltageAxis);

            // ── 右 Y 轴：电流 ──
            _currentAxis = new LinearAxis
            {
                Position = AxisPosition.Right,
                Key = "CurrentAxis",
                Title = "电流 (A)",
                TitleColor = OxyColor.FromRgb(0xFF, 0xC1, 0x07),
                TextColor = OxyColor.FromRgb(0xFF, 0xC1, 0x07),
                TicklineColor = OxyColor.FromArgb(120, 255, 193, 7),
                MajorGridlineStyle = LineStyle.None,
                AxislineColor = OxyColor.FromArgb(120, 110, 160, 200),
                AxislineThickness = 1,
                AxislineStyle = LineStyle.Solid,
                FontSize = 11
            };
            _plotModel.Axes.Add(_currentAxis);

            _voltageSeries = new LineSeries
            {
                Title = "电压",
                Color = OxyColor.FromRgb(0x00, 0xE5, 0xFF),
                StrokeThickness = 1.8,
                YAxisKey = "VoltageAxis",
                MarkerType = MarkerType.None,
                TrackerFormatString = "{0}\n时间: {2:0.#} s\n电压: {4:0.0000} V"
            };
            _currentSeries = new LineSeries
            {
                Title = "电流",
                Color = OxyColor.FromRgb(0xFF, 0xC1, 0x07),
                StrokeThickness = 1.8,
                YAxisKey = "CurrentAxis",
                MarkerType = MarkerType.None,
                TrackerFormatString = "{0}\n时间: {2:0.#} s\n电流: {4:0.0000} A"
            };
            _plotModel.Series.Add(_voltageSeries);
            _plotModel.Series.Add(_currentSeries);

            LivePlot.Model = _plotModel;

            // 监听 SelectedChannel 切换
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
                _voltageSeries.Points.Clear();
                _currentSeries.Points.Clear();
                _plotModel.InvalidatePlot(true);
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
                        _voltageSeries.Points.Add(new DataPoint(dp.ElapsedSeconds, dp.Voltage));
                        _currentSeries.Points.Add(new DataPoint(dp.ElapsedSeconds, dp.Current));
                    }
                }
                _plotModel.InvalidatePlot(true);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _voltageSeries.Points.Clear();
                _currentSeries.Points.Clear();
                _plotModel.InvalidatePlot(true);
            }
            else
            {
                RebuildPlot();
            }
        }

        private void RebuildPlot()
        {
            _voltageSeries.Points.Clear();
            _currentSeries.Points.Clear();
            if (_currentChannel != null)
            {
                foreach (var dp in _currentChannel.RecentSamples)
                {
                    _voltageSeries.Points.Add(new DataPoint(dp.ElapsedSeconds, dp.Voltage));
                    _currentSeries.Points.Add(new DataPoint(dp.ElapsedSeconds, dp.Current));
                }
            }
            _plotModel.InvalidatePlot(true);
        }
        private void CollapseToggle_Checked(object sender, RoutedEventArgs e)
        {
            // 展开
            if (RightPanel == null || RightColumn == null) return;
            RightColumn.MinWidth = 440;
            RightColumn.Width = _savedRightWidth;
            RightPanel.Visibility = Visibility.Visible;
        }

        private void CollapseToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // 折叠
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
