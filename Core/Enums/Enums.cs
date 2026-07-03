using System.ComponentModel;

namespace BatteryAging.Core.Enums
{
    /// <summary>
    /// 工步类型 - 锂电池充放电基础工步
    /// </summary>
    public enum StepType
    {
        [Description("恒流充电")]
        CC_Charge,

        [Description("恒压充电")]
        CV_Charge,

        [Description("恒流恒压充电")]
        CCCV_Charge,

        [Description("恒流放电")]
        CC_Discharge,

        [Description("恒功率充电")]
        CP_Charge,

        [Description("恒功率放电")]
        CP_Discharge,

        [Description("恒阻放电")]
        CR_Discharge,

        [Description("脉冲")]
        Pulse,

        [Description("静置")]
        Rest,

        [Description("循环")]
        Loop,

        [Description("DCIR内阻")]
        DCIR,

        [Description("子程序调用")]
        SubCall,

        [Description("结束")]
        End
    }

    /// <summary>
    /// 触发条件类型
    /// </summary>
    public enum TriggerType
    {
        [Description("时间")]
        Time,                // 时间触发

        [Description("电压")]
        Voltage,             // 电压触发

        [Description("电流")]
        Current,             // 电流触发

        [Description("容量")]
        Capacity,            // 容量触发

        [Description("能量")]
        Energy,              // 能量触发

        [Description("温度")]
        Temperature          // 温度触发
    }

    /// <summary>
    /// 比较操作符
    /// </summary>
    public enum CompareOperator
    {
        [Description("大于等于")]
        GreaterOrEqual,

        [Description("小于等于")]
        LessOrEqual,

        [Description("等于")]
        Equal,

        [Description("大于")]
        Greater,

        [Description("小于")]
        Less
    }

    /// <summary>
    /// 通道运行状态
    /// </summary>
    public enum ChannelStatus
    {
        [Description("空闲")]
        Idle,

        [Description("运行中")]
        Running,

        [Description("已暂停")]
        Paused,

        [Description("已完成")]
        Completed,

        [Description("已停止")]
        Stopped,

        [Description("故障")]
        Error,

        [Description("保护")]
        Protected
    }

    /// <summary>
    /// 工步运行状态
    /// </summary>
    public enum StepStatus
    {
        [Description("未开始")]
        NotStarted,

        [Description("运行中")]
        Running,

        [Description("已完成")]
        Completed,

        [Description("已跳过")]
        Skipped,

        [Description("已中止")]
        Aborted
    }

    /// <summary>
    /// 保护类型
    /// </summary>
    public enum ProtectionType
    {
        [Description("无")]
        None,
        [Description("过压")]
        OverVoltage,
        [Description("欠压")]
        UnderVoltage,
        [Description("过流")]
        OverCurrent,
        [Description("过温")]
        OverTemperature,
        [Description("超时")]
        Timeout,
        [Description("反接")]
        ReversePolarity,
        [Description("电压跌落异常")]
        VoltageDropAnomaly,
        [Description("通讯中断")]
        CommunicationLost,
        [Description("单体过压")]
        CellOverVoltage,
        [Description("单体欠压")]
        CellUnderVoltage,
        [Description("压差过大")]
        CellVoltageDeltaHigh,
        [Description("温差过大")]
        TempDeltaHigh,
        [Description("BMS通讯中断")]
        BmsCommunicationLost,
        [Description("BMS上报故障")]
        BmsFault,
        // 新增保护类型请追加在末尾，切勿插入中间——ProtectionTrigger 按枚举序号持久化，插入会错位已存数据
        [Description("温升速率异常")]
        RapidTempRise
    }

    /// <summary>
    /// 驱动类型 - 支持的设备厂商
    /// </summary>
    public enum DriverType
    {
        [Description("模拟器")]
        Simulator,           // =0
        [Description("Modbus")]
        Modbus,              // =1
        [Description("Socket")]
        GenericSocket,       // =2
        [Description("串口通用协议")]
        GenericSerial,       // =3 RS232/RS485 通用 ASCII 协议
        [Description("CAN")]
        Can                  // =4 CAN 总线（充放电指令 + 遥测）
    }

    /// <summary>
    /// 通讯方式
    /// </summary>
    public enum ConnectionType
    {
        [Description("TCP/IP")]
        Tcp,
        [Description("串口")]
        Serial,
        [Description("CAN")]
        Can
    }

    /// <summary>
    /// 标准化通讯协议分类 —— 驱动适配层向上层暴露的统一协议选择项。
    /// 类似"USB 兼容驱动"：用户只需选协议 + 品牌型号，适配层负责向下对接具体链路（DriverType/ConnectionType）。
    /// </summary>
    public enum CommProtocol
    {
        [Description("模拟器")]
        Simulator,
        [Description("Modbus (RTU/TCP)")]
        Modbus,
        [Description("RS232 / RS485 串口")]
        Serial,
        [Description("TCP/IP")]
        TcpIp,
        [Description("CAN 总线")]
        Can
    }

    /// <summary>BMS 采集驱动类型</summary>
    public enum BmsDriverType
    {
        [Description("模拟器")]
        Simulator,
        [Description("Modbus")]
        Modbus,
        [Description("CAN")]
        Can
    }

    /// <summary>
    /// 机柜状态
    /// </summary>
    public enum CabinetStatus
    {
        [Description("离线")]
        Offline,
        [Description("已连接")]
        Connected,
        [Description("通讯异常")]
        CommunicationError,
        [Description("已禁用")]
        Disabled
    }

    /// <summary>
    /// 容量档位
    /// </summary>
    public enum CapacityGrade
    {
        [Description("未分档")]
        Unknown,
        [Description("A档")]
        A,
        [Description("B档")]
        B,
        [Description("C档")]
        C,
        [Description("D档")]
        D,
        [Description("不合格")]
        Reject
    }

    public enum CabinetType
    {
        [Description("电芯柜")]  //单体
        Cell,
        [Description("模组柜")] //多个电芯串并联
        Module,
        [Description("PACK柜")]  //多个模组
        Pack,
        [Description("温箱")]
        Chamber
    }

    /// <summary>批量任务队列中一个任务的状态（运行时内存态，不落库）</summary>
    public enum TestJobStatus
    {
        [Description("排队中")]
        Queued,
        [Description("运行中")]
        Running,
        [Description("已取消")]
        Cancelled
    }
}
