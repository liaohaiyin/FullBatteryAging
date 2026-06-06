using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers
{
    // ════════════════════════════════════════════════════════════════════
    //  Modbus 充放电机驱动（公开协议，对应"鑫达能/通用 Modbus 设备"）
    //  - 支持 RTU(串口) 与 TCP 两种链路
    //  - 寄存器地址、缩放系数、模式码全部可配置（从设备说明书填入）
    //  - 一个通道 = 一个 Modbus 从站地址（unit id），可加每通道寄存器偏移
    // ════════════════════════════════════════════════════════════════════

    /// <summary>设备寄存器映射表 —— 请按你的设备《Modbus 协议手册》填写</summary>
    public class ModbusRegisterMap
    {
        // —— 读取寄存器（保持寄存器, FC03）地址（协议地址, 0-based）——
        public ushort VoltageReg = 0x0000;     // 电压
        public ushort CurrentReg = 0x0002;     // 电流（有符号: 正充负放, 视设备而定）
        public ushort TemperatureReg = 0x0004; // 温度
        public bool VoltageIs32Bit = true;     // 多数设备用 2 个寄存器存 32 位
        public bool CurrentIs32Bit = true;
        public bool TemperatureIs32Bit = false;
        public bool CurrentSigned = true;      // 电流是否有符号
        public bool BigEndianWord = true;      // 32 位时高字在前(ABCD) 还是低字在前(CDAB)

        // —— 缩放：寄存器原始值 × scale = 工程值 ——
        public double VoltageScale = 0.001;    // mV → V
        public double CurrentScale = 0.001;    // mA → A
        public double TemperatureScale = 0.1;  // 0.1℃ → ℃

        // —— 写入寄存器（FC16 写多个）——
        public ushort ModeReg = 0x0100;        // 工作模式码
        public ushort SetCurrentReg = 0x0101;  // 设定电流
        public ushort SetVoltageReg = 0x0102;  // 设定电压
        public ushort SetPowerReg = 0x0103;    // 设定功率
        public ushort SetResistanceReg = 0x0104;// 设定电阻
        public ushort RunCmdReg = 0x0110;       // 启停命令寄存器
        public ushort SetValueScaleInv = 1000;  // 工程值 × 该值 → 寄存器（V/A→mV/mA）

        // —— 模式码（设备相关，按手册改）——
        public ushort ModeRest = 0;
        public ushort ModeCcCharge = 1;
        public ushort ModeCcDischarge = 2;
        public ushort ModeCvCharge = 3;
        public ushort ModeCccvCharge = 4;
        public ushort ModeCpCharge = 5;
        public ushort ModeCpDischarge = 6;
        public ushort ModeCrDischarge = 7;

        public ushort CmdRun = 1;
        public ushort CmdStop = 0;

        // —— 通道 → 从站地址映射 ——
        public byte UnitIdBase = 1;             // 通道1 对应的从站地址
        public ushort RegisterStridePerChannel = 0; // 若多通道共用一个从站、按地址块分布则填偏移; =0 表示按从站地址区分
    }

    /// <summary>Modbus 主站抽象（RTU/TCP 各自实现）</summary>
    internal interface IModbusMaster : IDisposable
    {
        bool Connect();
        ushort[] ReadHolding(byte unit, ushort addr, ushort count);
        void WriteRegisters(byte unit, ushort addr, ushort[] values);
        bool IsOpen { get; }
    }

    public class ModbusDeviceDriver : IDeviceDriver
    {
        private readonly IModbusMaster _master;
        private readonly ModbusRegisterMap _map;
        private readonly object _lock = new();

        public DriverType DriverType => DriverType.Modbus;
        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        /// <summary>TCP 构造</summary>
        public ModbusDeviceDriver(string host, int port, ModbusRegisterMap map = null)
        {
            _map = map ?? new ModbusRegisterMap();
            _master = new ModbusTcpMaster(host, port);
        }

        /// <summary>RTU(串口) 构造</summary>
        public ModbusDeviceDriver(string portName, int baud, int dataBits, int stopBits,
            string parity, ModbusRegisterMap map = null)
        {
            _map = map ?? new ModbusRegisterMap();
            _master = new ModbusRtuMaster(portName, baud, dataBits, stopBits, parity);
        }

        public Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try { IsConnected = _master.Connect(); }
            catch (Exception ex) { RaiseError($"连接失败: {ex.Message}"); IsConnected = false; }
            return Task.FromResult(IsConnected);
        }

        public Task DisconnectAsync() { try { _master.Dispose(); } catch { } IsConnected = false; return Task.CompletedTask; }
        public Task<bool> PingAsync(CancellationToken token = default) => Task.FromResult(IsConnected && _master.IsOpen);

        public Task ApplyStepAsync(int channelIndex, StepSetpoint sp, CancellationToken token = default)
        {
            var (unit, off) = Resolve(channelIndex);
            ushort mode; double setI = 0, setV = 0, setP = 0, setR = 0;
            switch (sp.Type)
            {
                case StepType.CC_Charge: mode = _map.ModeCcCharge; setI = Math.Abs(sp.Current); break;
                case StepType.CC_Discharge: mode = _map.ModeCcDischarge; setI = Math.Abs(sp.Current); break;
                case StepType.CV_Charge: mode = _map.ModeCvCharge; setV = sp.Voltage; setI = Math.Abs(sp.Current); break;
                case StepType.CCCV_Charge: mode = _map.ModeCccvCharge; setI = Math.Abs(sp.Current); setV = sp.Voltage; break;
                case StepType.CP_Charge: mode = _map.ModeCpCharge; setP = Math.Abs(sp.Power); break;
                case StepType.CP_Discharge: mode = _map.ModeCpDischarge; setP = Math.Abs(sp.Power); break;
                case StepType.CR_Discharge: mode = _map.ModeCrDischarge; setR = sp.Resistance; break;
                case StepType.Pulse:
                    // 脉冲由上层 ChannelExecutor 切换电流时反复调用 ApplyStep，这里按当前电流符号下发 CC
                    mode = sp.PulseCurrent >= 0 ? _map.ModeCcCharge : _map.ModeCcDischarge;
                    setI = Math.Abs(sp.PulseCurrent); break;
                default: mode = _map.ModeRest; break;
            }
            try
            {
                lock (_lock)
                {
                    // 下发模式 + 设定值（一次写多个寄存器，地址需连续；若不连续则分多次写）
                    WriteReg(unit, (ushort)(_map.ModeReg + off), mode);
                    if (setI != 0) WriteReg(unit, (ushort)(_map.SetCurrentReg + off), Scale(setI));
                    if (setV != 0) WriteReg(unit, (ushort)(_map.SetVoltageReg + off), Scale(setV));
                    if (setP != 0) WriteReg(unit, (ushort)(_map.SetPowerReg + off), Scale(setP));
                    if (setR != 0) WriteReg(unit, (ushort)(_map.SetResistanceReg + off), Scale(setR));
                    WriteReg(unit, (ushort)(_map.RunCmdReg + off), _map.CmdRun);
                }
            }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 下发失败: {ex.Message}"); throw; }
            return Task.CompletedTask;
        }

        public Task StopChannelAsync(int channelIndex, CancellationToken token = default)
        {
            var (unit, off) = Resolve(channelIndex);
            try { lock (_lock) WriteReg(unit, (ushort)(_map.RunCmdReg + off), _map.CmdStop); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 停止失败: {ex.Message}"); }
            return Task.CompletedTask;
        }

        public Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
        {
            var (unit, off) = Resolve(channelIndex);
            try
            {
                double v, i, t;
                lock (_lock)
                {
                    v = ReadVal(unit, (ushort)(_map.VoltageReg + off), _map.VoltageIs32Bit, false, _map.VoltageScale);
                    i = ReadVal(unit, (ushort)(_map.CurrentReg + off), _map.CurrentIs32Bit, _map.CurrentSigned, _map.CurrentScale);
                    t = ReadVal(unit, (ushort)(_map.TemperatureReg + off), _map.TemperatureIs32Bit, true, _map.TemperatureScale);
                }
                return Task.FromResult(new DeviceMeasurement { Voltage = v, Current = i, Temperature = t, Timestamp = DateTime.Now });
            }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 采样失败: {ex.Message}"); throw; }
        }

        // —— 内部工具 ——
        private (byte unit, ushort off) Resolve(int channelIndex)
        {
            if (_map.RegisterStridePerChannel > 0)
                return (_map.UnitIdBase, (ushort)((channelIndex - 1) * _map.RegisterStridePerChannel));
            return ((byte)(_map.UnitIdBase + channelIndex - 1), 0);
        }

        private void WriteReg(byte unit, ushort addr, ushort value) => _master.WriteRegisters(unit, addr, new[] { value });

        private ushort Scale(double engValue) => (ushort)Math.Round(engValue * _map.SetValueScaleInv);

        private double ReadVal(byte unit, ushort addr, bool is32, bool signed, double scale)
        {
            var regs = _master.ReadHolding(unit, addr, (ushort)(is32 ? 2 : 1));
            long raw;
            if (is32)
            {
                uint u = _map.BigEndianWord
                    ? ((uint)regs[0] << 16) | regs[1]
                    : ((uint)regs[1] << 16) | regs[0];
                raw = signed ? (int)u : u;
            }
            else raw = signed ? (short)regs[0] : regs[0];
            return raw * scale;
        }
        public ushort[] ReadHoldingForChamber(byte unit, ushort addr, ushort count) { lock (_lock) return _master.ReadHolding(unit, addr, count); }
        public void WriteRegistersForChamber(byte unit, ushort addr, ushort[] values) { lock (_lock) _master.WriteRegisters(unit, addr, values); }
        private void RaiseError(string msg) => CommunicationError?.Invoke(this, msg);
        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }

    // ──────────────────────── Modbus RTU 主站 ────────────────────────
    internal sealed class ModbusRtuMaster : IModbusMaster
    {
        private SerialPort _port;
        private readonly string _name; private readonly int _baud, _dataBits;
        private readonly StopBits _stop; private readonly Parity _parity;

        public ModbusRtuMaster(string name, int baud, int dataBits, int stopBits, string parity)
        {
            _name = name; _baud = baud; _dataBits = dataBits;
            _stop = stopBits switch { 0 => StopBits.None, 2 => StopBits.Two, _ => StopBits.One };
            _parity = parity?.ToLowerInvariant() switch { "odd" => Parity.Odd, "even" => Parity.Even, _ => Parity.None };
        }

        public bool IsOpen => _port?.IsOpen == true;

        public bool Connect()
        {
            _port?.Dispose();
            _port = new SerialPort(_name, _baud, _parity, _dataBits, _stop) { ReadTimeout = 800, WriteTimeout = 800 };
            _port.Open();
            return _port.IsOpen;
        }

        public ushort[] ReadHolding(byte unit, ushort addr, ushort count)
        {
            // 请求: unit, 0x03, addrHi, addrLo, cntHi, cntLo, CRClo, CRChi
            var req = new byte[] { unit, 0x03, (byte)(addr >> 8), (byte)addr, (byte)(count >> 8), (byte)count };
            var frame = AppendCrc(req);
            var resp = Transact(frame, 5 + count * 2); // unit+func+bytecount + data + crc(2)
            if (resp[1] != 0x03) throw new Exception($"Modbus异常码 0x{resp[2]:X2}");
            int byteCount = resp[2];
            var regs = new ushort[byteCount / 2];
            for (int k = 0; k < regs.Length; k++) regs[k] = (ushort)((resp[3 + k * 2] << 8) | resp[4 + k * 2]);
            return regs;
        }

        public void WriteRegisters(byte unit, ushort addr, ushort[] values)
        {
            // FC16: unit,0x10,addrHi,addrLo,cntHi,cntLo,byteCount, data..., CRC
            var body = new List<byte>
            { unit, 0x10, (byte)(addr >> 8), (byte)addr, (byte)(values.Length >> 8), (byte)values.Length, (byte)(values.Length * 2) };
            foreach (var v in values) { body.Add((byte)(v >> 8)); body.Add((byte)v); }
            var frame = AppendCrc(body.ToArray());
            Transact(frame, 8); // echo: unit,func,addr(2),cnt(2),crc(2)
        }

        private byte[] Transact(byte[] frame, int expectedLen)
        {
            _port.DiscardInBuffer(); _port.DiscardOutBuffer();
            _port.Write(frame, 0, frame.Length);
            var buf = new byte[expectedLen]; int read = 0; var sw = DateTime.Now;
            while (read < expectedLen)
            {
                if ((DateTime.Now - sw).TotalMilliseconds > 1000) break;
                try { int n = _port.Read(buf, read, expectedLen - read); if (n > 0) read += n; }
                catch (TimeoutException) { break; }
            }
            if (read < 3) throw new TimeoutException("Modbus RTU 读超时");
            return buf;
        }

        private static byte[] AppendCrc(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            var outp = new byte[data.Length + 2];
            Array.Copy(data, outp, data.Length);
            outp[data.Length] = (byte)crc;            // low
            outp[data.Length + 1] = (byte)(crc >> 8); // high
            return outp;
        }

        public void Dispose() { try { _port?.Close(); _port?.Dispose(); } catch { } }
    }

    // ──────────────────────── Modbus TCP 主站 ────────────────────────
    internal sealed class ModbusTcpMaster : IModbusMaster
    {
        private readonly string _host; private readonly int _port;
        private TcpClient _client; private NetworkStream _stream; private ushort _tx;

        public ModbusTcpMaster(string host, int port) { _host = host; _port = port; }
        public bool IsOpen => _client?.Connected == true;

        public bool Connect()
        {
            _client = new TcpClient { ReceiveTimeout = 1000, SendTimeout = 1000 };
            _client.Connect(_host, _port);
            _stream = _client.GetStream();
            return _client.Connected;
        }

        public ushort[] ReadHolding(byte unit, ushort addr, ushort count)
        {
            var pdu = new byte[] { 0x03, (byte)(addr >> 8), (byte)addr, (byte)(count >> 8), (byte)count };
            var resp = Transact(unit, pdu);
            if (resp[0] != 0x03) throw new Exception($"Modbus异常码 0x{resp[1]:X2}");
            int byteCount = resp[1];
            var regs = new ushort[byteCount / 2];
            for (int k = 0; k < regs.Length; k++) regs[k] = (ushort)((resp[2 + k * 2] << 8) | resp[3 + k * 2]);
            return regs;
        }

        public void WriteRegisters(byte unit, ushort addr, ushort[] values)
        {
            var body = new List<byte>
            { 0x10, (byte)(addr >> 8), (byte)addr, (byte)(values.Length >> 8), (byte)values.Length, (byte)(values.Length * 2) };
            foreach (var v in values) { body.Add((byte)(v >> 8)); body.Add((byte)v); }
            Transact(unit, body.ToArray());
        }

        private byte[] Transact(byte unit, byte[] pdu)
        {
            _tx++;
            int len = pdu.Length + 1; // unit + pdu
            var mbap = new byte[] { (byte)(_tx >> 8), (byte)_tx, 0, 0, (byte)(len >> 8), (byte)len, unit };
            var frame = new byte[mbap.Length + pdu.Length];
            Array.Copy(mbap, frame, mbap.Length);
            Array.Copy(pdu, 0, frame, mbap.Length, pdu.Length);
            _stream.Write(frame, 0, frame.Length);

            var head = ReadExact(7);
            int respLen = (head[4] << 8) | head[5]; // includes unit
            var rest = ReadExact(respLen);
            // rest[0]=unit, rest[1..]=pdu
            var pduResp = new byte[respLen - 1];
            Array.Copy(rest, 1, pduResp, 0, pduResp.Length);
            return pduResp;
        }

        private byte[] ReadExact(int n)
        {
            var buf = new byte[n]; int read = 0;
            while (read < n) { int r = _stream.Read(buf, read, n - read); if (r <= 0) throw new Exception("TCP连接中断"); read += r; }
            return buf;
        }

        public void Dispose() { try { _stream?.Dispose(); _client?.Close(); } catch { } }
    }
}
