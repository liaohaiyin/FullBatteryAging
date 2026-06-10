using System;
using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Drivers
{
    /// <summary>BMS 寄存器映射（按 BMS《Modbus 通讯协议》填写）</summary>
    public class BmsModbusMap
    {
        public byte UnitId = 1;
        public ushort CellVoltageBaseReg = 0x0000; public double CellVoltageScale = 0.001; // mV→V
        public ushort TempBaseReg = 0x0100; public double TempScale = 0.1;          // 0.1℃
        public ushort SocReg = 0x0200; public double SocScale = 0.1;
        public ushort SohReg = 0x0201; public double SohScale = 0.1;
        public ushort FaultReg = 0x0202;
    }

    /// <summary>Modbus-TCP BMS 驱动（复用本程序集内部的 ModbusTcpMaster）。</summary>
    public class ModbusBmsDriver : IBmsDriver
    {
        private readonly ModbusTcpMaster _master;     // 与 ModbusDeviceDriver 同一程序集，可访问 internal
        private readonly BmsModbusMap _map;
        private readonly int _cellCount, _tempCount;
        private readonly object _lock = new();

        public bool IsConnected { get; private set; }
        public event EventHandler<string> CommunicationError;

        public ModbusBmsDriver(string host, int port, int cellCount, int tempCount, BmsModbusMap map = null)
        {
            _master = new ModbusTcpMaster(host, port);
            _map = map ?? new BmsModbusMap();
            _cellCount = Math.Max(0, cellCount);
            _tempCount = Math.Max(0, tempCount);
        }

        public Task<bool> ConnectAsync(CancellationToken token = default)
        {
            try { IsConnected = _master.Connect(); }
            catch (Exception ex) { CommunicationError?.Invoke(this, $"BMS连接失败: {ex.Message}"); IsConnected = false; }
            return Task.FromResult(IsConnected);
        }

        public Task DisconnectAsync() { try { _master.Dispose(); } catch { } IsConnected = false; return Task.CompletedTask; }

        public Task<BmsReading> ReadAsync(int packIndex, CancellationToken token = default)
        {
            try
            {
                lock (_lock)
                {
                    var r = new BmsReading { Timestamp = DateTime.Now };
                    if (_cellCount > 0)
                    {
                        var regs = _master.ReadHolding(_map.UnitId, _map.CellVoltageBaseReg, (ushort)_cellCount);
                        r.CellVoltages = new double[regs.Length];
                        for (int i = 0; i < regs.Length; i++) r.CellVoltages[i] = regs[i] * _map.CellVoltageScale;
                    }
                    if (_tempCount > 0)
                    {
                        var regs = _master.ReadHolding(_map.UnitId, _map.TempBaseReg, (ushort)_tempCount);
                        r.Temperatures = new double[regs.Length];
                        for (int i = 0; i < regs.Length; i++) r.Temperatures[i] = (short)regs[i] * _map.TempScale;
                    }
                    r.Soc = _master.ReadHolding(_map.UnitId, _map.SocReg, 1)[0] * _map.SocScale;
                    r.Soh = _master.ReadHolding(_map.UnitId, _map.SohReg, 1)[0] * _map.SohScale;
                    r.FaultCode = _master.ReadHolding(_map.UnitId, _map.FaultReg, 1)[0];
                    return Task.FromResult(r);
                }
            }
            catch (Exception ex)
            {
                CommunicationError?.Invoke(this, $"BMS采样失败: {ex.Message}");
                throw;
            }
        }

        public void Dispose() { try { _master.Dispose(); } catch { } }
    }

    /// <summary>BMS 驱动工厂</summary>
    public static class BmsDriverFactory
    {
        public static IBmsDriver Create(Core.Models.Cabinet cab) => cab.BmsDriverType switch
        {
            Core.Enums.BmsDriverType.Modbus =>
                new ModbusBmsDriver(cab.BmsIp, cab.BmsPort, cab.CellCount, cab.TempPointCount),
            // CAN 需厂商 SDK（PCAN/Kvaser/ZLG 等），暂回退模拟器，避免初始化崩溃
            Core.Enums.BmsDriverType.Can =>
                new Can.ZlgBmsDriver(new Can.ZlgBmsCanMap
                {
                    CellCount = cab.CellCount,
                    TempCount = cab.TempPointCount
                }),
        };
    }
}