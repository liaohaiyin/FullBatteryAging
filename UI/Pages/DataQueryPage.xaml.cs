using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using BatteryAging.ViewModels;
using OxyPlot.Legends;

namespace BatteryAging.UI.Pages
{
    public partial class DataQueryPage : Page
    {
        private readonly DataQueryViewModel _vm;
        private readonly PlotModel _model;
        private readonly LineSeries _voltage;
        private readonly LineSeries _current;
        private readonly LineSeries _temperature;

        public DataQueryPage(DataQueryViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _model = new PlotModel
            {
                //LegendPosition = LegendPosition.TopRight,
                PlotMargins = new OxyThickness(60, 8, 60, 36),
                PlotAreaBorderColor = OxyColor.FromArgb(60, 0, 0, 0)
            };
            _model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "时间 (s)",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0)
            });
            _model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Key = "V",
                Title = "电压 (V)",
                TitleColor = OxyColor.FromRgb(0x00, 0x79, 0x6B),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0)
            });
            _model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Right,
                Key = "I",
                Title = "电流 (A) / 温度 (°C)",
                TitleColor = OxyColor.FromRgb(0xFF, 0x6F, 0x00)
            });

            _voltage = new LineSeries { Title = "电压", Color = OxyColor.FromRgb(0x00, 0x79, 0x6B), YAxisKey = "V", StrokeThickness = 1.4 };
            _current = new LineSeries { Title = "电流", Color = OxyColor.FromRgb(0xFF, 0x6F, 0x00), YAxisKey = "I", StrokeThickness = 1.4 };
            _temperature = new LineSeries { Title = "温度", Color = OxyColor.FromRgb(0xE5, 0x39, 0x35), YAxisKey = "I", StrokeThickness = 1.0, LineStyle = LineStyle.Dash };
            _model.Series.Add(_voltage);
            _model.Series.Add(_current);
            _model.Series.Add(_temperature);

            HistoryPlot.Model = _model;

            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.DataPoints).CollectionChanged += OnDataChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DataQueryViewModel.SelectedRecord))
            {
                if (_vm.SelectedRecord == null)
                {
                    if (Dispatcher.CheckAccess())
                    {
                        _voltage.Points.Clear();
                        _current.Points.Clear();
                        _temperature.Points.Clear();
                        _model.InvalidatePlot(true);
                    }
                    else
                    {
                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _voltage.Points.Clear();
                            _current.Points.Clear();
                            _temperature.Points.Clear();
                            _model.InvalidatePlot(true);
                        }));
                    }
                }
            }
        }

        private void OnDataChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _voltage.Points.Clear();
            _current.Points.Clear();
            _temperature.Points.Clear();
            foreach (var p in _vm.DataPoints)
            {
                _voltage.Points.Add(new DataPoint(p.ElapsedSeconds, p.Voltage));
                _current.Points.Add(new DataPoint(p.ElapsedSeconds, p.Current));
                _temperature.Points.Add(new DataPoint(p.ElapsedSeconds, p.Temperature));
            }
            _model.InvalidatePlot(true);
        }
    }
}
