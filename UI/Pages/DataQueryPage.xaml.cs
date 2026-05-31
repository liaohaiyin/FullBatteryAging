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
        private readonly PlotModel _cycleModel;
        private readonly LineSeries _voltage;
        private readonly LineSeries _current;
        private readonly LineSeries _temperature;
        private readonly LineSeries _cycleSeries;

        public DataQueryPage(DataQueryViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _model = new PlotModel
            {
                DefaultFont = "微软雅黑",
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

            _cycleModel = new PlotModel
            {
                DefaultFont = "微软雅黑",
                PlotMargins = new OxyThickness(60, 8, 20, 36),
                PlotAreaBorderColor = OxyColor.FromArgb(60, 0, 0, 0)
            };
            _cycleModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "循环次数",
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0)
            });
            _cycleModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "放电容量 (Ah)",
                TitleColor = OxyColor.FromRgb(0x00, 0x79, 0x6B),
                MajorGridlineStyle = LineStyle.Dot,
                MajorGridlineColor = OxyColor.FromArgb(40, 0, 0, 0)
            });
            _cycleSeries = new LineSeries
            {
                Title = "容量衰减",
                Color = OxyColor.FromRgb(0x00, 0x79, 0x6B),
                StrokeThickness = 1.6,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3
            };
            _cycleModel.Series.Add(_cycleSeries);
            CyclePlot.Model = _cycleModel;

            ((INotifyCollectionChanged)_vm.CycleData).CollectionChanged += OnCycleChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DataQueryViewModel.SelectedRecord))
            {
                if (_vm.SelectedRecord == null)
                {
                    if (Dispatcher.CheckAccess())
                    {
                        ClearAllSeries();
                    }
                    else
                    {
                        Dispatcher.BeginInvoke((Action)ClearAllSeries);
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

        private void OnCycleChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _cycleSeries.Points.Clear();
            foreach (var c in _vm.CycleData)
            {
                // 直接画放电容量：
                _cycleSeries.Points.Add(new DataPoint(c.CycleIndex, c.DischargeCapacity));
            }
            _cycleModel.InvalidatePlot(true);
        }
        private void ClearAllSeries()
        {
            _voltage.Points.Clear();
            _current.Points.Clear();
            _temperature.Points.Clear();
            _model.InvalidatePlot(true);

            _cycleSeries.Points.Clear();
            _cycleModel.InvalidatePlot(true);
        }
    }
}
