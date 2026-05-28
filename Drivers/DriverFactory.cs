using System;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 驱动工厂 - 根据机柜配置创建对应驱动
    /// </summary>
    public static class DriverFactory
    {
        public static IDeviceDriver Create(Cabinet cabinet, int samplingIntervalMs)
        {
            if (cabinet == null) throw new ArgumentNullException(nameof(cabinet));
            return cabinet.ConnectionType switch
            {
                ConnectionType.Tcp => CreateTcpDriver(cabinet, samplingIntervalMs),
                ConnectionType.RS485 => throw new NotSupportedException("RS485 connection type is not supported yet."),
                _ => CreateTcpDriver(cabinet, samplingIntervalMs)
            };
        }

        private static IDeviceDriver CreateTcpDriver(Cabinet cabinet, int samplingIntervalMs)
        {
            return cabinet.DriverType switch
            {
                DriverType.Simulator => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.NeWare => new NewareDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.Land => new LandDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.XinDaNeng => new XinDaNengDriver(cabinet.IpAddress, cabinet.TcpPort),
                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }
    }
}
