using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// TCP 驱动基类 - 各厂商 TCP 驱动可继承此类
    /// 当前为骨架实现，留待对接具体协议（NeWare / Land / Chroma 等）
    /// </summary>
    public abstract class TcpDriverBase : IDeviceDriver
    {
        protected TcpClient _client;
        protected NetworkStream _stream;
        protected readonly string _host;
        protected readonly int _port;
        protected readonly object _ioLock = new();

        public abstract DriverType DriverType { get; }
        public bool IsConnected { get; protected set; }

        public event EventHandler<string> CommunicationError;

        protected TcpDriverBase(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public virtual async Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try
            {
                _client = new TcpClient();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await _client.ConnectAsync(_host, _port, linkedCts.Token);
                _stream = _client.GetStream();
                IsConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"TCP 连接失败 {_host}:{_port} - {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        public virtual Task DisconnectAsync()
        {
            try
            {
                _stream?.Dispose();
                _client?.Close();
            }
            catch { }
            IsConnected = false;
            return Task.CompletedTask;
        }

        public virtual async Task<bool> PingAsync(CancellationToken token = default)
        {
            if (!IsConnected || _client?.Connected != true) return false;
            await Task.CompletedTask;
            return true;
        }

        public abstract Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default);
        public abstract Task StopChannelAsync(int channelIndex, CancellationToken token = default);
        public abstract Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default);

        protected void RaiseError(string msg) => CommunicationError?.Invoke(this, msg);

        public virtual void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>新威驱动占位实现 - TODO 待对接 BTSDA 协议</summary>
    public class NewareDriver : TcpDriverBase
    {
        public override DriverType DriverType => DriverType.NeWare;
        public NewareDriver(string host, int port) : base(host, port) { }

        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default)
            => throw new NotImplementedException("NeWare 驱动待实现");

        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();

        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
            => throw new NotImplementedException();
    }

    /// <summary>蓝电驱动占位</summary>
    public class LandDriver : TcpDriverBase
    {
        public override DriverType DriverType => DriverType.Land;
        public LandDriver(string host, int port) : base(host, port) { }
        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default) => throw new NotImplementedException();
        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default) => throw new NotImplementedException();
        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default) => throw new NotImplementedException();
    }

    /// <summary>致茂驱动占位 - 通常用 SCPI</summary>
    public class XinDaNengDriver : TcpDriverBase
    {
        public override DriverType DriverType => DriverType.XinDaNeng;
        public XinDaNengDriver(string host, int port) : base(host, port) { }
        public override Task ApplyStepAsync(int channelIndex, StepSetpoint setpoint, CancellationToken token = default) => throw new NotImplementedException();
        public override Task StopChannelAsync(int channelIndex, CancellationToken token = default) => throw new NotImplementedException();
        public override Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default) => throw new NotImplementedException();
    }
}
