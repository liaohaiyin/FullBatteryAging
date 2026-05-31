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
        private readonly List<Cabinet> _cabinets = new();

        public IReadOnlyDictionary<int, ChannelExecutor> Channels => _channels;
        public IReadOnlyList<Cabinet> Cabinets => _cabinets;
        public int ChannelCount => _channels.Count;

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
                _drivers[cab.Id] = driver;

                _ = ConnectAsync(cab.Id, driver);

                for (int local = 1; local <= cab.ChannelCount; local++)
                {
                    int global = cab.ChannelStartIndex + local - 1;
                    var executor = new ChannelExecutor(global, driver, cab.Id, local)
                    {
                        SamplingIntervalMs = sampleMs
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
                Id = "default",
                Name = "机柜1",
                CabinetIndex = 1,
                DriverType = DriverType.Simulator,
                ConnectionType = ConnectionType.Simulation,
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

        public ChannelExecutor GetChannel(int index)
            => _channels.TryGetValue(index, out var ch) ? ch : null;

        public IEnumerable<ChannelExecutor> GetAll() => _channels.Values;

        public IEnumerable<ChannelExecutor> GetByCabinet(string cabinetId)
            => _channels.Values.Where(c => c.CabinetId == cabinetId);

        /// <summary>多通道同步启动 - 用 Barrier 让通道在同一时刻开始</summary>
        public async Task SyncStartAsync(IEnumerable<(ChannelExecutor exec, TestRecipe recipe, TestRecord record)> jobs)
        {
            var list = jobs.ToList();
            if (list.Count == 0) return;

            var barrier = new Barrier(list.Count);
            foreach (var (exec, _, _) in list) exec.SyncBarrier = barrier;

            var tasks = list.Select(j => j.exec.StartAsync(j.recipe, j.record)).ToList();
            await Task.WhenAll(tasks.Select(t => Task.Run(() => { })));

            // 清除引用
            foreach (var (exec, _, _) in list) exec.SyncBarrier = null;
        }

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
            _drivers.Clear();
            _channels.Clear();
            _cabinets.Clear();
        }
    }
}
