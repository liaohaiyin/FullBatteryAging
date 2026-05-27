using CommunityToolkit.Mvvm.ComponentModel;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.ViewModels
{
    public partial class StepViewModel : ObservableObject
    {
        private readonly TestStep _step;
        public TestStep Model => _step;

        public StepViewModel(TestStep step)
        {
            _step = step;
            _sequence = step.Sequence;
            _name = step.Name;
            _type = step.Type;
            _current = step.Current;
            _voltage = step.Voltage;
            _cutoffCurrent = step.CutoffCurrent;
            _cutoffVoltage = step.CutoffVoltage;
            _durationSeconds = step.DurationSeconds;
            _capacityLimit = step.CapacityLimit;
            _triggerType = step.TriggerType;
            _triggerOperator = step.TriggerOperator;
            _triggerValue = step.TriggerValue;
            _loopStartIndex = step.LoopStartIndex;
            _loopCount = step.LoopCount;
            _maxVoltage = step.MaxVoltage;
            _minVoltage = step.MinVoltage;
            _maxCurrent = step.MaxCurrent;
            _maxTemperature = step.MaxTemperature;
            _protectionTimeSeconds = step.ProtectionTimeSeconds;
            _reversePolarityCheck = step.ReversePolarityCheck;
            _maxVoltageDropRate = step.MaxVoltageDropRate;
            _remark = step.Remark;
        }

        [ObservableProperty] private int _sequence;
        partial void OnSequenceChanged(int v) => _step.Sequence = v;

        [ObservableProperty] private string _name;
        partial void OnNameChanged(string v) => _step.Name = v;

        [ObservableProperty] private StepType _type;
        partial void OnTypeChanged(StepType v) => _step.Type = v;

        [ObservableProperty] private double _current;
        partial void OnCurrentChanged(double v) => _step.Current = v;

        [ObservableProperty] private double _voltage;
        partial void OnVoltageChanged(double v) => _step.Voltage = v;

        [ObservableProperty] private double _cutoffCurrent;
        partial void OnCutoffCurrentChanged(double v) => _step.CutoffCurrent = v;

        [ObservableProperty] private double _cutoffVoltage;
        partial void OnCutoffVoltageChanged(double v) => _step.CutoffVoltage = v;

        [ObservableProperty] private double _durationSeconds;
        partial void OnDurationSecondsChanged(double v) => _step.DurationSeconds = v;

        [ObservableProperty] private double _capacityLimit;
        partial void OnCapacityLimitChanged(double v) => _step.CapacityLimit = v;

        [ObservableProperty] private TriggerType _triggerType;
        partial void OnTriggerTypeChanged(TriggerType v) => _step.TriggerType = v;

        [ObservableProperty] private CompareOperator _triggerOperator;
        partial void OnTriggerOperatorChanged(CompareOperator v) => _step.TriggerOperator = v;

        [ObservableProperty] private double _triggerValue;
        partial void OnTriggerValueChanged(double v) => _step.TriggerValue = v;

        [ObservableProperty] private int _loopStartIndex;
        partial void OnLoopStartIndexChanged(int v) => _step.LoopStartIndex = v;

        [ObservableProperty] private int _loopCount;
        partial void OnLoopCountChanged(int v) => _step.LoopCount = v;

        [ObservableProperty] private double _maxVoltage;
        partial void OnMaxVoltageChanged(double v) => _step.MaxVoltage = v;

        [ObservableProperty] private double _minVoltage;
        partial void OnMinVoltageChanged(double v) => _step.MinVoltage = v;

        [ObservableProperty] private double _maxCurrent;
        partial void OnMaxCurrentChanged(double v) => _step.MaxCurrent = v;

        [ObservableProperty] private double _maxTemperature;
        partial void OnMaxTemperatureChanged(double v) => _step.MaxTemperature = v;

        [ObservableProperty] private double _protectionTimeSeconds;
        partial void OnProtectionTimeSecondsChanged(double v) => _step.ProtectionTimeSeconds = v;

        [ObservableProperty] private bool _reversePolarityCheck;
        partial void OnReversePolarityCheckChanged(bool v) => _step.ReversePolarityCheck = v;

        [ObservableProperty] private double _maxVoltageDropRate;
        partial void OnMaxVoltageDropRateChanged(double v) => _step.MaxVoltageDropRate = v;

        [ObservableProperty] private string _remark;
        partial void OnRemarkChanged(string v) => _step.Remark = v;
    }
}
