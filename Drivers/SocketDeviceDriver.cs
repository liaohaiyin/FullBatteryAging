using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 通用 TCP Socket 协议映射（ASCII 行命令-响应）。
    /// 对接具体设备时，按其《通讯协议手册》调整命令模板 / 模式码 / 响应正则 / 缩放。
    /// 命令模板占位符: {ch} {mode} {i} {v} {p} {r}
    /// </summary>
    public class SocketProtocolMap
    {
        public string Terminator = "\r\n";       // 行结束符
        public int ChannelIdBase = 1;            // 命令中通道号起始值
        public int ReadTimeoutMs = 1000;

        // —— 命令模板 ——（默认示例，务必按设备协议改）
        public string ReadCmd = "RD {ch}";
        public string StopCmd = "STOP {ch}";
        public string ApplyCmd = "SET {ch} {mode} I={i} V={v} P={p} R={r}";

        // —— 模式码（按手册改）——
        public string ModeRest = "REST";
        public string ModeCcCharge = "CCC";
        public string ModeCcDischarge = "CCD";
        public string ModeCvCharge = "CVC";
        public string ModeCccvCharge = "CCCV";
        public string ModeCpCharge = "CPC";
        public string ModeCpDischarge = "CPD";
        public string ModeCrDischarge = "CRD";

        // —— 响应解析：命名组 v / i / t —— 默认匹配 "V=3.700,I=1.000,T=25.0"
        public string ReadResponsePattern =
            @"V=(?<v>-?\d+(\.\d+)?).*?I=(?<i>-?\d+(\.\d+)?).*?T=(?<t>-?\d+(\.\d+)?)";

        // 工程值缩放（原始值 × scale = 工程值）
        public double VoltageScale = 1.0;
        public double CurrentScale = 1.0;        // 注意电流符号约定：正充负放
        public double TemperatureScale = 1.0;
    }

    /// <summary>
    /// 通用 TCP Socket 充放电机驱动。
    /// 一个机柜一条 Socket，通道号写入命令（{ch}），半双工请求-响应。
    /// </summary>
    public class SocketDeviceDriver : TcpDriverBase
    {
        private readonly SocketProtocolMap _map;
        private readonly Regex _readRegex;
        private readonly SemaphoreSlim _ioGate = new(1, 1);   // 异步串行化收发

        public override DriverType DriverType => DriverType.GenericSocket;

        public SocketDeviceDriver(string host, int port, SocketProtocolMap map = null)
            : base(host, port)
        {
            _map = map ?? new SocketProtocolMap();
            _readRegex = new Regex(_map.ReadResponsePattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public override async Task ApplyStepAsync(int channelIndex, StepSetpoint sp, CancellationToken token = default)
        {
            string mode; double i = 0, v = 0, p = 0, r = 0;
            switch (sp.Type)
            {
                case StepType.CC_Charge: mode = _map.ModeCcCharge; i = Math.Abs(sp.Current); break;
                case StepType.CC_Discharge: mode = _map.ModeCcDischarge; i = Math.Abs(sp.Current); break;
                case StepType.CV_Charge: mode = _map.ModeCvCharge; v = sp.Voltage; i = Math.Abs(sp.Current); break;
                case StepType.CCCV_Charge: mode = _map.ModeCccvCharge; i = Math.Abs(sp.Current); v = sp.Voltage; break;
                case StepType.CP_Charge: mode = _map.ModeCpCharge; p = Math.Abs(sp.Power); break;
                case StepType.CP_Discharge: mode = _map.ModeCpDischarge; p = Math.Abs(sp.Power); break;
                case StepType.CR_Discharge: mode = _map.ModeCrDischarge; r = sp.Resistance; break;
                case StepType.Pulse:
                    // 脉冲由上层反复下发 CC，按当前电流符号选充/放
                    mode = sp.PulseCurrent >= 0 ? _map.ModeCcCharge : _map.ModeCcDischarge;
                    i = Math.Abs(sp.PulseCurrent); break;
                default: mode = _map.ModeRest; break;
            }
            await TransactAsync(Format(_map.ApplyCmd, channelIndex, mode, i, v, p, r), token);
        }

        public override async Task StopChannelAsync(int channelIndex, CancellationToken token = default)
            => await TransactAsync(Format(_map.StopCmd, channelIndex, _map.ModeRest, 0, 0, 0, 0), token);

        public override async Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
        {
            var resp = await TransactAsync(Format(_map.ReadCmd, channelIndex, "", 0, 0, 0, 0), token);
            var m = _readRegex.Match(resp);
            if (!m.Success)
            {
                RaiseError($"CH{channelIndex} 响应解析失败: {resp}");
                throw new FormatException($"无法解析 Socket 响应: {resp}");
            }
            double G(string g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return new DeviceMeasurement
            {
                Voltage = G("v") * _map.VoltageScale,
                Current = G("i") * _map.CurrentScale,
                Temperature = G("t") * _map.TemperatureScale,
                Timestamp = DateTime.Now
            };
        }

        // ── 工具 ──
        private string Format(string template, int ch, string mode, double i, double v, double p, double r)
            => template
                .Replace("{ch}", (_map.ChannelIdBase + ch - 1).ToString(CultureInfo.InvariantCulture))
                .Replace("{mode}", mode)
                .Replace("{i}", i.ToString("0.###", CultureInfo.InvariantCulture))
                .Replace("{v}", v.ToString("0.###", CultureInfo.InvariantCulture))
                .Replace("{p}", p.ToString("0.###", CultureInfo.InvariantCulture))
                .Replace("{r}", r.ToString("0.####", CultureInfo.InvariantCulture));

        /// <summary>发送一行命令并读取一行响应（半双工，串行化）</summary>
        private async Task<string> TransactAsync(string command, CancellationToken token)
        {
            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("Socket 未连接");

            var payload = Encoding.ASCII.GetBytes(command + _map.Terminator);
            await _ioGate.WaitAsync(token);
            try
            {
                await _stream.WriteAsync(payload, 0, payload.Length, token);
                await _stream.FlushAsync(token);
                return await ReadLineAsync(token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                RaiseError($"Socket 通讯失败: {ex.Message}");
                throw;
            }
            finally { _ioGate.Release(); }
        }

        /// <summary>
        /// 逐字节读取直到遇到 \r 或 \n —— 因为响应长度取决于数值位数，与 Modbus
        /// 那种"先读固定字节数的帧头再知道剩余长度"的二进制协议不同，ASCII
        /// 行协议只能靠终止符判断一行是否结束，读多了会连累下一条命令的响应。
        /// </summary>
        private async Task<string> ReadLineAsync(CancellationToken token)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(_map.ReadTimeoutMs);   // 读超时
            while (true)
            {
                int n = await _stream.ReadAsync(buf, 0, 1, cts.Token);
                if (n <= 0) break;                 // 连接关闭
                char c = (char)buf[0];
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0) break;      // 一行结束
                    continue;                       // 跳过前导/空行
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public override void Dispose()
        {
            base.Dispose();
            _ioGate.Dispose();
        }
    }
}