using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Drivers
{
    /// <summary>BMS 模拟器：围绕一个缓慢漂移的基准电压散布各单体，偶发轻微离群以体现压差。</summary>
    public class SimulatorBmsDriver : IBmsDriver
    {
        private readonly int _cellCount;
        private readonly int _tempCount;
        private readonly Random _rng = new(Guid.NewGuid().GetHashCode());
        private readonly ConcurrentDictionary<int, double> _base = new();   // 每 PACK 基准电压

        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        public SimulatorBmsDriver(int cellCount, int tempCount)
        {
            _cellCount = Math.Max(1, cellCount);
            _tempCount = Math.Max(1, tempCount);
        }

        public Task<bool> ConnectAsync(CancellationToken token = default) { IsConnected = true; return Task.FromResult(true); }
        public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }

        public Task<BmsReading> ReadAsync(int packIndex, CancellationToken token = default)
        {
            var vBase = _base.AddOrUpdate(packIndex, 3.70,
                (_, v) => Math.Clamp(v + (_rng.NextDouble() - 0.5) * 0.004, 3.0, 4.2)); // 随机游走

            var cells = new double[_cellCount];
            for (int i = 0; i < _cellCount; i++)
                cells[i] = Math.Round(vBase + (_rng.NextDouble() - 0.5) * 0.012, 4);    // ±6mV 离散
            cells[_rng.Next(_cellCount)] += 0.008;                                       // 偶发离群

            var temps = new double[_tempCount];
            for (int i = 0; i < _tempCount; i++)
                temps[i] = Math.Round(25 + (_rng.NextDouble() - 0.5) * 3, 2);

            double soc = Math.Clamp((vBase - 3.0) / (4.2 - 3.0) * 100, 0, 100);
            return Task.FromResult(new BmsReading
            {
                CellVoltages = cells,
                Temperatures = temps,
                PackVoltage = Math.Round(vBase * _cellCount, 2),
                Soc = Math.Round(soc, 1),
                Soh = 99.5,
                FaultCode = 0,
                Timestamp = DateTime.Now
            });
        }

        public void Dispose() => IsConnected = false;
    }
}