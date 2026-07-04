using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BatteryAging.Core.Enums;

namespace BatteryAging.Drivers.Can
{
    /// <summary>充放电指令 / 遥测 CAN 报文映射（按设备《CAN 通讯协议》填写）</summary>
    public class ZlgDeviceCanMap
    {
        public uint DeviceType = ZlgCan.ZCAN_USBCANFD_200U; // 卡型号
        public uint DeviceIndex = 0;     // 多卡时区分
        public uint CanIndex = 0;        // 卡内通道(0/1)
        public string BaudRate = "500000";

        // 指令报文：基准ID + 通道偏移，8字节: [0]=模式 [1-2]=设定电流(mA,小端) [3-4]=设定电压(mV,小端) [5]=运行命令
        public uint CmdBaseId = 0x200;
        public uint CmdIdStridePerChannel = 1;

        // 遥测报文：基准ID + 通道偏移，8字节: [0-1]=电压(mV) [2-3]=电流(mA,有符号) [4-5]=温度(0.1℃,有符号)
        public uint TelemetryBaseId = 0x300;
        public uint TelemetryIdStridePerChannel = 1;

        public double VoltageScale = 0.001;    // mV → V
        public double CurrentScale = 0.001;    // mA → A
        public double TemperatureScale = 0.1;  // 0.1℃ → ℃
        public double SetValueScaleInv = 1000; // 工程值 × 该值 → 报文原始值（V/A → mV/mA）

        // 模式码（设备相关，按手册改）
        public byte ModeRest = 0;
        public byte ModeCcCharge = 1;
        public byte ModeCcDischarge = 2;
        public byte ModeCvCharge = 3;
        public byte ModeCccvCharge = 4;
        public byte ModeCpCharge = 5;
        public byte ModeCpDischarge = 6;
        public byte ModeCrDischarge = 7;

        public byte CmdRun = 1;
        public byte CmdStop = 0;
    }

    /// <summary>
    /// ZLG CAN 主设备驱动（充放电指令下发 + 遥测采集），与 <see cref="ZlgBmsDriver"/> 共用同一张 ZLG SDK 封装。
    /// 真实部署前务必按设备 CAN 协议核对帧 ID、字节布局与模式码。
    /// </summary>
    public class ZlgDeviceDriver : IDeviceDriver
    {
        private readonly ZlgDeviceCanMap _map;
        private IntPtr _device = IntPtr.Zero;
        private IntPtr _channel = IntPtr.Zero;
        private CancellationTokenSource _cts;
        private Task _rxTask;
        private readonly object _txLock = new();
        private readonly ConcurrentDictionary<int, DeviceMeasurement> _latest = new();

        public DriverType DriverType => DriverType.Can;
        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        public ZlgDeviceDriver(ZlgDeviceCanMap map = null)
        {
            var bits = IntPtr.Size == 8 ? "x64" : "x86";
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZLG", bits);
            Environment.CurrentDirectory = path;

            _map = map ?? new ZlgDeviceCanMap();
        }

        public Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try
            {
                _device = ZlgCan.ZCAN_OpenDevice(_map.DeviceType, _map.DeviceIndex, 0);
                if (_device == IntPtr.Zero || (uint)_device == ZlgCan.INVALID_DEVICE_HANDLE)
                    throw new Exception("打开 CAN 设备失败");

                ZlgCan.ZCAN_SetValue(_device,
                    $"{_map.DeviceIndex}/{_map.CanIndex}/baud_rate", _map.BaudRate);

                var cfg = new ZlgCan.ZCAN_CHANNEL_INIT_CONFIG
                {
                    can_type = 0,
                    acc_code = 0,
                    acc_mask = 0xFFFFFFFF,
                    filter = 0,
                    mode = 0
                };
                _channel = ZlgCan.ZCAN_InitCAN(_device, _map.CanIndex, ref cfg);
                if (_channel == IntPtr.Zero)
                    throw new Exception("初始化 CAN 通道失败");

                if (ZlgCan.ZCAN_StartCAN(_channel) != ZlgCan.STATUS_OK)
                    throw new Exception("启动 CAN 通道失败");

                IsConnected = true;
                _cts = new CancellationTokenSource();
                _rxTask = Task.Run(() => ReceiveLoop(_cts.Token));
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                RaiseError($"CAN连接失败: {ex.Message}");
                IsConnected = false;
                return Task.FromResult(false);
            }
        }

        public Task DisconnectAsync()
        {
            try { _cts?.Cancel(); _rxTask?.Wait(500); } catch { }
            try { if (_channel != IntPtr.Zero) ZlgCan.ZCAN_ResetCAN(_channel); } catch { }
            try { if (_device != IntPtr.Zero) ZlgCan.ZCAN_CloseDevice(_device); } catch { }
            _channel = IntPtr.Zero; _device = IntPtr.Zero;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync(CancellationToken token = default) => Task.FromResult(IsConnected);

        public Task ApplyStepAsync(int channelIndex, StepSetpoint sp, CancellationToken token = default)
        {
            byte mode; double setI = 0, setV = 0;
            switch (sp.Type)
            {
                case StepType.CC_Charge: mode = _map.ModeCcCharge; setI = Math.Abs(sp.Current); break;
                case StepType.CC_Discharge: mode = _map.ModeCcDischarge; setI = Math.Abs(sp.Current); break;
                case StepType.CV_Charge: mode = _map.ModeCvCharge; setV = sp.Voltage; setI = Math.Abs(sp.Current); break;
                case StepType.CCCV_Charge: mode = _map.ModeCccvCharge; setI = Math.Abs(sp.Current); setV = sp.Voltage; break;
                case StepType.CP_Charge: mode = _map.ModeCpCharge; break;
                case StepType.CP_Discharge: mode = _map.ModeCpDischarge; break;
                case StepType.CR_Discharge: mode = _map.ModeCrDischarge; break;
                case StepType.Pulse:
                    mode = sp.PulseCurrent >= 0 ? _map.ModeCcCharge : _map.ModeCcDischarge;
                    setI = Math.Abs(sp.PulseCurrent); break;
                default: mode = _map.ModeRest; break;
            }

            try { Transmit(CmdId(channelIndex), BuildCmdFrame(mode, setI, setV, _map.CmdRun)); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 下发失败: {ex.Message}"); throw; }
            return Task.CompletedTask;
        }

        public Task StopChannelAsync(int channelIndex, CancellationToken token = default)
        {
            try { Transmit(CmdId(channelIndex), BuildCmdFrame(_map.ModeRest, 0, 0, _map.CmdStop)); }
            catch (Exception ex) { RaiseError($"CH{channelIndex} 停止失败: {ex.Message}"); }
            return Task.CompletedTask;
        }

        /// <summary>
        /// CAN 是总线广播式协议，遥测数据由设备主动周期发送、后台 ReceiveLoop 持续收帧缓存，
        /// 这里不像 Modbus/串口那样主动发起"读请求"，而是直接取最近一次缓存的帧。
        /// 超过 3 秒没收到新遥测帧就视为该通道失联（设备离线/总线故障/ID 配置错误）。
        /// </summary>
        public Task<DeviceMeasurement> ReadAsync(int channelIndex, CancellationToken token = default)
        {
            if (_latest.TryGetValue(channelIndex, out var m) && (DateTime.Now - m.Timestamp).TotalSeconds <= 3)
                return Task.FromResult(m);

            var msg = $"CH{channelIndex} CAN 数据超时（>3s 未更新）";
            RaiseError(msg);
            throw new TimeoutException(msg);
        }

        // ── 指令编码 / 发送 ──
        private byte[] BuildCmdFrame(byte mode, double engCurrent, double engVoltage, byte runCmd)
        {
            var data = new byte[8];
            short rawI = (short)Math.Round(engCurrent * _map.SetValueScaleInv);
            short rawV = (short)Math.Round(engVoltage * _map.SetValueScaleInv);
            data[0] = mode;
            data[1] = (byte)(rawI & 0xFF); data[2] = (byte)((rawI >> 8) & 0xFF);
            data[3] = (byte)(rawV & 0xFF); data[4] = (byte)((rawV >> 8) & 0xFF);
            data[5] = runCmd;
            return data;
        }

        private void Transmit(uint canId, byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("CAN 未连接");
            var frame = new ZlgCan.ZCAN_CAN_FRAME { can_id = canId, can_dlc = (byte)data.Length, data = data };
            var txData = new[] { new ZlgCan.ZCAN_Transmit_Data { frame = frame, transmit_type = 0 } };
            lock (_txLock)
            {
                if (ZlgCan.ZCAN_Transmit(_channel, txData, 1) != 1)
                    throw new Exception("CAN 发送失败");
            }
        }

        private uint CmdId(int channelIndex) => _map.CmdBaseId + (uint)(channelIndex - 1) * _map.CmdIdStridePerChannel;

        // ── 后台收帧：解析遥测报文，按通道缓存最新一帧 ──
        private void ReceiveLoop(CancellationToken token)
        {
            var buf = new ZlgCan.ZCAN_Receive_Data[100];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    uint num = ZlgCan.ZCAN_GetReceiveNum(_channel, 0);
                    if (num == 0) { Thread.Sleep(5); continue; }
                    uint got = ZlgCan.ZCAN_Receive(_channel, buf, Math.Min(num, (uint)buf.Length), 50);
                    for (int i = 0; i < got; i++)
                        ParseTelemetryFrame(buf[i].frame);
                }
                catch (Exception ex)
                {
                    CommunicationError?.Invoke(this, $"CAN收帧异常: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        private void ParseTelemetryFrame(ZlgCan.ZCAN_CAN_FRAME f)
        {
            // & 0x1FFFFFFF 去掉扩展帧标志位，只保留 29 位的实际 CAN ID
            uint id = f.can_id & 0x1FFFFFFF;
            if (id < _map.TelemetryBaseId || f.data == null || f.data.Length < 6) return;

            // 反推 CmdId() 的编码方式：ID = 基准ID + 通道号(0-based) * 跨度，
            // 除不尽说明这帧不属于本机映射范围的通道遥测，直接丢弃。
            uint stride = Math.Max(1u, _map.TelemetryIdStridePerChannel);
            uint offset = (id - _map.TelemetryBaseId) / stride;
            if ((id - _map.TelemetryBaseId) % stride != 0) return;

            int channelIndex = (int)offset + 1;
            var d = f.data;
            ushort rawV = (ushort)(d[0] | (d[1] << 8));
            short rawI = (short)(d[2] | (d[3] << 8));
            short rawT = (short)(d[4] | (d[5] << 8));

            _latest[channelIndex] = new DeviceMeasurement
            {
                Voltage = rawV * _map.VoltageScale,
                Current = rawI * _map.CurrentScale,
                Temperature = rawT * _map.TemperatureScale,
                Timestamp = DateTime.Now
            };
        }

        private void RaiseError(string msg) => CommunicationError?.Invoke(this, msg);
        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }
}
