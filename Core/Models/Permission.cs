using System;

namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 按位权限标志。新增权限请向高位追加（1L &lt;&lt; n），不要复用已删除位。
    /// </summary>
    [Flags]
    public enum Permission : long
    {
        None = 0,

        // ── 测试执行 ─────────────────────────────────────────────────────
        TestExecution_View = 1L << 0,   // 查看执行页面
        TestExecution_Start = 1L << 1,   // 开始（单通道）测试
        TestExecution_Stop = 1L << 2,   // 停止（单通道）测试
        TestExecution_New = 1L << 3,   // 新建/重置测试
        // 下列为后续补充的执行类操作，使用高位（23+）追加，避免改动已存掩码的低位含义
        TestExecution_Pause = 1L << 23,  // 暂停
        TestExecution_Resume = 1L << 24,  // 恢复
        TestExecution_StartAll = 1L << 25,  // 全部启动
        TestExecution_StopAll = 1L << 26,  // 全部停止
        TestExecution_SyncStart = 1L << 27,  // 同步启动
        TestExecution_SkipStep = 1L << 28,  // 跳过工步
        TestExecution_ClearFault = 1L << 29,  // 清除保护/故障
        TestExecution_EmergencyStop = 1L << 30,  // 急停
        TestExecution_ExportLive = 1L << 31,  // 导出实时数据

        // ── 流程编辑（工步编辑）──────────────────────────────────────────
        FlowEditor_View = 1L << 4,
        FlowEditor_New = 1L << 5,
        FlowEditor_Edit = 1L << 6,
        FlowEditor_Save = 1L << 7,
        FlowEditor_Delete = 1L << 8,
        FlowEditor_Clone = 1L << 9,
        FlowEditor_Import = 1L << 10,
        FlowEditor_Export = 1L << 11,
        FlowEditor_Simulate = 1L << 12,

        // ── 数据查询 ─────────────────────────────────────────────────────
        DataQuery_View = 1L << 13,
        DataQuery_Export = 1L << 14,
        DataQuery_Delete = 1L << 15,
        DataQuery_Report = 1L << 16,

        // ── 统计分析（批次/对比分析）─────────────────────────────────────
        Statistics_View = 1L << 17,
        Statistics_Export = 1L << 18,

        // ── 系统设置 ─────────────────────────────────────────────────────
        Settings_CommConfig = 1L << 19,  // 机柜/通信配置
        Settings_UserManagement = 1L << 20,
        Settings_RoleManagement = 1L << 21,
        Settings_SystemConfig = 1L << 22,

        // ── 审计 / 校准 / 任务队列 / 工单 / 运营统计（高位追加）───────────
        Settings_AuditLog = 1L << 32,  // 查看操作审计日志
        Settings_Calibration = 1L << 33,  // 设备校准记录管理
        TestExecution_ManageQueue = 1L << 34,  // 批量任务队列管理
        Production_WorkOrder = 1L << 35,  // 工单管理
        Statistics_Utilization = 1L << 36,  // 设备稼动率/能耗成本统计
        TestExecution_CellHeatmap = 1L << 37,  // 单体电压热力图（多通道矩阵）

        // ── 预定义角色权限集合 ───────────────────────────────────────────
        /// <summary>观察者：只读查看</summary>
        Role_Viewer =
            TestExecution_View |
            DataQuery_View |
            Statistics_View,

        /// <summary>操作员：执行测试 + 查看数据 + 生成报告</summary>
        Role_Operator =
            Role_Viewer |
            TestExecution_Start |
            TestExecution_Stop |
            TestExecution_New |
            TestExecution_Pause |
            TestExecution_Resume |
            TestExecution_StartAll |
            TestExecution_StopAll |
            TestExecution_SyncStart |
            TestExecution_EmergencyStop |
            TestExecution_ManageQueue |
            FlowEditor_View |
            DataQuery_Report,

        /// <summary>工程师：操作员 + 流程编辑（除删除）+ 数据导出 + 机柜配置 + 校准/审计</summary>
        Role_Engineer =
            Role_Operator |
            TestExecution_SkipStep |
            TestExecution_ClearFault |
            TestExecution_ExportLive |
            FlowEditor_New |
            FlowEditor_Edit |
            FlowEditor_Save |
            FlowEditor_Clone |
            FlowEditor_Import |
            FlowEditor_Export |
            FlowEditor_Simulate |
            DataQuery_Export |
            Statistics_Export |
            Settings_CommConfig |
            Settings_Calibration |
            Settings_AuditLog |
            Production_WorkOrder |
            Statistics_Utilization |
            TestExecution_CellHeatmap,

        /// <summary>管理员：全部权限 —— ~None 是按位取反，None=0 取反后全部位为 1，
        /// 天然覆盖包括未来新增权限位在内的所有权限，无需每加一个权限就手动加进这里</summary>
        Role_Admin = ~None,
    }

    /// <summary>登录后的当前会话（不可变快照）</summary>
    public class UserSession
    {
        public static readonly UserSession Empty = new();

        public bool IsAuthenticated { get; init; }
        public int UserId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string RoleName { get; init; } = string.Empty;
        public Permission Permissions { get; init; }
        public DateTime LoginTime { get; init; }
        public bool IsDeveloper { get; init; }

        public bool HasPermission(Permission permission) =>
            IsAuthenticated && (Permissions & permission) == permission;

        public bool HasAnyPermission(params Permission[] permissions)
        {
            if (!IsAuthenticated) return false;
            foreach (var p in permissions)
                if ((Permissions & p) == p) return true;
            return false;
        }
    }
}
