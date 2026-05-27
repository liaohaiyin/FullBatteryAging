using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BatteryAging.Communication;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.ViewModels
{
    /// <summary>
    /// 通道运行时数据 ViewModel - 一个通道对应一个
    /// </summary>
    public partial class ChannelViewModel : ObservableObject
    {
        private readonly ChannelExecutor _executor;

        public int ChannelIndex => _executor.ChannelIndex;
        public int LocalChannelIndex => _executor.LocalChannelIndex;
        public string CabinetId => _executor.CabinetId;
        public int CabinetIndex { get; }
        public string ChannelName => $"通道 {_executor.ChannelIndex:D2}";
        public string ShortLabel => $"{CabinetIndex}-{_executor.LocalChannelIndex}";

        [ObservableProperty] private string _barCode = "";
        [ObservableProperty] private string _recipeName = "(未选择)";
        [ObservableProperty] private ChannelStatus _status = ChannelStatus.Idle;
        [ObservableProperty] private string _statusText = "空闲";
        [ObservableProperty] private string _currentStepName = "-";
        [ObservableProperty] private StepType _currentStepType = StepType.Rest;
        [ObservableProperty] private int _currentStepIndex;
        [ObservableProperty] private int _totalSteps;
        [ObservableProperty] private int _currentLoopIndex;

        // 实时测量值
        [ObservableProperty] private double _voltage;
        [ObservableProperty] private double _current;
        [ObservableProperty] private double _capacity;
        [ObservableProperty] private double _energy;
        [ObservableProperty] private double _temperature;
        [ObservableProperty] private double _soc;
        [ObservableProperty] private TimeSpan _stepElapsed;
        [ObservableProperty] private TimeSpan _totalElapsed;

        // 汇总
        [ObservableProperty] private double _totalChargeCapacity;
        [ObservableProperty] private double _totalDischargeCapacity;
        [ObservableProperty] private double _totalChargeEnergy;
        [ObservableProperty] private double _totalDischargeEnergy;

        [ObservableProperty] private TestRecipe _selectedRecipe;
        [ObservableProperty] private int _testRecordId;

        // 历史采样点 - 用于绘图
        public ObservableCollection<DataPoint> RecentSamples { get; } = new();

        public ChannelExecutor Executor => _executor;

        public ChannelViewModel(ChannelExecutor executor, int cabinetIndex = 1)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            CabinetIndex = cabinetIndex;
            _executor.DataSampled += OnDataSampled;
            _executor.StepChanged += OnStepChanged;
            _executor.StatusChanged += OnStatusChanged;
        }

        private void OnDataSampled(object sender, DataSampleEventArgs e)
        {
            App.UIDispatch(() =>
            {
                var d = e.Data;
                Voltage = d.Voltage;
                Current = d.Current;
                Capacity = d.Capacity;
                Energy = d.Energy;
                Temperature = d.Temperature;
                Soc = d.Soc;
                StepElapsed = TimeSpan.FromSeconds(d.ElapsedSeconds);
                TotalElapsed = TimeSpan.FromSeconds(d.TotalElapsedSeconds);
                CurrentLoopIndex = d.LoopIndex;

                TotalChargeCapacity = _executor.TotalChargeCapacity;
                TotalDischargeCapacity = _executor.TotalDischargeCapacity;
                TotalChargeEnergy = _executor.TotalChargeEnergy;
                TotalDischargeEnergy = _executor.TotalDischargeEnergy;

                // 保留近 600 个点用于绘图（约 10 分钟 @1Hz）
                RecentSamples.Add(d);
                while (RecentSamples.Count > 600) RecentSamples.RemoveAt(0);
            });
        }

        private void OnStepChanged(object sender, StepChangedEventArgs e)
        {
            App.UIDispatch(() =>
            {
                if (e.CurrentStep != null)
                {
                    CurrentStepName = $"#{e.CurrentStep.Sequence} {e.CurrentStep.Name}";
                    CurrentStepType = e.CurrentStep.Type;
                    CurrentStepIndex = e.CurrentStep.Sequence;
                }
                CurrentLoopIndex = e.LoopIndex;
            });
        }

        private void OnStatusChanged(object sender, ChannelStatusChangedEventArgs e)
        {
            App.UIDispatch(() =>
            {
                Status = e.Status;
                StatusText = e.Status switch
                {
                    ChannelStatus.Idle => "空闲",
                    ChannelStatus.Running => "运行中",
                    ChannelStatus.Paused => "已暂停",
                    ChannelStatus.Completed => "已完成",
                    ChannelStatus.Stopped => "已停止",
                    ChannelStatus.Error => "故障",
                    ChannelStatus.Protected => $"保护[{e.Protection}]",
                    _ => e.Status.ToString()
                };
            });
        }

        public void Reset()
        {
            Voltage = 0;
            Current = 0;
            Capacity = 0;
            Energy = 0;
            Temperature = 25;
            Soc = 0;
            StepElapsed = TimeSpan.Zero;
            TotalElapsed = TimeSpan.Zero;
            CurrentStepName = "-";
            CurrentLoopIndex = 0;
            TotalChargeCapacity = 0;
            TotalDischargeCapacity = 0;
            TotalChargeEnergy = 0;
            TotalDischargeEnergy = 0;
            RecentSamples.Clear();
        }
    }
}