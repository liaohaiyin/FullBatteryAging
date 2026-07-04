using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers;

namespace BatteryAging.Communication
{
    /// <summary>
    /// 通道管理器 - 管理所有机柜和通道
    /// 每个机柜对应一个 IDeviceDriver
    /// </summary>
    public class ChannelManager
    {
        private readonly Dictionary<int, ChannelExecutor> _channels = new();
        private readonly Dictionary<string, IDeviceDriver> _drivers = new();
        private readonly Dictionary<string, IBmsDriver> _bmsDrivers = new();
        private readonly List<Cabinet> _cabinets = new();

        /// <summary>全部通道，键为全局通道号</summary>
        public IReadOnlyDictionary<int, ChannelExecutor> Channels => _channels;
        /// <summary>当前已启用并初始化的机柜列表</summary>
        public IReadOnlyList<Cabinet> Cabinets => _cabinets;
        /// <summary>已初始化的通道总数</summary>
        public int ChannelCount => _channels.Count;

        /// <summary>任一机柜/通道/BMS 驱动发生通讯异常时触发</summary>
        public event EventHandler<string> CommunicationError;

        /// <summary>用一组机柜初始化</summary>
        public void InitializeFromCabinets(IEnumerable<Cabinet> cabinets)
        {
            ClearAll();
            foreach (var cab in cabinets.Where(c => c.IsEnabled))
            {
                _cabinets.Add(cab);
                var sampleMs = cab.SamplingIntervalMs > 0 ? cab.SamplingIntervalMs : 1000;

                var driver = DriverFactory.Create(cab, sampleMs);
                driver.CommunicationError += (s, msg) =>
                {
                    cab.Status = CabinetStatus.CommunicationError;
                    CommunicationError?.Invoke(this, $"[{cab.Name}] {msg}");
                };
                IClimateChamber chamber = null;
                if (cab.HasChamber)
                {
                    // 复用一个 Modbus 主站（温箱通常独立 IP）
                    var chamberDrv = new ModbusDeviceDriver(cab.ChamberIp, cab.ChamberPort);
                    _ = chamberDrv.ConnectAsync();
                    chamber = new ModbusClimateChamber(
                        read: (u, a, c) => chamberDrv.ReadHoldingForChamber(u, a, c),   // 见 §5.1
                        write: (u, a, v) => chamberDrv.WriteRegistersForChamber(u, a, v),
                        map: new ChamberRegisterMap());
                    _ = chamber.ConnectAsync();
                }
                _drivers[cab.Id] = driver;

                _ = ConnectAsync(cab.Id, driver);

                // ── BMS 采集驱动（按机柜，单 PACK 多通道可共用）──
                IBmsDriver bms = null;
                if (cab.HasBms)
                {
                    bms = BmsDriverFactory.Create(cab);
                    bms.CommunicationError += (s, msg) =>
                        CommunicationError?.Invoke(this, $"[{cab.Name}] BMS {msg}");
                    _bmsDrivers[cab.Id] = bms;
                    _ = bms.ConnectAsync();
                }

                for (int local = 1; local <= cab.ChannelCount; local++)
                {
                    // 全局通道号 = 机柜起始号 + 机柜内本地号(1-based) - 1，
                    // 用于跨机柜时把所有通道映射到一个不重叠的连续编号空间，
                    // UI/数据库都按这个 global 索引区分通道，driver 通信仍用 local。
                    int global = cab.ChannelStartIndex + local - 1;
                    var executor = new ChannelExecutor(global, driver, cab.Id, local)
                    {
                        SamplingIntervalMs = sampleMs,
                        Chamber = chamber,
                        Bms = bms,
                        CellCount = cab.CellCount,
                        TempPointCount = cab.TempPointCount
                    };
                    _channels[global] = executor;
                }               
            }
        }

        /// <summary>简便方法：单机柜模拟器初始化（向后兼容）</summary>
        public void Initialize(int channelCount, int samplingIntervalMs = 1000)
        {
            var cab = new Cabinet
            {
                Name = "机柜1",
                CabinetIndex = 1,
                DriverType = DriverType.Simulator,
                ConnectionType = ConnectionType.Tcp,
                ChannelStartIndex = 1,
                ChannelCount = channelCount,
                SamplingIntervalMs = samplingIntervalMs,
                IsEnabled = true
            };
            InitializeFromCabinets(new[] { cab });
        }

        private async Task ConnectAsync(string cabId, IDeviceDriver driver)
        {
            try
            {
                var ok = await driver.ConnectAsync();
                var cab = _cabinets.FirstOrDefault(c => c.Id == cabId);
                if (cab != null)
                    cab.Status = ok ? CabinetStatus.Connected : CabinetStatus.Offline;
            }
            catch (Exception ex)
            {
                CommunicationError?.Invoke(this, ex.Message);
            }
        }

        /// <summary>按全局通道号取通道，不存在返回 null</summary>
        public ChannelExecutor GetChannel(int index)
            => _channels.TryGetValue(index, out var ch) ? ch : null;

        /// <summary>全部通道</summary>
        public IEnumerable<ChannelExecutor> GetAll() => _channels.Values;

        /// <summary>指定机柜下的所有通道</summary>
        public IEnumerable<ChannelExecutor> GetByCabinet(string cabinetId)
            => _channels.Values.Where(c => c.CabinetId == cabinetId);

        /// <summary>
        /// 多通道同步启动 —— 每个通道各自的 RunLoop 一启动就先在 Barrier.SignalAndWait 上
        /// 阻塞（见 ChannelExecutor.RunLoop 开头），等所有目标通道都到达这个点后 Barrier
        /// 才一次性放行，从而让它们在同一时刻真正开始下发第一个工步，而不是按线程调度
        /// 顺序先后启动、导致各通道计时基准略有偏差。
        /// </summary>
        public Task SyncStartAsync(IEnumerable<(ChannelExecutor exec, TestRecipe recipe, TestRecord record)> jobs)
        {
            var list = jobs.ToList();
            if (list.Count == 0) return Task.CompletedTask;

            var barrier = new Barrier(list.Count);
            foreach (var (exec, _, _) in list) exec.SyncBarrier = barrier;

            // 真正的运行任务（每个 StartAsync 返回的是其 RunLoop 任务）
            var runningTasks = list
                .Select(j => j.exec.StartAsync(j.recipe, j.record))
                .ToList();

            // 等所有通道运行结束后再清理屏障，避免过早置空导致 SignalAndWait 永远等不齐
            return Task.WhenAll(runningTasks).ContinueWith(_ =>
            {
                foreach (var (exec, _, _) in list)
                {
                    if (ReferenceEquals(exec.SyncBarrier, barrier))
                        exec.SyncBarrier = null;
                }
                barrier.Dispose();
            }, TaskScheduler.Default);
        }

        /// <summary>停止所有正在运行/暂停的通道</summary>
        public void StopAll()
        {
            foreach (var ch in _channels.Values)
            {
                if (ch.Status == ChannelStatus.Running || ch.Status == ChannelStatus.Paused)
                    ch.Stop();
            }
        }

        private void ClearAll()
        {
            StopAll();
            foreach (var d in _drivers.Values) { try { d.Dispose(); } catch { } }
            foreach (var b in _bmsDrivers.Values) { try { b.Dispose(); } catch { } }
            _drivers.Clear();
            _bmsDrivers.Clear();
            _channels.Clear();
            _cabinets.Clear();
        }
    }
}
