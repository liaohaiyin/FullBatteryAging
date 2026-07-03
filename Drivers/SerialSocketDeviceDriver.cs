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
    /// 通用 RS232/RS485 串口充放电机驱动（ASCII 行命令-响应，复用 <see cref="SocketProtocolMap"/> 的命令模板/正则约定）。
    /// 与 <see cref="SocketDeviceDriver"/> 协议格式一致，仅链路层换成串口；RS485 多机挂载靠命令中的通道号({ch})区分。
    /// </summary>
    public class SerialSocketDeviceDriver : SerialDriverBase
    {
        private readonly SocketProtocolMap _map;
        private readonly Regex _readRegex;

        public override DriverType DriverType => DriverType.GenericSerial;

        public SerialSocketDeviceDriver(string portName, int baudRate, int dataBits, int stopBits, string parity,
            SocketProtocolMap map = null)
            : base(portName, baudRate, dataBits, stopBits, parity)
        {
            _map = map ?? new SocketProtocolMap();
            _readRegex = new Regex(_map.ReadResponsePattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public override Task ApplyStepAsync(int channelIndex, StepSetpoint sp, CancellationToken token = default)
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
                    mode = sp.PulseCurrent >= 0 ? _map.ModeCcCharge : _map.ModeCcDischarge;
                    i = Math.Abs(sp.PulseCurrent); break;
                default: mode = _map.ModeRest; break;
            }
            try { TransactLine(Format(_map.ApplyCmd, channelIndex, mode, i, v, p, r)); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 下发失败: {ex.Message}"); throw; }
            return Task.CompletedTask;
        }

        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default)
        {
            try { TransactLine(Format(_map.StopCmd, channelIndex, _map.ModeRest, 0, 0, 0, 0)); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 停止失败: {ex.Message}"); }
            return Task.CompletedTask;
        }

        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
        {
            string resp;
            try { resp = TransactLine(Format(_map.ReadCmd, channelIndex, "", 0, 0, 0, 0)); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 采样失败: {ex.Message}"); throw; }

            var m = _readRegex.Match(resp);
            if (!m.Success)
            {
                RaiseError($"CH{channelIndex} 响应解析失败: {resp}");
                throw new FormatException($"无法解析串口响应: {resp}");
            }
            double G(string g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return Task.FromResult(new DeviceMeasurement
            {
                Voltage = G("v") * _map.VoltageScale,
                Current = G("i") * _map.CurrentScale,
                Temperature = G("t") * _map.TemperatureScale,
                Timestamp = DateTime.Now
            });
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

        /// <summary>发送一行命令并读取一行响应（半双工，串行化在 SerialDriverBase._ioLock 内）</summary>
        private string TransactLine(string command)
        {
            lock (_ioLock)
            {
                if (_port == null || !_port.IsOpen)
                    throw new InvalidOperationException("串口未打开");

                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                var payload = Encoding.ASCII.GetBytes(command + _map.Terminator);
                _port.Write(payload, 0, payload.Length);
                return ReadLine();
            }
        }

        private string ReadLine()
        {
            var sb = new StringBuilder();
            var prevTimeout = _port.ReadTimeout;
            _port.ReadTimeout = _map.ReadTimeoutMs;
            try
            {
                while (true)
                {
                    int b;
                    try { b = _port.ReadByte(); }
                    catch (TimeoutException) { break; }
                    if (b < 0) break;                  // 连接关闭
                    char c = (char)b;
                    if (c == '\n' || c == '\r')
                    {
                        if (sb.Length > 0) break;      // 一行结束
                        continue;                       // 跳过前导/空行
                    }
                    sb.Append(c);
                }
            }
            finally { _port.ReadTimeout = prevTimeout; }
            return sb.ToString();
        }
    }
}
