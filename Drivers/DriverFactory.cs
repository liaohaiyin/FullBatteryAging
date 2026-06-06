using System;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 驱动工厂：ConnectionType 决定"用哪条链路"，DriverType 决定"说什么协议"，二者正交组合。
    /// </summary>
    public static class DriverFactory
    {
        public static IDeviceDriver Create(Cabinet cabinet, int samplingIntervalMs)
        {
            if (cabinet == null) throw new ArgumentNullException(nameof(cabinet));

            bool isTcp = cabinet.ConnectionType == ConnectionType.Tcp;

            return cabinet.DriverType switch
            {
                DriverType.Simulator =>
                    new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),

                DriverType.Modbus => isTcp
                    ? new ModbusDeviceDriver(cabinet.IpAddress, cabinet.TcpPort, BuildModbusMap(cabinet))
                    : new ModbusDeviceDriver(cabinet.SerialPort, cabinet.BaudRate,
                                             cabinet.DataBits, cabinet.StopBits, cabinet.Parity, BuildModbusMap(cabinet)),

                // 通用 Socket 仅适用于 TCP；串口组合矛盾，回退模拟器避免初始化崩溃
                DriverType.GenericSocket => isTcp
                    ? new SocketDeviceDriver(cabinet.IpAddress, cabinet.TcpPort)
                    : new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs),

                _ => new SimulatorDriver(cabinet.ChannelCount, samplingIntervalMs)
            };
        }

        private static ModbusRegisterMap BuildModbusMap(Cabinet cab) => new ModbusRegisterMap
        {
            // TODO: 按设备《Modbus 协议手册》调整。默认值仅为占位。
            UnitIdBase = 1,
        };
    }
}