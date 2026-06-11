using BatteryAging.Core.Models;
using BatteryAging.Core.Enums;

namespace BatteryAging.Communication
{
    /// <summary>采样数据事件参数</summary>
    public class DataSampleEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public DataPoint Data { get; set; }
    }

    /// <summary>工步切换事件参数</summary>
    public class StepChangedEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public TestStep PreviousStep { get; set; }
        public TestStep CurrentStep { get; set; }
        public int LoopIndex { get; set; }
    }

    /// <summary>通道状态变化事件参数</summary>
    public class ChannelStatusChangedEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public ChannelStatus Status { get; set; }
        public string Message { get; set; }
        public ProtectionType Protection { get; set; }
    }

    /// <summary>断点检查点事件参数 - 用于掉电续测的持久化</summary>
    public class CheckpointEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public int StepIndex { get; set; }
        public int LoopIndex { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public double TotalChargeCapacity { get; set; }
        public double TotalDischargeCapacity { get; set; }
        public double TotalChargeEnergy { get; set; }
        public double TotalDischargeEnergy { get; set; }
    }
    public class CycleCompletedEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public int CycleIndex { get; set; }
        public double ChargeCapacity { get; set; }
        public double DischargeCapacity { get; set; }
        public double ChargeEnergy { get; set; }
        public double DischargeEnergy { get; set; }
    }
    public class DcirResultEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public DcirResult Result { get; set; }
    }

    /// <summary>BMS 采样事件（单体电压/多温度）</summary>
    public class BmsSampleEventArgs : EventArgs
    {
        public int ChannelIndex { get; set; }
        public BmsDataPoint Data { get; set; }
    }
}
