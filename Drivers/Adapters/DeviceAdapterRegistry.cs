using System;
using System.Collections.Generic;
using System.Linq;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Drivers.Can;

namespace BatteryAging.Drivers.Adapters
{
    /// <summary>
    /// 标准化驱动适配层的注册中心 —— 类似电脑上的"USB 兼容驱动"：
    /// 向下以统一的 <see cref="DeviceAdapterDescriptor"/> 接入不同品牌、不同链路的硬件设备（CAN / Modbus / RS232-485 / TCP-IP），
    /// 向上为 <see cref="DriverFactory"/>/<see cref="BmsDriverFactory"/> 和设备配置界面提供统一的发现、解析入口。
    /// 新增品牌/协议只需调用 <see cref="Register"/> 注册一个描述符，无需改动工厂或界面代码（开闭原则）。
    /// </summary>
    public static class DeviceAdapterRegistry
    {
        public const string SimulatorId = "simulator";

        private static readonly object _gate = new();
        private static readonly Dictionary<string, DeviceAdapterDescriptor> _byId = new();

        static DeviceAdapterRegistry() => RegisterBuiltIns();

        /// <summary>注册（或覆盖同 Id 的）适配器描述符 —— 供内置协议库或第三方插件调用</summary>
        public static void Register(DeviceAdapterDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Id)) throw new ArgumentException("适配器 Id 不能为空", nameof(descriptor));
            lock (_gate) _byId[descriptor.Id] = descriptor;
        }

        public static IReadOnlyList<DeviceAdapterDescriptor> All
        {
            get { lock (_gate) return _byId.Values.OrderBy(a => a.Protocol).ThenBy(a => a.DisplayName).ToList(); }
        }

        public static IEnumerable<DeviceAdapterDescriptor> ForProtocol(CommProtocol protocol)
            => All.Where(a => a.Protocol == protocol);

        public static DeviceAdapterDescriptor Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            lock (_gate) return _byId.TryGetValue(id, out var d) ? d : null;
        }

        /// <summary>
        /// 解析机柜应使用的主设备适配器。优先按显式选择的 AdapterId；
        /// 若为空（历史数据）则按 DriverType/ConnectionType 自动推断，保证旧配置不受影响。
        /// </summary>
        public static DeviceAdapterDescriptor ResolveMain(Cabinet cabinet)
        {
            if (cabinet == null) throw new ArgumentNullException(nameof(cabinet));

            var byId = Get(cabinet.AdapterId);
            if (byId != null && byId.SupportsMainDriver) return byId;

            var match = All.FirstOrDefault(a =>
                a.SupportsMainDriver &&
                a.DriverType == cabinet.DriverType &&
                (a.DriverType != DriverType.Modbus || a.ConnectionType == cabinet.ConnectionType));

            return match ?? All.First(a => a.Id == SimulatorId);
        }

        /// <summary>解析机柜应使用的 BMS 采集适配器（按 BmsDriverType 匹配）</summary>
        public static DeviceAdapterDescriptor ResolveBms(Cabinet cabinet)
        {
            if (cabinet == null) throw new ArgumentNullException(nameof(cabinet));

            var match = All.FirstOrDefault(a => a.SupportsBms && a.BmsDriverType == cabinet.BmsDriverType);
            return match ?? All.First(a => a.Id == SimulatorId);
        }

        // ════════════════════════════ 内置适配器 ════════════════════════════
        private static void RegisterBuiltIns()
        {
            Register(new DeviceAdapterDescriptor
            {
                Id = SimulatorId,
                DisplayName = "内置模拟器",
                Protocol = CommProtocol.Simulator,
                Description = "无需真实硬件，用于联调测试方案与界面。",
                SupportsMainDriver = true,
                DriverType = DriverType.Simulator,
                ConnectionType = ConnectionType.Tcp,
                CreateDriver = (cab, ms) => new SimulatorDriver(cab.ChannelCount, ms),
                SupportsBms = true,
                BmsDriverType = BmsDriverType.Simulator,
                CreateBmsDriver = cab => new SimulatorBmsDriver(cab.CellCount, cab.TempPointCount),
            });

            Register(new DeviceAdapterDescriptor
            {
                Id = "modbus-tcp",
                DisplayName = "Modbus TCP 通用充放电设备",
                Protocol = CommProtocol.Modbus,
                Description = "寄存器地址/缩放系数按设备《Modbus 协议手册》在 ModbusRegisterMap 中调整。",
                SupportsMainDriver = true,
                DriverType = DriverType.Modbus,
                ConnectionType = ConnectionType.Tcp,
                RequiresIp = true,
                CreateDriver = (cab, ms) => new ModbusDeviceDriver(cab.IpAddress, cab.TcpPort, new ModbusRegisterMap { UnitIdBase = 1 }),
                SupportsBms = true,
                BmsDriverType = BmsDriverType.Modbus,
                CreateBmsDriver = cab => new ModbusBmsDriver(cab.BmsIp, cab.BmsPort, cab.CellCount, cab.TempPointCount),
            });

            Register(new DeviceAdapterDescriptor
            {
                Id = "modbus-rtu",
                DisplayName = "Modbus RTU 通用充放电设备（RS232/RS485）",
                Protocol = CommProtocol.Modbus,
                Description = "经 RS232/RS485 串口接入，通道号映射从站地址（Unit Id）。",
                SupportsMainDriver = true,
                DriverType = DriverType.Modbus,
                ConnectionType = ConnectionType.Serial,
                RequiresSerial = true,
                CreateDriver = (cab, ms) => new ModbusDeviceDriver(
                    cab.SerialPort, cab.BaudRate, cab.DataBits, cab.StopBits, cab.Parity,
                    new ModbusRegisterMap { UnitIdBase = 1 }),
            });

            Register(new DeviceAdapterDescriptor
            {
                Id = "socket-tcp",
                DisplayName = "通用 TCP/IP 设备（ASCII 行协议）",
                Protocol = CommProtocol.TcpIp,
                Description = "命令模板/响应正则按设备《通讯协议手册》在 SocketProtocolMap 中调整。",
                SupportsMainDriver = true,
                DriverType = DriverType.GenericSocket,
                ConnectionType = ConnectionType.Tcp,
                RequiresIp = true,
                CreateDriver = (cab, ms) => new SocketDeviceDriver(cab.IpAddress, cab.TcpPort),
            });

            Register(new DeviceAdapterDescriptor
            {
                Id = "serial-generic",
                DisplayName = "通用 RS232/RS485 设备（ASCII 行协议）",
                Protocol = CommProtocol.Serial,
                Description = "命令模板/响应正则按设备《通讯协议手册》在 SocketProtocolMap 中调整，多机挂载 RS485 用命令中的通道号区分。",
                SupportsMainDriver = true,
                DriverType = DriverType.GenericSerial,
                ConnectionType = ConnectionType.Serial,
                RequiresSerial = true,
                CreateDriver = (cab, ms) => new SerialSocketDeviceDriver(cab.SerialPort, cab.BaudRate, cab.DataBits, cab.StopBits, cab.Parity),
            });

            Register(new DeviceAdapterDescriptor
            {
                Id = "can-zlg",
                DisplayName = "ZLG CAN（USBCANFD 系列）",
                Protocol = CommProtocol.Can,
                Description = "CAN 帧 ID / 字节布局按设备《CAN 通讯协议》在 ZlgDeviceCanMap / ZlgBmsCanMap 中调整。",
                SupportsMainDriver = true,
                DriverType = DriverType.Can,
                ConnectionType = ConnectionType.Can,
                RequiresCan = true,
                CreateDriver = (cab, ms) => new ZlgDeviceDriver(new ZlgDeviceCanMap
                {
                    DeviceIndex = (uint)Math.Max(0, cab.CanDeviceIndex),
                    CanIndex = (uint)Math.Max(0, cab.CanChannelIndex),
                    BaudRate = cab.CanBaudRate,
                }),
                SupportsBms = true,
                BmsDriverType = BmsDriverType.Can,
                CreateBmsDriver = cab => new ZlgBmsDriver(new ZlgBmsCanMap
                {
                    DeviceIndex = (uint)Math.Max(0, cab.CanDeviceIndex),
                    CanIndex = (uint)Math.Max(0, cab.CanChannelIndex),
                    BaudRate = cab.CanBaudRate,
                    CellCount = cab.CellCount,
                    TempCount = cab.TempPointCount,
                }),
            });
        }
    }
}
