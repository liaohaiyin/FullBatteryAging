using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// RS485 (串口) 驱动基类 - 各厂商串口驱动可继承此类
    /// 串口本身是半双工，所有读写都串行化在 _ioLock 内
    /// </summary>
    public abstract class SerialDriverBase : IDeviceDriver
    {
        protected SerialPort _port;
        protected readonly object _ioLock = new();

        protected readonly string _portName;
        protected readonly int _baudRate;
        protected readonly int _dataBits;
        protected readonly StopBits _stopBits;
        protected readonly Parity _parity;

        public abstract DriverType DriverType { get; }
        public bool IsConnected { get; protected set; }

        public event EventHandler<string> CommunicationError;

        protected SerialDriverBase(string portName, int baudRate,
            int dataBits = 8, int stopBits = 1, string parity = "None")
        {
            _portName = portName;
            _baudRate = baudRate;
            _dataBits = dataBits;
            _stopBits = stopBits switch
            {
                0 => StopBits.None,
                2 => StopBits.Two,
                3 => StopBits.OnePointFive,
                _ => StopBits.One
            };
            _parity = parity?.ToLowerInvariant() switch
            {
                "odd" => Parity.Odd,
                "even" => Parity.Even,
                "mark" => Parity.Mark,
                "space" => Parity.Space,
                _ => Parity.None
            };
        }

        public virtual Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try
            {
                lock (_ioLock)
                {
                    _port?.Close();
                    _port = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000,
                        Handshake = Handshake.None,
                        DtrEnable = true,
                        RtsEnable = true
                    };
                    _port.Open();
                }
                IsConnected = true;
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                RaiseError($"串口连接失败 {_portName}@{_baudRate} - {ex.Message}");
                IsConnected = false;
                return Task.FromResult(false);
            }
        }

        public virtual Task DisconnectAsync()
        {
            try
            {
                lock (_ioLock)
                {
                    if (_port != null && _port.IsOpen) _port.Close();
                    _port?.Dispose();
                    _port = null;
                }
            }
            catch { }
            IsConnected = false;
            return Task.CompletedTask;
        }

        public virtual Task<bool> PingAsync(CancellationToken token = default)
            => Task.FromResult(IsConnected && _port?.IsOpen == true);

        public abstract Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default);
        public abstract Task StopChannelAsync(int channelIndex, CancellationToken token = default);
        public abstract Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default);

        /// <summary>串行化的请求-响应（子类协议层调用）</summary>
        protected byte[] Transact(byte[] request, int expectedRespLen, int timeoutMs = 500)
        {
            lock (_ioLock)
            {
                if (_port == null || !_port.IsOpen)
                    throw new InvalidOperationException("串口未打开");

                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                _port.Write(request, 0, request.Length);

                var buf = new byte[expectedRespLen];
                var read = 0;
                var prevTo = _port.ReadTimeout;
                _port.ReadTimeout = timeoutMs;
                try
                {
                    while (read < expectedRespLen)
                    {
                        var n = _port.Read(buf, read, expectedRespLen - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
                finally { _port.ReadTimeout = prevTo; }
                if (read < expectedRespLen)
                    throw new TimeoutException($"串口读超时: 期望{expectedRespLen}字节, 实际{read}");
                return buf;
            }
        }

        protected void RaiseError(string msg) => CommunicationError?.Invoke(this, msg);

        public virtual void Dispose()
        {
            try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    /// <summary>新威 RS485 驱动占位</summary>
    public class NewareSerialDriver : SerialDriverBase
    {
        public override DriverType DriverType => DriverType.NeWare;
        public NewareSerialDriver(string portName, int baudRate, int dataBits, int stopBits, string parity)
            : base(portName, baudRate, dataBits, stopBits, parity) { }

        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default)
            => throw new NotImplementedException("NeWare 串口驱动待实现");
        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
    }

    /// <summary>蓝电 RS485 驱动占位</summary>
    public class LandSerialDriver : SerialDriverBase
    {
        public override DriverType DriverType => DriverType.Land;
        public LandSerialDriver(string portName, int baudRate, int dataBits, int stopBits, string parity)
            : base(portName, baudRate, dataBits, stopBits, parity) { }

        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default)
            => throw new NotImplementedException();
        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
    }

    /// <summary>鑫达能 RS485 驱动占位 - 通常 Modbus RTU</summary>
    public class XinDaNengSerialDriver : SerialDriverBase
    {
        public override DriverType DriverType => DriverType.XinDaNeng;
        public XinDaNengSerialDriver(string portName, int baudRate, int dataBits, int stopBits, string parity)
            : base(portName, baudRate, dataBits, stopBits, parity) { }

        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default)
            => throw new NotImplementedException();
        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
    }
}