using System;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Drivers
{
    // ════════════════════════════════════════════════════════════════════
    //  环境仓（高低温箱/温湿度箱）联动驱动
    //  - 绝大多数温箱控制器(如宇电/欧陆/RKC)支持 Modbus，复用同一套主站
    //  - 接口与电池驱动解耦，便于将来接振动台/冷却系统
    // ════════════════════════════════════════════════════════════════════
    public interface IClimateChamber : IDisposable
    {
        bool IsConnected { get; }
        Task<bool> ConnectAsync(CancellationToken token = default);

        /// <summary>设定目标温度(℃)</summary>
        Task SetTemperatureAsync(double celsius, CancellationToken token = default);

        /// <summary>设定目标湿度(%RH)，不支持可忽略</summary>
        Task SetHumidityAsync(double percentRh, CancellationToken token = default);

        /// <summary>读取当前温度(℃)</summary>
        Task<double> ReadTemperatureAsync(CancellationToken token = default);

        /// <summary>启动/停止运行</summary>
        Task RunAsync(bool run, CancellationToken token = default);

        /// <summary>等待温度进入 ±tolerance 并稳定 holdSeconds 秒</summary>
        Task<bool> WaitForTemperatureAsync(double target, double tolerance, int holdSeconds,
            int timeoutSeconds, IProgress<double> progress = null, CancellationToken token = default);

        event EventHandler<string> CommunicationError;
    }

    /// <summary>温箱寄存器映射（按温箱控制器手册填写）</summary>
    public class ChamberRegisterMap
    {
        public ushort SetTempReg = 0x0000;   // 温度设定(SV)
        public ushort PvTempReg = 0x0001;    // 当前温度(PV)
        public ushort SetHumiReg = 0x0002;   // 湿度设定
        public ushort RunReg = 0x0010;       // 运行命令
        public double TempScale = 0.1;       // 0.1℃
        public ushort TempScaleInv = 10;
        public byte UnitId = 1;
        public ushort CmdRun = 1, CmdStop = 0;
    }

    /// <summary>Modbus 温箱实现（TCP 或 RTU 由构造决定，内部复用驱动里的主站）</summary>
    public class ModbusClimateChamber : IClimateChamber
    {
        // 复用 ModbusDeviceDriver 内的主站逻辑：这里通过组合一个最小驱动来收发寄存器。
        private readonly IDeviceDriver _link;     // 仅借用其连接生命周期（可独立连接）
        private readonly Func<byte, ushort, ushort, ushort[]> _read;
        private readonly Action<byte, ushort, ushort[]> _write;
        private readonly ChamberRegisterMap _m;

        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        /// <param name="read">读保持寄存器委托 (unit,addr,count)=>ushort[]</param>
        /// <param name="write">写寄存器委托 (unit,addr,values)</param>
        public ModbusClimateChamber(Func<byte, ushort, ushort, ushort[]> read,
            Action<byte, ushort, ushort[]> write, ChamberRegisterMap map = null)
        {
            _read = read; _write = write; _m = map ?? new ChamberRegisterMap();
            _link = null;
        }

        public Task<bool> ConnectAsync(CancellationToken token = default) { IsConnected = true; return Task.FromResult(true); }

        public Task SetTemperatureAsync(double c, CancellationToken token = default)
        {
            Safe(() => _write(_m.UnitId, _m.SetTempReg, new[] { (ushort)Math.Round(c * _m.TempScaleInv) }));
            return Task.CompletedTask;
        }

        public Task SetHumidityAsync(double rh, CancellationToken token = default)
        {
            Safe(() => _write(_m.UnitId, _m.SetHumiReg, new[] { (ushort)Math.Round(rh) }));
            return Task.CompletedTask;
        }

        public Task<double> ReadTemperatureAsync(CancellationToken token = default)
        {
            short raw = (short)_read(_m.UnitId, _m.PvTempReg, 1)[0];
            return Task.FromResult(raw * _m.TempScale);
        }

        public Task RunAsync(bool run, CancellationToken token = default)
        {
            Safe(() => _write(_m.UnitId, _m.RunReg, new[] { run ? _m.CmdRun : _m.CmdStop }));
            return Task.CompletedTask;
        }

        public async Task<bool> WaitForTemperatureAsync(double target, double tolerance, int holdSeconds,
            int timeoutSeconds, IProgress<double> progress = null, CancellationToken token = default)
        {
            var start = DateTime.Now; int inBand = 0;
            while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
            {
                token.ThrowIfCancellationRequested();
                double pv;
                try { pv = await ReadTemperatureAsync(token); }
                catch { await Task.Delay(2000, token); continue; }
                progress?.Report(pv);
                inBand = Math.Abs(pv - target) <= tolerance ? inBand + 1 : 0;
                if (inBand >= Math.Max(1, holdSeconds / 2)) return true; // 每 2s 采样
                await Task.Delay(2000, token);
            }
            return false;
        }

        private void Safe(Action a) { try { a(); IsConnected = true; } catch (Exception ex) { CommunicationError?.Invoke(this, ex.Message); } }
        public void Dispose() { _link?.Dispose(); }
    }
}
