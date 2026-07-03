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
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Tcp;

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

        // ── 通道采样配置 ──
        public int SamplingIntervalMs { get; set; } = 1000;   // 采样间隔(ms)
        public CabinetType CabinetType { get; set; } = CabinetType.Cell;

        // 关联温箱（环境仓联动）
        public bool HasChamber { get; set; } = false;
        public string ChamberIp { get; set; } = "127.0.0.1";
        public int ChamberPort { get; set; } = 502;

        // ── BMS（PACK 单体电压 / 多路温度采集）──
        public bool HasBms { get; set; } = false;
        public BmsDriverType BmsDriverType { get; set; } = BmsDriverType.Simulator;
        public string BmsIp { get; set; } = "127.0.0.1";
        public int BmsPort { get; set; } = 502;
        public int CellCount { get; set; } = 0;        // 每 PACK 单体数（0=非PACK）
        public int TempPointCount { get; set; } = 0;   // 温度采集点数

        // ── 标准化驱动适配层（见 Drivers/Adapters/DeviceAdapterRegistry）──
        public CommProtocol Protocol { get; set; } = CommProtocol.Simulator;   // 统一协议分类，驱动可视化配置界面
        public string AdapterId { get; set; }          // 适配层中选定的品牌/型号 id；留空则按 DriverType/ConnectionType 自动匹配（兼容旧数据）

        // ── CAN 连接参数（主设备下发指令 / 采集遥测共用）──
        public int CanDeviceIndex { get; set; } = 0;    // CAN 卡设备序号（多卡时区分）
        public int CanChannelIndex { get; set; } = 0;   // 卡内通道号（0/1...）
        public string CanBaudRate { get; set; } = "500000";
    }
}
