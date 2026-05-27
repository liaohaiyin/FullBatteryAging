using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BatteryAging.Core.Enums;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 测试机柜 - 一个物理机柜，包含多个通道，对应一个设备连接
    /// </summary>
    public class Cabinet
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; }                     // 机柜名称
        public int CabinetIndex { get; set; }                // 机柜编号（1, 2, 3...）

        public DriverType DriverType { get; set; } = DriverType.Simulator;
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Simulation;

        // ── TCP 连接参数 ──
        public string IpAddress { get; set; } = "127.0.0.1";
        public int TcpPort { get; set; } = 502;

        // ── 串口连接参数 ──
        public string SerialPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public int StopBits { get; set; } = 1;
        public string Parity { get; set; } = "None";        // None/Odd/Even

        // ── 机柜通道范围（全局通道号映射） ──
        public int ChannelStartIndex { get; set; } = 1;     // 该机柜起始通道号
        public int ChannelCount { get; set; } = 8;          // 该机柜通道数

        public CabinetStatus Status { get; set; } = CabinetStatus.Offline;

        public bool IsEnabled { get; set; } = true;
        public DateTime CreateTime { get; set; } = DateTime.Now;
        public DateTime UpdateTime { get; set; } = DateTime.Now;
        public string Remark { get; set; }
    }
}
