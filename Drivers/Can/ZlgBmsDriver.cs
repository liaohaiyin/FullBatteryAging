using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Drivers.Can
{
    /// <summary>BMS CAN 报文映射（按 BMS《CAN 通讯协议》填写）</summary>
    public class ZlgBmsCanMap
    {
        public uint DeviceType = ZlgCan.ZCAN_USBCANFD_200U; // 你的卡型号
        public uint DeviceIndex = 0;     // 多卡时区分
        public uint CanIndex = 0;        // 卡内通道(0/1)
        public string BaudRate = "500000"; // 500k，按 BMS 改
        public int CellCount = 16;
        public int TempCount = 4;

        // 单体电压报文：基准ID，连续若干帧，每帧含 4 个 uint16(mV)
        public uint CellVoltBaseId = 0x180;
        public int CellsPerFrame = 4;
        public double CellVoltScale = 0.001;   // mV→V

        // 温度报文：基准ID，每帧含 8 个 int8 或 4 个 int16，按协议改
        public uint TempBaseId = 0x190;
        public int TempsPerFrame = 4;
        public double TempScale = 0.1;

        // 状态报文：SOC/SOH/故障
        public uint StatusId = 0x1A0;
    }

    /// <summary>
    /// ZLG CAN BMS 驱动：后台线程持续收帧并拼装成最新一帧 BmsReading。
    /// 真实部署前务必按 BMS 协议核对 CAN ID 与字节解析。
    /// </summary>
    public class ZlgBmsDriver : IBmsDriver
    {
        private readonly ZlgBmsCanMap _map;
        private IntPtr _device = IntPtr.Zero;
        private IntPtr _channel = IntPtr.Zero;
        private CancellationTokenSource _cts;
        private Task _rxTask;
        private readonly object _lock = new();

        // 滚动拼装中的最新数据
        private readonly double[] _cells;
        private readonly double[] _temps;
        private volatile BmsReading _latest;

        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        public ZlgBmsDriver(ZlgBmsCanMap map = null)
        {
            _map = map ?? new ZlgBmsCanMap();
            _cells = new double[_map.CellCount];
            _temps = new double[_map.TempCount];
            _latest = new BmsReading();
        }

        public Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try
            {
                _device = ZlgCan.ZCAN_OpenDevice(_map.DeviceType, _map.DeviceIndex, 0);
                if (_device == IntPtr.Zero || (uint)_device == ZlgCan.INVALID_DEVICE_HANDLE)
                    throw new Exception("打开 CAN 设备失败");

                // 新版用字符串属性配波特率：路径形如 "<DeviceIndex>/<CanIndex>/canfd_abit_baud_rate"
                // 经典 CAN 用 "baud_rate"；具体键名以 SDK 手册为准
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
                CommunicationError?.Invoke(this, $"CAN连接失败: {ex.Message}");
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

        /// <summary>CAN 是被动收流，这里直接返回后台线程拼装好的最新快照（packIndex 暂未用，单 PACK/卡）</summary>
        public Task<BmsReading> ReadAsync(int packIndex, CancellationToken token = default)
        {
            var r = _latest;
            if (r == null || (DateTime.Now - r.Timestamp).TotalSeconds > 3)
                throw new TimeoutException("BMS CAN 数据超时（>3s 未更新）");
            return Task.FromResult(r);
        }

        // ── 后台收帧 + 拼装 ──
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
                        ParseFrame(buf[i].frame);
                    PublishSnapshot();
                }
                catch (Exception ex)
                {
                    CommunicationError?.Invoke(this, $"CAN收帧异常: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>按 BMS 协议把一帧解析进 _cells/_temps —— 这里是你最该按手册改的地方</summary>
        private void ParseFrame(ZlgCan.ZCAN_CAN_FRAME f)
        {
            uint id = f.can_id & 0x1FFFFFFF;   // 去掉标志位
            var d = f.data;

            lock (_lock)
            {
                // 单体电压：若干连续 ID，每帧 4 个 uint16(小端, mV)
                if (id >= _map.CellVoltBaseId &&
                    id < _map.CellVoltBaseId + (uint)Math.Ceiling(_map.CellCount / (double)_map.CellsPerFrame))
                {
                    int frameIdx = (int)(id - _map.CellVoltBaseId);
                    for (int k = 0; k < _map.CellsPerFrame; k++)
                    {
                        int cell = frameIdx * _map.CellsPerFrame + k;
                        if (cell >= _map.CellCount) break;
                        ushort mv = (ushort)(d[k * 2] | (d[k * 2 + 1] << 8));   // 小端
                        _cells[cell] = mv * _map.CellVoltScale;
                    }
                }
                // 温度：示例每帧 4 个 int16(0.1℃)
                else if (id >= _map.TempBaseId &&
                         id < _map.TempBaseId + (uint)Math.Ceiling(_map.TempCount / (double)_map.TempsPerFrame))
                {
                    int frameIdx = (int)(id - _map.TempBaseId);
                    for (int k = 0; k < _map.TempsPerFrame; k++)
                    {
                        int tp = frameIdx * _map.TempsPerFrame + k;
                        if (tp >= _map.TempCount) break;
                        short raw = (short)(d[k * 2] | (d[k * 2 + 1] << 8));
                        _temps[tp] = raw * _map.TempScale;
                    }
                }
                // 状态：SOC(byte0,1%)、SOH(byte1,1%)、故障码(byte2,3)
                else if (id == _map.StatusId)
                {
                    _statusSoc = d[0];
                    _statusSoh = d[1];
                    _statusFault = d[2] | (d[3] << 8);
                }
            }
        }

        private int _statusSoc, _statusSoh, _statusFault;

        private void PublishSnapshot()
        {
            lock (_lock)
            {
                _latest = new BmsReading
                {
                    CellVoltages = (double[])_cells.Clone(),
                    Temperatures = (double[])_temps.Clone(),
                    PackVoltage = Math.Round(_cells.Sum(), 2),
                    Soc = _statusSoc,
                    Soh = _statusSoh,
                    FaultCode = _statusFault,
                    Timestamp = DateTime.Now
                };
            }
        }

        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }
}