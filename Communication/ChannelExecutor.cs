using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers;

namespace BatteryAging.Communication
{
    /// <summary>
    /// 单通道执行器
    /// 通过 IDeviceDriver 与设备交互，按工步流程执行充放电测试
    /// </summary>
    public class ChannelExecutor
    {
        public int ChannelIndex { get; }
        public string CabinetId { get; }
        public int LocalChannelIndex { get; }     // 机柜内通道号（1-based）

        private readonly IDeviceDriver _driver;
        private CancellationTokenSource _cts;
        private Task _runningTask;
        // ── 条件跳转 ──
        private int _jumpRequest = -1;
        private int _jumpGuard = 0;                       // 防止死循环的跳转计数
        private const int MaxJumps = 100000;
        private double _cycBaseChgCap, _cycBaseDisCap, _cycBaseChgEng, _cycBaseDisEng;
        private int _completedCycles = 0;        

        // 用于多通道同步触发的屏障（外部传入）
        public Barrier SyncBarrier { get; set; }

        // ── 状态 ──
        public ChannelStatus Status { get; private set; } = ChannelStatus.Idle;
        public TestRecipe CurrentRecipe { get; private set; }
        public TestStep CurrentStep { get; private set; }
        public int CurrentStepIndex { get; private set; }
        public int CurrentLoopIndex { get; private set; }
        public TestRecord CurrentRecord { get; private set; }

        public int CompletedCycles => _completedCycles;

        // ── 实时测量值 ──
        public double Voltage { get; private set; }
        public double Current { get; private set; }
        public double Capacity { get; private set; }
        public double Energy { get; private set; }
        public double Temperature { get; private set; }
        public double StepElapsedSeconds { get; private set; }
        public double TotalElapsedSeconds { get; private set; }

        // 汇总
        public double TotalChargeCapacity { get; private set; }
        public double TotalDischargeCapacity { get; private set; }
        public double TotalChargeEnergy { get; private set; }
        public double TotalDischargeEnergy { get; private set; }
        public IClimateChamber Chamber { get; set; }

        // ── dV/dt 检测 ──
        private double _lastVoltage = double.NaN;
        private DateTime _lastVoltageTime;

        // ── 通讯丢失检测 ──
        private int _consecutiveCommErrors = 0;
        private const int MaxConsecutiveCommErrors = 5;

        // ── 配置 ──
        public int SamplingIntervalMs { get; set; } = 1000;

        // ── 事件 ──
        public event EventHandler<DataSampleEventArgs> DataSampled;
        public event EventHandler<StepChangedEventArgs> StepChanged;
        public event EventHandler<ChannelStatusChangedEventArgs> StatusChanged;
        public event EventHandler<CheckpointEventArgs> CheckpointReached;
        public event EventHandler<CycleCompletedEventArgs> CycleCompleted;
        public event EventHandler<DcirResultEventArgs> DcirMeasured;

        public ChannelExecutor(int globalChannelIndex, IDeviceDriver driver,
            string cabinetId = null, int localChannelIndex = 0)
        {
            ChannelIndex = globalChannelIndex;
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            CabinetId = cabinetId;
            LocalChannelIndex = localChannelIndex > 0 ? localChannelIndex : globalChannelIndex;
            Temperature = 25;
        }

        /// <summary>启动测试（从头开始或从断点恢复）</summary>
        public Task StartAsync(TestRecipe recipe, TestRecord record, bool resumeFromCheckpoint = false)
        {
            if (Status == ChannelStatus.Running)
                throw new InvalidOperationException("通道正在运行");

            CurrentRecipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            CurrentRecord = record;

            if (resumeFromCheckpoint && record != null)
            {
                // 从断点恢复
                CurrentStepIndex = Math.Max(0, Math.Min(record.LastStepIndex, recipe.Steps.Count - 1));
                CurrentLoopIndex = record.LastLoopIndex;
                TotalElapsedSeconds = record.LastTotalElapsed;
                TotalChargeCapacity = record.TotalChargeCapacity;
                TotalDischargeCapacity = record.TotalDischargeCapacity;
                TotalChargeEnergy = record.TotalChargeEnergy;
                TotalDischargeEnergy = record.TotalDischargeEnergy;
                _cycBaseChgCap = record.TotalChargeCapacity;
                _cycBaseDisCap = record.TotalDischargeCapacity;
                _cycBaseChgEng = record.TotalChargeEnergy;
                _cycBaseDisEng = record.TotalDischargeEnergy;
            }
            else
            {
                CurrentStepIndex = 0;
                CurrentLoopIndex = 0;
                TotalElapsedSeconds = 0;
                TotalChargeCapacity = 0;
                TotalDischargeCapacity = 0;
                TotalChargeEnergy = 0;
                TotalDischargeEnergy = 0;
                _cycBaseChgCap = 0;
                _cycBaseDisCap = 0;
                _cycBaseChgEng = 0;
                _cycBaseDisEng = 0;
            }

            _consecutiveCommErrors = 0;
            _lastVoltage = double.NaN;
            _cts = new CancellationTokenSource();
            if (_driver is SimulatorDriver sim)
                sim.SetNominalCapacity(LocalChannelIndex, recipe.NominalCapacity);
            ChangeStatus(ChannelStatus.Running, resumeFromCheckpoint ? "从断点恢复" : "测试启动");

            _runningTask = Task.Run(() => RunLoop(_cts.Token), _cts.Token);
            return _runningTask;
        }

        public void Pause()
        {
            if (Status == ChannelStatus.Running)
                ChangeStatus(ChannelStatus.Paused, "已暂停");
        }

        public void Resume()
        {
            if (Status == ChannelStatus.Paused)
                ChangeStatus(ChannelStatus.Running, "已恢复");
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _driver.StopChannelAsync(LocalChannelIndex).Wait(500); } catch { }
            ChangeStatus(ChannelStatus.Stopped, "用户停止");
        }

        // ════════════════════════════════════════════════════════════
        //  主循环
        // ════════════════════════════════════════════════════════════
        private async Task RunLoop(CancellationToken token)
        {
            try
            {
                // 多通道同步触发：等待所有通道就绪
                if (SyncBarrier != null)
                {
                    try { SyncBarrier.SignalAndWait(token); }
                    catch (BarrierPostPhaseException) { }
                    catch (OperationCanceledException) { return; }
                }

                while (CurrentStepIndex < CurrentRecipe.Steps.Count && !token.IsCancellationRequested)
                {
                    var step = CurrentRecipe.Steps[CurrentStepIndex];
                    var previous = CurrentStep;
                    CurrentStep = step;

                    StepChanged?.Invoke(this, new StepChangedEventArgs
                    {
                        ChannelIndex = ChannelIndex,
                        PreviousStep = previous,
                        CurrentStep = step,
                        LoopIndex = CurrentLoopIndex
                    });

                    if (step.Type == StepType.Loop)
                    {
                        // 刚跑完一圈循环体，先结算（无论是不是最后一圈）
                        CycleCompleted?.Invoke(this, new CycleCompletedEventArgs
                        {
                            ChannelIndex = ChannelIndex,
                            CycleIndex = CurrentLoopIndex + 1,
                            ChargeCapacity = TotalChargeCapacity - _cycBaseChgCap,
                            DischargeCapacity = TotalDischargeCapacity - _cycBaseDisCap,
                            ChargeEnergy = TotalChargeEnergy - _cycBaseChgEng,
                            DischargeEnergy = TotalDischargeEnergy - _cycBaseDisEng
                        });
                        _cycBaseChgCap = TotalChargeCapacity;
                        _cycBaseDisCap = TotalDischargeCapacity;
                        _cycBaseChgEng = TotalChargeEnergy;
                        _cycBaseDisEng = TotalDischargeEnergy;
                        _completedCycles = CurrentLoopIndex + 1;

                        if (CurrentLoopIndex < step.LoopCount - 1)
                        {
                            CurrentLoopIndex++;
                            CurrentStepIndex = Math.Max(0, step.LoopStartIndex);
                            continue;
                        }
                        else
                        {
                            CurrentLoopIndex = 0;
                            CurrentStepIndex++;
                            continue;
                        }
                    }

                    if (step.Type == StepType.End) break;

                    // ── 反接保护检测（启动前） ──
                    if (step.ReversePolarityCheck && step.Type != StepType.Rest)
                    {
                        var prot = await CheckReversePolarityAsync(token);
                        if (prot != ProtectionType.None)
                        {
                            ChangeStatus(ChannelStatus.Protected, $"保护触发: {prot}", prot);
                            return;
                        }
                    }

                    if (Chamber != null && step.TargetTemperature is double tt)
                    {
                        try
                        {
                            await Chamber.SetTemperatureAsync(tt, token);
                            await Chamber.RunAsync(true, token);
                            if (step.WaitForTempStable)
                                await Chamber.WaitForTemperatureAsync(tt, 1.0, 120, 7200, null, token);
                        }
                        catch { /* 温箱异常不阻断主流程，按需改为保护 */ }
                    }
                    // ── DCIR 工步 ──
                    if (step.Type == StepType.DCIR)
                    {
                        var profile = new Services.DcirProfile
                        {
                            PulseCurrent = step.PulseCurrent != 0 ? step.PulseCurrent : -Math.Abs(step.Current),
                            PreRestSeconds = step.PulseOffSeconds > 0 ? step.PulseOffSeconds : 30,
                            SampleTimes = new double[] { 1, 10, Math.Max(11, step.PulseOnSeconds) }
                        };
                        var dcir = await Services.DcirCalculator.MeasureAsync(
                            _driver, LocalChannelIndex, profile, EstimateSoc(),
                            CurrentRecord?.Id ?? 0, ChannelIndex, token);
                        DcirMeasured?.Invoke(this, new DcirResultEventArgs { ChannelIndex = ChannelIndex, Result = dcir });
                        CurrentStepIndex++;
                        continue;   // DCIR 自成一步，跳过常规 ExecuteStepAsync
                    }
                    var stepProt = await ExecuteStepAsync(step, token);
                    if (stepProt != ProtectionType.None)
                    {
                        ChangeStatus(ChannelStatus.Protected, $"保护触发: {stepProt}", stepProt);
                        return;
                    }

                    if (_jumpRequest >= 0 && _jumpRequest < CurrentRecipe.Steps.Count)
                    {
                        if (++_jumpGuard > MaxJumps)
                        {
                            ChangeStatus(ChannelStatus.Error, "跳转次数超限，疑似死循环");
                            return;
                        }
                        CurrentStepIndex = _jumpRequest;
                        _jumpRequest = -1;
                        continue;
                    }
                    _jumpRequest = -1;
                    CurrentStepIndex++;
                }

                if (Status == ChannelStatus.Running)
                    ChangeStatus(ChannelStatus.Completed, "测试完成");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ChangeStatus(ChannelStatus.Error, $"运行异常: {ex.Message}");
            }
        }

        /// <summary>反接保护：先施加 0 电流采样，若端电压 < -0.5V 视为反接</summary>
        private async Task<ProtectionType> CheckReversePolarityAsync(CancellationToken token)
        {
            try
            {
                await _driver.ApplyStepAsync(LocalChannelIndex,
                    new StepSetpoint { Type = StepType.Rest }, token);
                await Task.Delay(200, token);
                var m = await _driver.ReadAsync(LocalChannelIndex, token);
                if (m.Voltage < -0.5) return ProtectionType.ReversePolarity;
                return ProtectionType.None;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 通讯异常先不算反接，主循环会单独报错
                return ProtectionType.None;
            }
        }

        /// <summary>执行单个工步直到截止条件满足</summary>
        private async Task<ProtectionType> ExecuteStepAsync(TestStep step, CancellationToken token)
        {
            Capacity = 0;
            Energy = 0;
            StepElapsedSeconds = 0;
            _lastVoltage = double.NaN;

            var dtSec = SamplingIntervalMs / 1000.0;

            // 下发工步设定给驱动
            var setpoint = new StepSetpoint
            {
                Type = step.Type,
                Current = step.Current,
                Voltage = step.Voltage,
                CutoffCurrent = step.CutoffCurrent,
                CutoffVoltage = step.CutoffVoltage,
                Power = step.Power,
                Resistance = step.Resistance,
                PulseCurrent = step.PulseCurrent,
                PulseOnSeconds = step.PulseOnSeconds,
                PulseOffSeconds = step.PulseOffSeconds
            };
            try
            {
                await _driver.ApplyStepAsync(LocalChannelIndex, setpoint, token);
            }
            catch (Exception ex)
            {
                ChangeStatus(ChannelStatus.Error, $"下发工步失败: {ex.Message}");
                return ProtectionType.CommunicationLost;
            }

            while (!token.IsCancellationRequested)
            {
                while (Status == ChannelStatus.Paused && !token.IsCancellationRequested)
                    await Task.Delay(100, token);

                // ── 采样 ──
                DeviceMeasurement m;
                try
                {
                    m = await _driver.ReadAsync(LocalChannelIndex, token);
                    _consecutiveCommErrors = 0;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _consecutiveCommErrors++;
                    if (_consecutiveCommErrors >= MaxConsecutiveCommErrors)
                    {
                        ChangeStatus(ChannelStatus.Error, $"通讯持续异常({_consecutiveCommErrors}次): {ex.Message}");
                        return ProtectionType.CommunicationLost;
                    }
                    await Task.Delay(SamplingIntervalMs, token);
                    continue;
                }

                Voltage = m.Voltage;
                Current = m.Current;
                Temperature = m.Temperature;

                // ── 累计 ──
                var dAh = Math.Abs(m.Current) * dtSec / 3600.0;
                var dWh = Math.Abs(m.Current) * m.Voltage * dtSec / 3600.0;
                Capacity += dAh;
                Energy += dWh;

                if (step.Type == StepType.Pulse)
                {
                    if (m.Current > 0) { TotalChargeCapacity += dAh; TotalChargeEnergy += dWh; }
                    else if (m.Current < 0) { TotalDischargeCapacity += dAh; TotalDischargeEnergy += dWh; }
                }
                else if (IsChargeStep(step.Type))
                {
                    TotalChargeCapacity += dAh;
                    TotalChargeEnergy += dWh;
                }
                else if (IsDischargeStep(step.Type))
                {
                    TotalDischargeCapacity += dAh;
                    TotalDischargeEnergy += dWh;
                }

                StepElapsedSeconds += dtSec;
                TotalElapsedSeconds += dtSec;

                // ── 采样事件 ──
                var sample = new DataPoint
                {
                    TestRecordId = CurrentRecord?.Id ?? 0,
                    ChannelIndex = ChannelIndex,
                    StepSequence = step.Sequence,
                    StepType = step.Type,
                    LoopIndex = CurrentLoopIndex,
                    Timestamp = m.Timestamp,
                    ElapsedSeconds = StepElapsedSeconds,
                    TotalElapsedSeconds = TotalElapsedSeconds,
                    Voltage = Math.Round(Voltage, 4),
                    Current = Math.Round(Current, 4),
                    Capacity = Math.Round(Capacity, 5),
                    Energy = Math.Round(Energy, 5),
                    Temperature = Math.Round(Temperature, 2),
                    Soc = EstimateSoc()
                };
                DataSampled?.Invoke(this, new DataSampleEventArgs
                {
                    ChannelIndex = ChannelIndex,
                    Data = sample
                });

                // ── 断点写入（每 10 秒触发一次） ──
                if ((int)StepElapsedSeconds % 10 == 0)
                {
                    CheckpointReached?.Invoke(this, new CheckpointEventArgs
                    {
                        ChannelIndex = ChannelIndex,
                        StepIndex = CurrentStepIndex,
                        LoopIndex = CurrentLoopIndex,
                        TotalElapsedSeconds = TotalElapsedSeconds,
                        TotalChargeCapacity = TotalChargeCapacity,
                        TotalDischargeCapacity = TotalDischargeCapacity,
                        TotalChargeEnergy = TotalChargeEnergy,
                        TotalDischargeEnergy = TotalDischargeEnergy
                    });
                }

                // ── 保护检查 ──
                var prot = CheckProtection(step, m);
                if (prot != ProtectionType.None) return prot;

                if (IsStepFinished(step)) break;

                try { await Task.Delay(SamplingIntervalMs, token); }
                catch (TaskCanceledException) { break; }
            }

            return ProtectionType.None;
        }

        private bool IsChargeStep(StepType t) => t == StepType.CC_Charge || t == StepType.CV_Charge || t == StepType.CCCV_Charge || t == StepType.CP_Charge;

        private bool IsDischargeStep(StepType t) => t == StepType.CC_Discharge || t == StepType.CP_Discharge || t == StepType.CR_Discharge;

        private bool IsStepFinished(TestStep step)
        {
            if (step.DurationSeconds > 0 && StepElapsedSeconds >= step.DurationSeconds) return true;
            if (step.CapacityLimit > 0 && Capacity >= step.CapacityLimit) return true;

            switch (step.Type)
            {
                case StepType.CC_Charge:
                    if (step.CutoffVoltage > 0 && Voltage >= step.CutoffVoltage) return true;
                    break;
                case StepType.CC_Discharge:
                case StepType.CP_Discharge:
                case StepType.CR_Discharge:
                    if (step.CutoffVoltage > 0 && Voltage <= step.CutoffVoltage) return true;
                    break;
                case StepType.CP_Charge:
                    if (step.CutoffVoltage > 0 && Voltage >= step.CutoffVoltage) return true;
                    break;
                case StepType.CV_Charge:
                case StepType.CCCV_Charge:
                    {
                        var cvTarget = step.Voltage > 0 ? step.Voltage : step.CutoffVoltage;
                        if (step.CutoffCurrent > 0 && cvTarget > 0
                            && Voltage >= cvTarget - 0.005
                            && Math.Abs(Current) <= step.CutoffCurrent) return true;
                    }
                    break;
            }
            return CheckTrigger(step);
        }

        private bool CheckTrigger(TestStep step)
        {
            double actual = step.TriggerType switch
            {
                TriggerType.Time => StepElapsedSeconds,
                TriggerType.Voltage => Voltage,
                TriggerType.Current => Math.Abs(Current),
                TriggerType.Capacity => Capacity,
                TriggerType.Energy => Energy,
                TriggerType.Temperature => Temperature,
                _ => double.NaN
            };
            if (double.IsNaN(actual) || step.TriggerValue == 0) return false;

            bool hit = step.TriggerOperator switch
            {
                CompareOperator.Greater => actual > step.TriggerValue,
                CompareOperator.GreaterOrEqual => actual >= step.TriggerValue,
                CompareOperator.Less => actual < step.TriggerValue,
                CompareOperator.LessOrEqual => actual <= step.TriggerValue,
                CompareOperator.Equal => Math.Abs(actual - step.TriggerValue) < 0.001,
                _ => false
            };

            if (hit && step.JumpTargetIndex >= 0)
                _jumpRequest = step.JumpTargetIndex;       // 记录跳转目标
            return hit;                                     // 命中即结束本步
        }

        private ProtectionType CheckProtection(TestStep step, DeviceMeasurement m)
        {
            // 反接（运行中也可能因接触不良发生）
            if (m.Voltage < -0.5) return ProtectionType.ReversePolarity;

            if (step.MaxVoltage > 0 && m.Voltage > step.MaxVoltage + 0.05)
                return ProtectionType.OverVoltage;
            if (step.MinVoltage > 0 && m.Voltage < step.MinVoltage - 0.05 && Math.Abs(m.Current) > 0.001)
                return ProtectionType.UnderVoltage;
            if (step.MaxCurrent > 0 && Math.Abs(m.Current) > step.MaxCurrent + 0.1)
                return ProtectionType.OverCurrent;
            if (step.MaxTemperature > 0 && m.Temperature > step.MaxTemperature)
                return ProtectionType.OverTemperature;
            if (step.ProtectionTimeSeconds > 0 && StepElapsedSeconds > step.ProtectionTimeSeconds)
                return ProtectionType.Timeout;

            // dV/dt 跌落速率检测（安全放电保护）
            if (step.MaxVoltageDropRate > 0 && !double.IsNaN(_lastVoltage))
            {
                var dt = (m.Timestamp - _lastVoltageTime).TotalSeconds;
                if (dt > 0)
                {
                    var dropRate = (_lastVoltage - m.Voltage) / dt;
                    if (dropRate > step.MaxVoltageDropRate)
                        return ProtectionType.VoltageDropAnomaly;
                }
            }
            _lastVoltage = m.Voltage;
            _lastVoltageTime = m.Timestamp;

            return ProtectionType.None;
        }

        /// <summary>简单 SOC 估算：基于容量累计</summary>
        private double EstimateSoc()
        {
            if (CurrentRecipe == null || CurrentRecipe.NominalCapacity <= 0) return 0;
            // 当驱动是模拟器时直接读 SOC
            if (_driver is SimulatorDriver sim)
            {
                var b = sim.GetBattery(LocalChannelIndex);
                if (b != null) return Math.Round(b.Soc * 100, 2);
            }
            // 真实设备：通过净充放容量估算
            var net = TotalChargeCapacity - TotalDischargeCapacity;
            return Math.Round(Math.Clamp(net / CurrentRecipe.NominalCapacity * 100, 0, 100), 2);
        }

        private void ChangeStatus(ChannelStatus newStatus, string message,
            ProtectionType protection = ProtectionType.None)
        {
            Status = newStatus;
            StatusChanged?.Invoke(this, new ChannelStatusChangedEventArgs
            {
                ChannelIndex = ChannelIndex,
                Status = newStatus,
                Message = message,
                Protection = protection
            });
        }
    }
}
