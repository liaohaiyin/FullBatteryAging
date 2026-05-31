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
            _power = step.Power;
            _resistance = step.Resistance;
            _pulseCurrent = step.PulseCurrent;
            _pulseOnSeconds = step.PulseOnSeconds;
            _pulseOffSeconds = step.PulseOffSeconds;
            _jumpTargetIndex = step.JumpTargetIndex;
        }

        [ObservableProperty] private int _sequence;
        partial void OnSequenceChanged(int value) => _step.Sequence = value;

        [ObservableProperty] private string _name;
        partial void OnNameChanged(string value) => _step.Name = value;

        [ObservableProperty] private StepType _type;
        partial void OnTypeChanged(StepType value) => _step.Type = value;

        [ObservableProperty] private double _current;
        partial void OnCurrentChanged(double value) => _step.Current = value;

        [ObservableProperty] private double _voltage;
        partial void OnVoltageChanged(double value) => _step.Voltage = value;

        [ObservableProperty] private double _cutoffCurrent;
        partial void OnCutoffCurrentChanged(double value) => _step.CutoffCurrent = value;

        [ObservableProperty] private double _cutoffVoltage;
        partial void OnCutoffVoltageChanged(double value) => _step.CutoffVoltage = value;

        [ObservableProperty] private double _durationSeconds;
        partial void OnDurationSecondsChanged(double value) => _step.DurationSeconds = value;

        [ObservableProperty] private double _capacityLimit;
        partial void OnCapacityLimitChanged(double value) => _step.CapacityLimit = value;

        [ObservableProperty] private TriggerType _triggerType;
        partial void OnTriggerTypeChanged(TriggerType value) => _step.TriggerType = value;

        [ObservableProperty] private CompareOperator _triggerOperator;
        partial void OnTriggerOperatorChanged(CompareOperator value) => _step.TriggerOperator = value;

        [ObservableProperty] private double _triggerValue;
        partial void OnTriggerValueChanged(double value) => _step.TriggerValue = value;

        [ObservableProperty] private int _loopStartIndex;
        partial void OnLoopStartIndexChanged(int value) => _step.LoopStartIndex = value;

        [ObservableProperty] private int _loopCount;
        partial void OnLoopCountChanged(int value) => _step.LoopCount = value;

        [ObservableProperty] private double _maxVoltage;
        partial void OnMaxVoltageChanged(double value) => _step.MaxVoltage = value;

        [ObservableProperty] private double _minVoltage;
        partial void OnMinVoltageChanged(double value) => _step.MinVoltage = value;

        [ObservableProperty] private double _maxCurrent;
        partial void OnMaxCurrentChanged(double value) => _step.MaxCurrent = value;

        [ObservableProperty] private double _maxTemperature;
        partial void OnMaxTemperatureChanged(double value) => _step.MaxTemperature = value;

        [ObservableProperty] private double _protectionTimeSeconds;
        partial void OnProtectionTimeSecondsChanged(double value) => _step.ProtectionTimeSeconds = value;

        [ObservableProperty] private bool _reversePolarityCheck;
        partial void OnReversePolarityCheckChanged(bool value) => _step.ReversePolarityCheck = value;

        [ObservableProperty] private double _maxVoltageDropRate;
        partial void OnMaxVoltageDropRateChanged(double value) => _step.MaxVoltageDropRate = value;

        [ObservableProperty] private string _remark;
        partial void OnRemarkChanged(string value) => _step.Remark = value;

        [ObservableProperty] private int _jumpTargetIndex;
        partial void OnJumpTargetIndexChanged(int value) => _step.JumpTargetIndex = value;

        [ObservableProperty] private double _power;
        partial void OnPowerChanged(double value) => _step.Power = value;

        [ObservableProperty] private double _resistance;
        partial void OnResistanceChanged(double value) => _step.Resistance = value;

        [ObservableProperty] private double _pulseCurrent;
        partial void OnPulseCurrentChanged(double value) => _step.PulseCurrent = value;

        [ObservableProperty] private double _pulseOnSeconds;
        partial void OnPulseOnSecondsChanged(double value) => _step.PulseOnSeconds = value;

        [ObservableProperty] private double _pulseOffSeconds;
        partial void OnPulseOffSecondsChanged(double value) => _step.PulseOffSeconds = value;
    }
}
