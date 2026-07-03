using System;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.Drivers.Adapters
{
    /// <summary>
    /// 设备适配器描述符 —— 标准化驱动适配层的最小注册单元。
    /// 每个描述符代表"一种协议 + 一种品牌/型号"的接入方式，向下绑定具体 IDeviceDriver/IBmsDriver 的构造方式，
    /// 向上为设备配置界面提供协议分类、必填参数分组等元数据，使新增品牌/协议无需改动工厂或 UI 代码即可接入。
    /// </summary>
    public sealed class DeviceAdapterDescriptor
    {
        /// <summary>全局唯一标识，保存在 Cabinet.AdapterId 中</summary>
        public string Id { get; init; }

        /// <summary>界面展示名称（品牌/型号）</summary>
        public string DisplayName { get; init; }

        /// <summary>标准化协议分类，用于设备配置界面按协议筛选</summary>
        public CommProtocol Protocol { get; init; }

        /// <summary>说明文字（接线方式、注意事项等）</summary>
        public string Description { get; init; }

        // ── 主设备（充放电机）能力 ──
        public bool SupportsMainDriver { get; init; }
        public DriverType DriverType { get; init; }
        public ConnectionType ConnectionType { get; init; }
        public Func<Cabinet, int, IDeviceDriver> CreateDriver { get; init; }

        // ── BMS 采集能力 ──
        public bool SupportsBms { get; init; }
        public BmsDriverType BmsDriverType { get; init; }
        public Func<Cabinet, IBmsDriver> CreateBmsDriver { get; init; }

        // ── 配置界面所需参数分组（决定哪些字段区块可见）──
        public bool RequiresIp { get; init; }
        public bool RequiresSerial { get; init; }
        public bool RequiresCan { get; init; }
    }
}
