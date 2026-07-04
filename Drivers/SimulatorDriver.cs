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
        private readonly ConcurrentDictionary<int, double> _setpointElapsed = new();
        private readonly int _samplingIntervalMs;

        public DriverType DriverType => DriverType.Simulator;
        public bool IsConnected { get; private set; }

        public event EventHandler<string> CommunicationError;

        public SimulatorDriver(int channelCount, int samplingIntervalMs = 1000)
        {
            _samplingIntervalMs = samplingIntervalMs;
            for (int i = 1; i <= channelCount; i++)
            {
                // 各通道给一个错开的初始 SOC（20%~60% 循环分布）而不是全部相同，
                // 纯粹是为了让模拟环境下多通道界面看起来更真实、便于演示/测试多通道场景，
                // 与实际电芯状态无关；真实容量/内阻会在 StartAsync 时按方案覆盖。
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
            _setpointElapsed[channelIndex] = 0;
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
            _setpointElapsed.AddOrUpdate(channelIndex, dt, (_, v) => v + dt);
            battery.Step(appliedCurrent, dt);

            return Task.FromResult(new DeviceMeasurement
            {
                Voltage = battery.GetTerminalVoltage(appliedCurrent),
                Current = appliedCurrent,
                Temperature = battery.Temperature,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// 按当前工步类型把"设定值"换算成模拟电池实际要通过的电流：
        /// CC 直接用设定电流；CV/CP/CR 需要结合电池当前 OCV 反推电流；
        /// Pulse 靠已过时间在导通/间歇区间之间来回切换。这一层就是模拟器对
        /// 真实设备"恒压/恒功率/恒阻"闭环控制的近似还原，好让上层 ChannelExecutor
        /// 采到的电压/电流曲线跟真实充放电台架的行为一致。
        /// </summary>
        private double ComputeCurrent(StepSetpoint sp, BatteryCellSimulator battery, int chIdx)
        {
            if (sp == null) return 0;
            double v = battery.GetOcv(battery.Soc);      // 用 OCV 近似端电压做功率/电阻换算
            if (v < 0.5) v = 0.5;                          // 防除零
            return sp.Type switch
            {
                StepType.CC_Charge => Math.Abs(sp.Current),
                StepType.CC_Discharge => -Math.Abs(sp.Current),
                StepType.CV_Charge => battery.GetCvCurrent(sp.Voltage, Math.Abs(sp.Current)),
                StepType.CCCV_Charge => ComputeCccv(sp, battery, chIdx),
                StepType.CP_Charge => Math.Abs(sp.Power) / v,
                StepType.CP_Discharge => -Math.Abs(sp.Power) / v,
                StepType.CR_Discharge => sp.Resistance > 0 ? -(v / sp.Resistance) : 0,
                StepType.Pulse => ComputePulse(sp, chIdx),
                StepType.Rest => 0,
                _ => 0
            };
        }

        private double ComputePulse(StepSetpoint sp, int chIdx)
        {
            var period = sp.PulseOnSeconds + sp.PulseOffSeconds;
            if (period <= 0) return sp.PulseCurrent;
            var t = _setpointElapsed.TryGetValue(chIdx, out var e) ? e : 0;
            var phase = t % period;
            return phase < sp.PulseOnSeconds ? sp.PulseCurrent : 0;   // 正充负放由 PulseCurrent 符号决定
        }

        private double ComputeCccv(StepSetpoint sp, BatteryCellSimulator battery, int chIdx)
        {
            var cc = Math.Abs(sp.Current);
            // CV 平台电压：优先用设定电压 Voltage，与 ChannelExecutor 的截止判断保持一致；
            // 若未填则回退到 CutoffVoltage
            var cvTarget = sp.Voltage > 0 ? sp.Voltage : sp.CutoffVoltage;
            if (cvTarget <= 0) return cc;   // 无有效目标电压，按恒流处理

            var ocv = battery.GetOcv(battery.Soc);
            // 端电压 ≈ OCV + I*R 估算
            var estTerminal = ocv + cc * battery.InternalResistance;
            if (estTerminal >= cvTarget - 0.001)
                return battery.GetCvCurrent(cvTarget, cc);
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
