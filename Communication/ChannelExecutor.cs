using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers;

namespace BatteryAging.Communication
{
    /// <summary>
    /// 单通道执行器 —— 一个通道对应一个后台任务(<see cref="RunLoop"/>)，
    /// 按 TestRecipe.Steps 顺序逐工步下发设定值、采样、判断截止条件，
    /// 直到方案跑完或触发保护。所有状态变化通过事件向外广播，
    /// UI 层（ChannelViewModel）和持久化层（TestExecutionViewModel）都是事件订阅者，
    /// 本类本身不直接操作数据库/UI。
    /// </summary>
    public class ChannelExecutor
    {
        /// <summary>全局通道号（跨机柜连续编号），UI/数据库都用这个区分通道</summary>
        public int ChannelIndex { get; }
        /// <summary>所属机柜 Id</summary>
        public string CabinetId { get; }
        /// <summary>机柜内本地通道号（1-based），与设备驱动通信时使用</summary>
        public int LocalChannelIndex { get; }

        private readonly IDeviceDriver _driver;
        private CancellationTokenSource _cts;
        private Task _runningTask;
        // ── 条件跳转 ──
        // 触发条件命中且配置了 JumpTargetIndex 时（见 CheckTrigger），本步结束后不是顺序 +1
        // 而是跳到 _jumpRequest 指定的工步；_jumpGuard 统计跳转次数，超过 MaxJumps 视为
        // 方案配置错误导致的死循环（例如跳转目标又跳回自身），主动报错而不是无限执行下去。
        private int _jumpRequest = -1;
        private int _jumpGuard = 0;
        private const int MaxJumps = 100000;
        // 每次进入 Loop 工步结算一圈时，把当前累计充放电量/能量存为下一圈的起点，
        // 这样 CycleCompleted 事件里报告的才是"本圈"增量，而不是从测试开始累计的总量。
        private double _cycBaseChgCap, _cycBaseDisCap, _cycBaseChgEng, _cycBaseDisEng;
        private int _completedCycles = 0;

        /// <summary>多通道同步启动屏障，由 ChannelManager.SyncStartAsync 注入，见该方法说明</summary>
        public Barrier SyncBarrier { get; set; }

        // ── 状态 ──
        /// <summary>通道当前运行状态</summary>
        public ChannelStatus Status { get; private set; } = ChannelStatus.Idle;
        /// <summary>当前正在执行的测试方案（含全部工步）</summary>
        public TestRecipe CurrentRecipe { get; private set; }
        /// <summary>当前正在执行的工步</summary>
        public TestStep CurrentStep { get; private set; }
        /// <summary>当前工步在方案中的索引（0-based）</summary>
        public int CurrentStepIndex { get; private set; }
        /// <summary>当前处于 Loop 工步的第几轮（0-based）</summary>
        public int CurrentLoopIndex { get; private set; }
        /// <summary>本次运行对应的测试记录（落库实体）</summary>
        public TestRecord CurrentRecord { get; private set; }

        /// <summary>已完整跑完的循环圈数</summary>
        public int CompletedCycles => _completedCycles;

        // ── 实时测量值 ──
        /// <summary>最近一次采样的端电压 (V)</summary>
        public double Voltage { get; private set; }
        /// <summary>最近一次采样的电流 (A)，正充负放</summary>
        public double Current { get; private set; }
        /// <summary>当前工步内累计容量 (Ah)，进入新工步时清零</summary>
        public double Capacity { get; private set; }
        /// <summary>当前工步内累计能量 (Wh)，进入新工步时清零</summary>
        public double Energy { get; private set; }
        /// <summary>最近一次采样的温度 (℃)</summary>
        public double Temperature { get; private set; }
        /// <summary>当前工步已运行时长 (s)</summary>
        public double StepElapsedSeconds { get; private set; }
        /// <summary>本次测试从启动至今的总运行时长 (s)，跨工步累计、断点恢复时接续</summary>
        public double TotalElapsedSeconds { get; private set; }

        // 汇总
        /// <summary>本次测试累计充电容量 (Ah)</summary>
        public double TotalChargeCapacity { get; private set; }
        /// <summary>本次测试累计放电容量 (Ah)</summary>
        public double TotalDischargeCapacity { get; private set; }
        /// <summary>本次测试累计充电能量 (Wh)</summary>
        public double TotalChargeEnergy { get; private set; }
        /// <summary>本次测试累计放电能量 (Wh)</summary>
        public double TotalDischargeEnergy { get; private set; }
        /// <summary>联动的环境温箱，为 null 时不做温控联动</summary>
        public IClimateChamber Chamber { get; set; }

        // ── BMS（PACK 单体/多温度采集）──
        /// <summary>联动的 BMS 采集驱动，为 null 时不采集单体电压/多点温度</summary>
        public IBmsDriver Bms { get; set; }
        /// <summary>PACK 单体节数（用于分配 CellVoltages 数组大小等）</summary>
        public int CellCount { get; set; }
        /// <summary>温度采集点数</summary>
        public int TempPointCount { get; set; }
        private int _bmsConsecErrors = 0;
        private const int MaxBmsErrors = 5;

        // BMS 实时派生（供 UI 直读）
        /// <summary>最近一次 BMS 采样的单体最高电压 (V)</summary>
        public double MaxCellVoltage { get; private set; }
        /// <summary>最近一次 BMS 采样的单体最低电压 (V)</summary>
        public double MinCellVoltage { get; private set; }
        /// <summary>最近一次 BMS 采样的单体压差 (V) = 最高 - 最低</summary>
        public double CellVoltageDelta { get; private set; }
        /// <summary>最近一次 BMS 采样的 PACK 最高温度 (℃)</summary>
        public double MaxPackTemperature { get; private set; }
        /// <summary>最近一次 BMS 采样的温差 (℃)</summary>
        public double TempDelta { get; private set; }

        // ── dV/dt 检测 ──
        private double _lastVoltage = double.NaN;
        private DateTime _lastVoltageTime;

        // ── 工况仿真波形 (StepType.Waveform) ──
        private List<WaveformPoint> _currentWaveform;

        // ── dT/dt 温升速率检测（早于绝对温度阈值发现潜在热失控趋势）──
        private double _lastTempForRate = double.NaN;
        private DateTime _lastTempRateTime;

        // ── 通讯丢失检测 ──
        private int _consecutiveCommErrors = 0;
        private const int MaxConsecutiveCommErrors = 5;

        // ── 配置 ──
        /// <summary>采样/下发周期 (ms)，决定每个工步内轮询设备的频率</summary>
        public int SamplingIntervalMs { get; set; } = 1000;

        // ── 事件 ──
        /// <summary>每次采样产生一条 DataPoint 时触发，供落库/绘图订阅</summary>
        public event EventHandler<DataSampleEventArgs> DataSampled;
        /// <summary>切换到新工步时触发</summary>
        public event EventHandler<StepChangedEventArgs> StepChanged;
        /// <summary>运行状态变化时触发（含保护/故障信息）</summary>
        public event EventHandler<ChannelStatusChangedEventArgs> StatusChanged;
        /// <summary>断点信息更新时触发（约每 10 秒一次），供持久化层写入续测断点</summary>
        public event EventHandler<CheckpointEventArgs> CheckpointReached;
        /// <summary>Loop 工步跑完一圈时触发，携带本圈充放电量/能量增量</summary>
        public event EventHandler<CycleCompletedEventArgs> CycleCompleted;
        /// <summary>DCIR 内阻测量完成时触发</summary>
        public event EventHandler<DcirResultEventArgs> DcirMeasured;
        /// <summary>BMS 采到新一帧单体电压/温度数据时触发</summary>
        public event EventHandler<BmsSampleEventArgs> BmsSampled;

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
            _lastTempForRate = double.NaN;
            _cts = new CancellationTokenSource();
            if (_driver is SimulatorDriver sim)
                sim.SetNominalCapacity(LocalChannelIndex, recipe.NominalCapacity);
            ChangeStatus(ChannelStatus.Running, resumeFromCheckpoint ? "从断点恢复" : "测试启动");

            _runningTask = Task.Run(() => RunLoop(_cts.Token), _cts.Token);
            return _runningTask;
        }

        /// <summary>暂停：仅切换状态标志，采样循环仍在跑但会在 Paused 期间原地等待，不下发新设定值</summary>
        public void Pause()
        {
            if (Status == ChannelStatus.Running)
                ChangeStatus(ChannelStatus.Paused, "已暂停");
        }

        /// <summary>从暂停恢复运行</summary>
        public void Resume()
        {
            if (Status == ChannelStatus.Paused)
                ChangeStatus(ChannelStatus.Running, "已恢复");
        }

        /// <summary>用户主动停止：取消后台任务、尝试通知驱动停止输出、标记为 Stopped</summary>
        public void Stop()
        {
            _cts?.Cancel();
            try { _driver.StopChannelAsync(LocalChannelIndex).Wait(500); } catch { }
            ChangeStatus(ChannelStatus.Stopped, "用户停止");
        }

        // ════════════════════════════════════════════════════════════
        //  主循环 —— 工步状态机：顺序推进 CurrentStepIndex，
        //  Loop 类型改写索引实现循环体重跑，End 类型终止方案，
        //  其余类型交给 ExecuteStepAsync 实际下发/采样/判定截止。
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
                    // ── DCIR 工步：内阻脉冲测量走独立流程，不复用常规 ExecuteStepAsync ──
                    // 因为它需要"静置稳压 → 施加脉冲 → 按固定时间点采样电压降"这套专用序列
                    // 来计算内阻，而不是按普通截止条件（时长/容量/电压）跑到底。
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

        /// <summary>
        /// 执行单个工步直到截止条件满足：先一次性下发设定值(<see cref="StepSetpoint"/>)，
        /// 然后按 SamplingIntervalMs 周期循环采样，每个周期都做容量/能量累计、
        /// 落一条 DataPoint 采样事件、跑一遍保护阈值检查，直到 IsStepFinished 命中
        /// 或触发保护为止。Waveform 类型工步是例外：设定值需要在这个采样循环内
        /// 逐 tick 按已过时间重新插值下发（见下方波形分支），而不是只在进入工步时下发一次。
        /// </summary>
        private async Task<ProtectionType> ExecuteStepAsync(TestStep step, CancellationToken token)
        {
            Capacity = 0;
            Energy = 0;
            StepElapsedSeconds = 0;
            _lastVoltage = double.NaN;
            _lastTempForRate = double.NaN;
            _currentWaveform = step.Type == StepType.Waveform
                ? ParseWaveformProfile(step.WaveformProfileJson)
                : null;

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

                // ── 工况仿真波形：按已过时间插值下发目标电流 ──
                if (_currentWaveform != null && _currentWaveform.Count > 0)
                {
                    var wfCurrent = InterpolateWaveformCurrent(_currentWaveform, StepElapsedSeconds);
                    var wfSetpoint = new StepSetpoint
                    {
                        Type = wfCurrent >= 0 ? StepType.CC_Charge : StepType.CC_Discharge,
                        Current = Math.Abs(wfCurrent)
                    };
                    try { await _driver.ApplyStepAsync(LocalChannelIndex, wfSetpoint, token); }
                    catch (OperationCanceledException) { break; }
                    catch { /* 下发失败交由通讯异常计数器统一处理 */ }
                }

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

                // ── BMS 采集（PACK 单体电压 / 多路温度）──
                BmsReading bms = null;
                if (Bms != null)
                {
                    try
                    {
                        bms = await Bms.ReadAsync(LocalChannelIndex, token);
                        _bmsConsecErrors = 0;
                        MaxCellVoltage = bms.MaxCellVoltage;
                        MinCellVoltage = bms.MinCellVoltage;
                        CellVoltageDelta = bms.CellVoltageDelta;
                        MaxPackTemperature = bms.MaxTemperature;
                        TempDelta = bms.TempDelta;

                        BmsSampled?.Invoke(this, new BmsSampleEventArgs
                        {
                            ChannelIndex = ChannelIndex,
                            Data = new BmsDataPoint
                            {
                                TestRecordId = CurrentRecord?.Id ?? 0,
                                ChannelIndex = ChannelIndex,
                                Timestamp = bms.Timestamp,
                                TotalElapsedSeconds = TotalElapsedSeconds,
                                CellVoltages = bms.CellVoltages,
                                Temperatures = bms.Temperatures,
                                MaxCellVoltage = Math.Round(bms.MaxCellVoltage, 4),
                                MinCellVoltage = Math.Round(bms.MinCellVoltage, 4),
                                CellVoltageDelta = Math.Round(bms.CellVoltageDelta, 4),
                                MaxCellIndex = bms.MaxCellIndex,
                                MinCellIndex = bms.MinCellIndex,
                                MaxTempPoint = Math.Round(bms.MaxTemperature, 2),
                                TempDelta = Math.Round(bms.TempDelta, 2),
                                BmsSoc = bms.Soc,
                                BmsSoh = bms.Soh,
                                FaultCode = bms.FaultCode
                            }
                        });
                    }
                    catch (OperationCanceledException) { break; }
                    catch
                    {
                        if (++_bmsConsecErrors >= MaxBmsErrors)
                            return ProtectionType.BmsCommunicationLost;
                    }
                }

                // ── 累计 ──
                var dAh = Math.Abs(m.Current) * dtSec / 3600.0;
                var dWh = Math.Abs(m.Current) * m.Voltage * dtSec / 3600.0;
                Capacity += dAh;
                Energy += dWh;

                // Pulse/Waveform 工步内电流方向会来回切换（充/放交替），不能按工步的
                // "名义类型"归类充放电量，只能按每次采样瞬间的实际电流符号分别累计。
                if (step.Type == StepType.Pulse || step.Type == StepType.Waveform)
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
                    Soc = EstimateSoc(),
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
                // ── PACK 单体 / 温差 / BMS 故障保护 ──
                if (bms != null)
                {
                    var cellProt = CheckCellProtection(step, bms);
                    if (cellProt != ProtectionType.None) return cellProt;
                }

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
                        // 标准恒压充电"尾流截止"：电压已基本到达目标值（留 5mV 余量防抖），
                        // 且电流已衰减到截止电流以下，说明电池接近满充，此时结束比死等
                        // DurationSeconds 超时更准确，也是行业通用的 CV 阶段终止判据。
                        var cvTarget = step.Voltage > 0 ? step.Voltage : step.CutoffVoltage;
                        if (step.CutoffCurrent > 0 && cvTarget > 0
                            && Voltage >= cvTarget - 0.005
                            && Math.Abs(Current) <= step.CutoffCurrent) return true;
                    }
                    break;
                case StepType.Waveform:
                    if (_currentWaveform != null && _currentWaveform.Count > 0
                        && StepElapsedSeconds >= _currentWaveform[_currentWaveform.Count - 1].TimeSeconds) return true;
                    break;
            }
            return CheckTrigger(step);
        }

        /// <summary>解析导入的时间-电流波形 JSON，按时间升序排列</summary>
        private static List<WaveformPoint> ParseWaveformProfile(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<WaveformPoint>();
            try
            {
                var list = JsonSerializer.Deserialize<List<WaveformPoint>>(json) ?? new List<WaveformPoint>();
                return list.OrderBy(p => p.TimeSeconds).ToList();
            }
            catch
            {
                return new List<WaveformPoint>();
            }
        }

        /// <summary>按已过时间线性插值波形电流，超出范围取端点值</summary>
        private static double InterpolateWaveformCurrent(List<WaveformPoint> points, double t)
        {
            if (points.Count == 1 || t <= points[0].TimeSeconds) return points[0].Current;
            var last = points[points.Count - 1];
            if (t >= last.TimeSeconds) return last.Current;

            for (int i = 1; i < points.Count; i++)
            {
                if (t <= points[i].TimeSeconds)
                {
                    var p0 = points[i - 1];
                    var p1 = points[i];
                    var span = p1.TimeSeconds - p0.TimeSeconds;
                    if (span <= 0) return p1.Current;
                    var ratio = (t - p0.TimeSeconds) / span;
                    return p0.Current + (p1.Current - p0.Current) * ratio;
                }
            }
            return last.Current;
        }

        /// <summary>
        /// 通用多阈值触发判断（电压/电流/容量/能量/温度/时间任一条件达标即命中），
        /// 对应工艺编辑里的"触发条件跳转"：命中后若配置了 JumpTargetIndex 则记录
        /// 跳转目标供 RunLoop 处理，否则仅表示"本工步应当结束"。
        /// </summary>
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

        /// <summary>
        /// 每次采样都跑一遍的软件级保护阈值检查。电压/电流的比较都额外加了一点余量
        /// （+0.05V / +0.1A）而不是严格按阈值触发，用来吸收传感器噪声导致的瞬时抖动，
        /// 避免刚好卡在阈值附近时被误判保护、频繁打断正常测试。
        /// </summary>
        private ProtectionType CheckProtection(TestStep step, DeviceMeasurement m)
        {
            // 反接（运行中也可能因接触不良发生）
            if (m.Voltage < -0.5) return ProtectionType.ReversePolarity;

            if (step.MaxVoltage > 0 && m.Voltage > step.MaxVoltage + 0.05)
                return ProtectionType.OverVoltage;
            // 欠压判断要求电流不接近 0：静置/未接负载时端电压本就可能低于阈值，
            // 只有在确实有电流通过（说明处于充放电中）时低电压才代表真实的欠压风险。
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

            // dT/dt 温升速率检测：即使还没到绝对温度上限，升温过快也可能是热失控前兆
            if (step.MaxTempRiseRate > 0 && !double.IsNaN(_lastTempForRate))
            {
                var dt = (m.Timestamp - _lastTempRateTime).TotalSeconds;
                if (dt > 0)
                {
                    var riseRatePerMin = (m.Temperature - _lastTempForRate) / dt * 60.0;
                    if (riseRatePerMin > step.MaxTempRiseRate)
                        return ProtectionType.RapidTempRise;
                }
            }
            _lastTempForRate = m.Temperature;
            _lastTempRateTime = m.Timestamp;

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

        /// <summary>PACK 单体过压/欠压、压差、温差、BMS 故障保护</summary>
        private ProtectionType CheckCellProtection(TestStep step, BmsReading bms)
        {
            if (bms.FaultCode != 0) return ProtectionType.BmsFault;

            if (bms.HasCells)
            {
                if (step.CellMaxVoltage > 0 && bms.MaxCellVoltage > step.CellMaxVoltage)
                    return ProtectionType.CellOverVoltage;
                if (step.CellMinVoltage > 0 && bms.MinCellVoltage < step.CellMinVoltage)
                    return ProtectionType.CellUnderVoltage;
                if (step.MaxCellVoltageDelta > 0 && bms.CellVoltageDelta > step.MaxCellVoltageDelta)
                    return ProtectionType.CellVoltageDeltaHigh;
            }
            if (bms.HasTemps && step.MaxTempDelta > 0 && bms.TempDelta > step.MaxTempDelta)
                return ProtectionType.TempDeltaHigh;

            return ProtectionType.None;
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
