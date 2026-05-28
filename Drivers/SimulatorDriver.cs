using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;
using BatteryAging.Simulation;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 模拟器驱动 - 用 BatteryCellSimulator 模拟一个机柜内多个通道
    /// </summary>
    public class SimulatorDriver : IDeviceDriver
    {
        private readonly ConcurrentDictionary<int, BatteryCellSimulator> _batteries = new();
        private readonly ConcurrentDictionary<int, StepSetpoint> _setpoints = new();
        private readonly ConcurrentDictionary<int, double> _lastCvCurrents = new();
        private readonly int _samplingIntervalMs;
        private DateTime _lastReadTime = DateTime.Now;

        public DriverType DriverType => DriverType.Simulator;
        public bool IsConnected { get; private set; }

        public event EventHandler<string> CommunicationError;

        public SimulatorDriver(int channelCount, int samplingIntervalMs = 1000)
        {
            _samplingIntervalMs = samplingIntervalMs;
            for (int i = 1; i <= channelCount; i++)
            {
                _batteries[i] = new BatteryCellSimulator(
                    initialSoc: 0.2 + (i * 0.05) % 0.4,
                    nominalCapacity: 2.6, internalResistance: 0.15);
            }
        }

        public Task<bool> ConnectAsync(CancellationToken token = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync(CancellationToken token = default)
            => Task.FromResult(IsConnected);

        public Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default)
        {
            _setpoints[channelIndex] = setpoint;
            return Task.CompletedTask;
        }

        public Task StopChannelAsync(int channelIndex, CancellationToken token = default)
        {
            _setpoints.TryRemove(channelIndex, out _);
            return Task.CompletedTask;
        }

        public Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
        {
            if (!_batteries.TryGetValue(channelIndex, out var battery))
                throw new InvalidOperationException($"通道 {channelIndex} 未配置");

            var setpoint = _setpoints.TryGetValue(channelIndex, out var sp) ? sp : null;
            double appliedCurrent = ComputeCurrent(setpoint, battery, channelIndex);

            // 推进电池模拟
            double dt = _samplingIntervalMs / 1000.0;
            battery.Step(appliedCurrent, dt);

            return Task.FromResult(new DeviceMeasurement
            {
                Voltage = battery.GetTerminalVoltage(appliedCurrent),
                Current = appliedCurrent,
                Temperature = battery.Temperature,
                Timestamp = DateTime.Now
            });
        }

        private double ComputeCurrent(StepSetpoint sp, BatteryCellSimulator battery, int chIdx)
        {
            if (sp == null) return 0;
            return sp.Type switch
            {
                StepType.CC_Charge => Math.Abs(sp.Current),
                StepType.CC_Discharge => -Math.Abs(sp.Current),
                StepType.CV_Charge => battery.GetCvCurrent(sp.Voltage, Math.Abs(sp.Current)),
                StepType.CCCV_Charge => ComputeCccv(sp, battery, chIdx),
                StepType.Rest => 0,
                _ => 0
            };
        }

        private double ComputeCccv(StepSetpoint sp, BatteryCellSimulator battery, int chIdx)
        {
            var cc = Math.Abs(sp.Current);
            var ocv = battery.GetOcv(battery.Soc);
            // 端电压 ≈ OCV + I*R 估算
            var estTerminal = ocv + cc * battery.InternalResistance;
            if (estTerminal >= sp.CutoffVoltage - 0.001)
                return battery.GetCvCurrent(sp.CutoffVoltage, cc);
            return cc;
        }

        /// <summary>
        /// 电池模拟器访问入口（用于 SOC 等内部状态读取）
        /// </summary>
        public BatteryCellSimulator GetBattery(int channelIndex)
            => _batteries.TryGetValue(channelIndex, out var b) ? b : null;

        /// <summary>
        /// 设置指定通道电池的标称容量（用于让模拟器跟随测试方案）
        /// </summary>
        public void SetNominalCapacity(int channelIndex, double nominalCapacity)
        {
            if (nominalCapacity > 0 && _batteries.TryGetValue(channelIndex, out var b))
                b.NominalCapacity = nominalCapacity;
        }

        public void Dispose() => IsConnected = false;
    }
}
