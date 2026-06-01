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
                ConnectionType.Serial => CreateSerialDriver(cabinet, samplingIntervalMs),
                ConnectionType.Simulation => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }

        private static IDeviceDriver CreateTcpDriver(Cabinet cabinet, int samplingIntervalMs)
        {
            return cabinet.DriverType switch
            {
                DriverType.Simulator => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.NeWare => new NewareDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.Land => new LandDriver(cabinet.IpAddress, cabinet.TcpPort),
                DriverType.XinDaNeng => new ModbusDeviceDriver(cabinet.IpAddress, cabinet.TcpPort, BuildMap(cabinet)),
                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }

        private static IDeviceDriver CreateSerialDriver(Cabinet cabinet, int samplingIntervalMs)
        {
            return cabinet.DriverType switch
            {
                DriverType.Simulator => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),
                DriverType.NeWare => new NewareSerialDriver(cabinet.SerialPort, cabinet.BaudRate,
                                            cabinet.DataBits, cabinet.StopBits, cabinet.Parity),
                DriverType.Land => new LandSerialDriver(cabinet.SerialPort, cabinet.BaudRate,
                                            cabinet.DataBits, cabinet.StopBits, cabinet.Parity),
                DriverType.XinDaNeng => new ModbusDeviceDriver(cabinet.SerialPort, cabinet.BaudRate,
                                            cabinet.DataBits, cabinet.StopBits, cabinet.Parity, BuildMap(cabinet)),
                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }

        private static ModbusRegisterMap BuildMap(Cabinet cab) => new ModbusRegisterMap
        {
            // TODO: 按设备《Modbus 协议手册》调整。默认值仅为占位。
            UnitIdBase = 1,
        };
    }
}
