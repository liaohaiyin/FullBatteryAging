using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 设备测量结果
    /// </summary>
    public class DeviceMeasurement
    {
        public double Voltage { get; set; }       // 端电压 (V)
        public double Current { get; set; }       // 电流 (A) 正充负放
        public double Temperature { get; set; }   // 温度 (°C)
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 工步设定参数（驱动层使用，与 TestStep 解耦）
    /// </summary>
    public class StepSetpoint
    {
        public StepType Type { get; set; }
        public double Current { get; set; }
        public double Voltage { get; set; }
        public double CutoffCurrent { get; set; }
        public double CutoffVoltage { get; set; }
    }

    /// <summary>
    /// 设备驱动接口
    /// 抽象具体设备厂商的通讯实现（新威/蓝电/致茂/模拟器等）
    /// </summary>
    public interface IDeviceDriver : IDisposable
    {
        DriverType DriverType { get; }

        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>连接到设备</summary>
        Task<bool> ConnectAsync(CancellationToken token = default);

        /// <summary>断开连接</summary>
        Task DisconnectAsync();

        /// <summary>检测心跳（用于通讯监测）</summary>
        Task<bool> PingAsync(CancellationToken token = default);

        /// <summary>对指定通道下达工步命令</summary>
        Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default);

        /// <summary>停止指定通道</summary>
        Task StopChannelAsync(int channelIndex, CancellationToken token = default);

        /// <summary>采集指定通道实时数据</summary>
        Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default);

        /// <summary>通讯异常事件</summary>
        event EventHandler<string> CommunicationError;
    }
}
