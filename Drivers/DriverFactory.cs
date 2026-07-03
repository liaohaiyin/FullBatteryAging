using System;
using BatteryAging.Core.Models;
using BatteryAging.Drivers.Adapters;

namespace BatteryAging.Drivers
{
    /// <summary>
    /// 驱动工厂 —— 委托给标准化驱动适配层（<see cref="DeviceAdapterRegistry"/>）解析并创建具体驱动。
    /// 新增品牌/协议无需修改本类，向 DeviceAdapterRegistry 注册新的 DeviceAdapterDescriptor 即可。
    /// </summary>
    public static class DriverFactory
    {
        public static IDeviceDriver Create(Cabinet cabinet, int samplingIntervalMs)
        {
            if (cabinet == null) throw new ArgumentNullException(nameof(cabinet));
            var adapter = DeviceAdapterRegistry.ResolveMain(cabinet);
            return adapter.CreateDriver(cabinet, samplingIntervalMs);
        }
    }
}