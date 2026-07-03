using System;
using System.Runtime.InteropServices;

namespace BatteryAging.Drivers.Can
{
    /// <summary>
    /// ZLG zlgcan.dll P/Invoke 封装。
    /// ⚠ 结构体布局、函数签名、DeviceType 常量必须对照你所用 ZLG 卡的官方 SDK 头文件核对！
    /// 此处以新版 zlgcan(CAN/CANFD)接口为模板。
    /// </summary>
    internal static class ZlgCan
    {
        private const string Dll = "zlgcan.dll";

        // ── 设备类型常量（节选，按手册补全你的型号）──
        public const uint ZCAN_USBCAN1 = 3;
        public const uint ZCAN_USBCAN2 = 4;
        public const uint ZCAN_USBCANFD_200U = 41;   // 示例：USBCANFD-200U

        public const uint STATUS_OK = 1;
        public const uint INVALID_DEVICE_HANDLE = 0;
        public const uint INVALID_CHANNEL_HANDLE = 0;

        // 标准 CAN 帧（经典 CAN，8 字节）
        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_CAN_FRAME
        {
            public uint can_id;            // bit31=扩展帧标志, bit30=远程帧, bit29=错误帧
            public byte can_dlc;           // 数据长度 0~8
            public byte __pad;
            public byte __res0;
            public byte __res1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_Receive_Data
        {
            public ZCAN_CAN_FRAME frame;
            public ulong timestamp;        // 微秒
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_Transmit_Data
        {
            public ZCAN_CAN_FRAME frame;
            public uint transmit_type;     // 0=正常发送
        }

        // 通道初始化配置（不同 SDK 版本字段差异较大，这里给最常用字段；以手册为准）
        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_CHANNEL_INIT_CONFIG
        {
            public uint can_type;          // 0=CAN, 1=CANFD
            public uint acc_code;          // 验收码
            public uint acc_mask;          // 屏蔽码（0xFFFFFFFF=全部接收）
            public uint reserved;
            public byte filter;
            public byte timing0;           // 经典 CAN 波特率 BTR0（用 SetValue 配波特率时可不填）
            public byte timing1;           // BTR1
            public byte mode;              // 0=正常, 1=只听
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_OpenDevice(uint device_type, uint device_index, uint reserved);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_CloseDevice(IntPtr device_handle);

        // 波特率等常用新版用字符串属性接口配置
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, string value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_InitCAN(IntPtr device_handle, uint can_index, ref ZCAN_CHANNEL_INIT_CONFIG config);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_StartCAN(IntPtr channel_handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ResetCAN(IntPtr channel_handle);

        // 缓冲区里有多少帧待收
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_GetReceiveNum(IntPtr channel_handle, byte type /*0=CAN,1=CANFD*/);

        // 批量收帧
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Receive(IntPtr channel_handle,
            [Out] ZCAN_Receive_Data[] data, uint len, int wait_time_ms);

        // 批量发帧（下发充放电指令用）
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Transmit(IntPtr channel_handle,
            [In] ZCAN_Transmit_Data[] data, uint len);
    }
}