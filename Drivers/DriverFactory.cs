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

            return cabinet.DriverType switch
            {
                DriverType.Simulator => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.NeWare => new NewareDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.Land => new LandDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.Chroma => new ChromaDriver(cabinet.IpAddress, cabinet.TcpPort),
                // 其它厂商驱动待实现，先回退到模拟器
                DriverType.XinDaNeng => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.Arbin => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.Ets => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }
    }
}
