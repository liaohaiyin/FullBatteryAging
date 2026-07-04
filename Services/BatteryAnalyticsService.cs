using System;
using System.Collections.Generic;
using System.Linq;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;

namespace BatteryAging.Services
{
    /// <summary>批次一致性分析结果</summary>
    public class ConsistencyReport
    {
        public int SampleCount { get; set; }
        public double MeanCapacity { get; set; }
        public double StdDevCapacity { get; set; }
        public double MinCapacity { get; set; }
        public double MaxCapacity { get; set; }
        public double RangeCapacity { get; set; }
        public double CvPercent { get; set; }       // 变异系数 = std/mean × 100%
        public bool IsConsistent { get; set; }      // CV < 5% 视为一致
        public string Summary => $"N={SampleCount}, μ={MeanCapacity:F4} Ah, σ={StdDevCapacity:F4}, CV={CvPercent:F2}%";
    }

    /// <summary>容量分档区间定义</summary>
    public class GradeBoundary
    {
        public CapacityGrade Grade { get; set; }
        public double MinCapacity { get; set; }
        public double MaxCapacity { get; set; }
    }

    /// <summary>
    /// 剩余循环寿命（RUL）预测结果 —— 对历史循环的容量衰减趋势做线性回归外推，
    /// 估算达到寿命终止阈值（EOL，通常为标称容量的 80%）还需多少次循环。
    /// </summary>
    public class RulEstimate
    {
        public int SampleCount { get; set; }
        public int CurrentCycle { get; set; }
        public double CurrentCapacity { get; set; }
        public double EndOfLifeCapacity { get; set; }

        /// <summary>容量随循环次数的线性回归斜率（Ah/循环，衰减时为负）</summary>
        public double Slope { get; set; }
        public double Intercept { get; set; }
        /// <summary>拟合优度 R²，越接近 1 说明衰减趋势越线性、预测越可信</summary>
        public double RSquared { get; set; }

        /// <summary>外推预计达到 EOL 的循环序号；样本不足或未见衰减趋势时为 null</summary>
        public int? PredictedEndOfLifeCycle { get; set; }
        /// <summary>剩余可用循环数 = PredictedEndOfLifeCycle - CurrentCycle</summary>
        public int? RemainingUsefulCycles { get; set; }

        /// <summary>样本量、拟合优度、衰减方向都达标时才认为预测可信</summary>
        public bool IsReliable => SampleCount >= 5 && RSquared >= 0.5 && Slope < 0;

        public string Summary => RemainingUsefulCycles.HasValue
            ? $"预计还可循环 {RemainingUsefulCycles} 次（第 {PredictedEndOfLifeCycle} 次达到 EOL），R²={RSquared:F2}{(IsReliable ? "" : "（样本不足，仅供参考）")}"
            : SampleCount < 3
                ? "循环数据不足，无法预测"
                : "容量未见明显衰减趋势，暂无法预测寿命终止点";
    }

    /// <summary>dQ/dV 微分容量曲线上的一个采样点</summary>
    public class DqDvPoint
    {
        public double Voltage { get; set; }
        public double DqDv { get; set; }
    }

    /// <summary>单个通道在统计窗口内的稼动情况</summary>
    public class ChannelUtilization
    {
        public int ChannelIndex { get; set; }
        public double OccupiedHours { get; set; }
        public double WindowHours { get; set; }
        public double UtilizationPercent { get; set; }
        public int TotalRecords { get; set; }
        public int PassedRecords { get; set; }
        public double PassRatePercent { get; set; }
    }

    /// <summary>设备 OEE 稼动率统计报告（简化版：可用率 × 良品率，缺乏理论节拍时间故不含性能因子）</summary>
    public class UtilizationReport
    {
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public List<ChannelUtilization> Channels { get; set; } = new();
        public double OverallUtilizationPercent { get; set; }
        public double OverallPassRatePercent { get; set; }
    }

    /// <summary>
    /// EMS 能耗回馈/电费成本核算 —— 基于已有充放电能量数据汇总，
    /// 放电能量按回馈效率折算为"回馈电量"，冲抵充电能耗后估算净耗电与电费成本。
    /// </summary>
    public class EnergyCostReport
    {
        public double TotalChargeEnergyWh { get; set; }
        public double TotalDischargeEnergyWh { get; set; }
        public double FeedbackEfficiency { get; set; } = 0.85;
        public double ElectricityPricePerKwh { get; set; }

        public double FeedbackEnergyWh => TotalDischargeEnergyWh * FeedbackEfficiency;
        public double NetEnergyWh => Math.Max(0, TotalChargeEnergyWh - FeedbackEnergyWh);
        public double FeedbackSavingPercent => TotalChargeEnergyWh > 0 ? FeedbackEnergyWh / TotalChargeEnergyWh * 100 : 0;
        public double EstimatedCost => NetEnergyWh / 1000.0 * ElectricityPricePerKwh;
    }

    public interface IBatteryAnalyticsService
    {
        /// <summary>SOH 估算（容量法）：实际放电容量 / 标称容量</summary>
        double EstimateSohByCapacity(double dischargeCapacity, double nominalCapacity);

        /// <summary>SOH 估算（内阻法）：以初始内阻为基准的劣化估计</summary>
        double EstimateSohByResistance(double currentResistance, double initialResistance);

        /// <summary>对放电容量进行档位归档</summary>
        CapacityGrade GradeCapacity(double dischargeCapacity, double nominalCapacity,
            IEnumerable<GradeBoundary> boundaries = null);

        /// <summary>批次一致性统计</summary>
        ConsistencyReport AnalyzeBatchConsistency(IEnumerable<TestRecord> records);

        /// <summary>默认分档规则（基于标称容量百分比）</summary>
        List<GradeBoundary> GetDefaultGradeBoundaries(double nominalCapacity);

        /// <summary>基于历史循环容量数据线性外推剩余循环寿命（RUL）</summary>
        RulEstimate EstimateRul(IEnumerable<CycleData> cycles, double nominalCapacity, double eolFraction = 0.8);

        /// <summary>按电压分箱统计容量增量，计算 dQ/dV 微分容量曲线（用于识别相变/衰减机理）</summary>
        List<DqDvPoint> ComputeDifferentialCapacity(IEnumerable<DataPoint> points, int voltageBins = 100);

        /// <summary>按通道统计给定时间窗口内的占用时长/稼动率/良品率（设备 OEE 简化版）</summary>
        UtilizationReport ComputeUtilization(IEnumerable<TestRecord> records, DateTime windowStart, DateTime windowEnd);

        /// <summary>基于已有充放电能量数据核算回馈节电率与电费成本</summary>
        EnergyCostReport ComputeEnergyCost(IEnumerable<TestRecord> records, double electricityPricePerKwh, double feedbackEfficiency = 0.85);
    }

    public class BatteryAnalyticsService : IBatteryAnalyticsService
    {
        public double EstimateSohByCapacity(double dischargeCapacity, double nominalCapacity)
        {
            if (nominalCapacity <= 0) return 0;
            return Math.Round(Math.Clamp(dischargeCapacity / nominalCapacity, 0, 1.2), 4);
        }

        public double EstimateSohByResistance(double currentResistance, double initialResistance)
        {
            if (initialResistance <= 0 || currentResistance <= 0) return 0;
            // 内阻每增加 50% 视为容量衰减 20%（经验公式，可调）
            var growth = (currentResistance - initialResistance) / initialResistance;
            return Math.Round(Math.Clamp(1.0 - 0.4 * growth, 0, 1), 4);
        }

        public CapacityGrade GradeCapacity(double dischargeCapacity, double nominalCapacity,
            IEnumerable<GradeBoundary> boundaries = null)
        {
            var list = (boundaries ?? GetDefaultGradeBoundaries(nominalCapacity)).ToList();
            foreach (var b in list)
            {
                if (dischargeCapacity >= b.MinCapacity && dischargeCapacity < b.MaxCapacity)
                    return b.Grade;
            }
            return CapacityGrade.Reject;
        }

        public List<GradeBoundary> GetDefaultGradeBoundaries(double nominalCapacity)
        {
            // A: ≥98%, B: 95~98%, C: 90~95%, D: 80~90%, Reject: <80%
            return new List<GradeBoundary>
            {
                new() { Grade = CapacityGrade.A, MinCapacity = nominalCapacity * 0.98, MaxCapacity = nominalCapacity * 1.2 },
                new() { Grade = CapacityGrade.B, MinCapacity = nominalCapacity * 0.95, MaxCapacity = nominalCapacity * 0.98 },
                new() { Grade = CapacityGrade.C, MinCapacity = nominalCapacity * 0.90, MaxCapacity = nominalCapacity * 0.95 },
                new() { Grade = CapacityGrade.D, MinCapacity = nominalCapacity * 0.80, MaxCapacity = nominalCapacity * 0.90 },
                new() { Grade = CapacityGrade.Reject, MinCapacity = 0, MaxCapacity = nominalCapacity * 0.80 }
            };
        }

        public ConsistencyReport AnalyzeBatchConsistency(IEnumerable<TestRecord> records)
        {
            var caps = records.Where(r => r.TotalDischargeCapacity > 0)
                              .Select(r => r.TotalDischargeCapacity)
                              .ToList();
            if (caps.Count == 0)
                return new ConsistencyReport { SampleCount = 0 };

            var mean = caps.Average();
            var variance = caps.Sum(c => (c - mean) * (c - mean)) / caps.Count;
            var std = Math.Sqrt(variance);
            var cv = mean > 0 ? std / mean * 100 : 0;
            var min = caps.Min();
            var max = caps.Max();

            return new ConsistencyReport
            {
                SampleCount = caps.Count,
                MeanCapacity = Math.Round(mean, 5),
                StdDevCapacity = Math.Round(std, 5),
                MinCapacity = min,
                MaxCapacity = max,
                RangeCapacity = Math.Round(max - min, 5),
                CvPercent = Math.Round(cv, 3),
                IsConsistent = cv < 5.0
            };
        }

        public RulEstimate EstimateRul(IEnumerable<CycleData> cycles, double nominalCapacity, double eolFraction = 0.8)
        {
            var pts = (cycles ?? Enumerable.Empty<CycleData>())
                .Where(c => c.CycleIndex > 0 && c.DischargeCapacity > 0)
                .OrderBy(c => c.CycleIndex)
                .Select(c => (X: (double)c.CycleIndex, Y: c.DischargeCapacity))
                .ToList();

            var result = new RulEstimate
            {
                SampleCount = pts.Count,
                EndOfLifeCapacity = Math.Round(nominalCapacity * eolFraction, 5),
            };
            if (pts.Count == 0) return result;

            result.CurrentCycle = (int)pts[^1].X;
            result.CurrentCapacity = pts[^1].Y;
            if (pts.Count < 3) return result;   // 样本太少，不做回归外推

            // 最小二乘线性回归：y(容量) = slope * x(循环数) + intercept
            double n = pts.Count;
            double sumX = pts.Sum(p => p.X);
            double sumY = pts.Sum(p => p.Y);
            double sumXY = pts.Sum(p => p.X * p.Y);
            double sumXX = pts.Sum(p => p.X * p.X);

            double denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-9) return result;

            double slope = (n * sumXY - sumX * sumY) / denom;
            double intercept = (sumY - slope * sumX) / n;

            double meanY = sumY / n;
            double ssTot = pts.Sum(p => (p.Y - meanY) * (p.Y - meanY));
            double ssRes = pts.Sum(p => { var pred = slope * p.X + intercept; var e = p.Y - pred; return e * e; });
            double rSquared = ssTot > 1e-9 ? 1.0 - ssRes / ssTot : 0;

            result.Slope = slope;
            result.Intercept = intercept;
            result.RSquared = Math.Round(Math.Clamp(rSquared, 0, 1), 4);

            if (slope < 0)
            {
                // 回归直线 y = slope*x + intercept，解出 y = EndOfLifeCapacity 时的 x（循环数）
                double eolCycle = (result.EndOfLifeCapacity - intercept) / slope;
                if (eolCycle > result.CurrentCycle)
                {
                    result.PredictedEndOfLifeCycle = (int)Math.Ceiling(eolCycle);
                    result.RemainingUsefulCycles = result.PredictedEndOfLifeCycle - result.CurrentCycle;
                }
                else
                {
                    // 回归线已越过 EOL 容量：认为已到寿命终止
                    result.PredictedEndOfLifeCycle = result.CurrentCycle;
                    result.RemainingUsefulCycles = 0;
                }
            }
            return result;
        }

        public List<DqDvPoint> ComputeDifferentialCapacity(IEnumerable<DataPoint> points, int voltageBins = 100)
        {
            var ordered = (points ?? Enumerable.Empty<DataPoint>())
                .Where(p => p.Voltage > 0)
                .OrderBy(p => p.Timestamp)
                .ToList();
            if (ordered.Count < 10 || voltageBins < 2) return new List<DqDvPoint>();

            double vMin = ordered.Min(p => p.Voltage);
            double vMax = ordered.Max(p => p.Voltage);
            if (vMax - vMin < 0.01) return new List<DqDvPoint>();

            double binWidth = (vMax - vMin) / voltageBins;
            var binCapacity = new double[voltageBins];

            for (int i = 1; i < ordered.Count; i++)
            {
                var dQ = ordered[i].Capacity - ordered[i - 1].Capacity;
                if (dQ == 0) continue;
                var vMid = (ordered[i].Voltage + ordered[i - 1].Voltage) / 2.0;
                int bin = (int)((vMid - vMin) / binWidth);
                if (bin < 0) bin = 0;
                if (bin >= voltageBins) bin = voltageBins - 1;
                // 同一工步内 |Capacity| 单调递增，跨工步边界重置为 0 会产生负跳变，取绝对值避免污染曲线
                binCapacity[bin] += Math.Abs(dQ);
            }

            var raw = new List<DqDvPoint>();
            for (int b = 0; b < voltageBins; b++)
            {
                if (binCapacity[b] <= 0) continue;
                raw.Add(new DqDvPoint
                {
                    Voltage = Math.Round(vMin + (b + 0.5) * binWidth, 4),
                    DqDv = binCapacity[b] / binWidth
                });
            }

            // 三点滑动平均去噪
            var smoothed = new List<DqDvPoint>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                double sum = raw[i].DqDv; int n = 1;
                if (i > 0) { sum += raw[i - 1].DqDv; n++; }
                if (i < raw.Count - 1) { sum += raw[i + 1].DqDv; n++; }
                smoothed.Add(new DqDvPoint { Voltage = raw[i].Voltage, DqDv = Math.Round(sum / n, 4) });
            }
            return smoothed;
        }

        public UtilizationReport ComputeUtilization(IEnumerable<TestRecord> records, DateTime windowStart, DateTime windowEnd)
        {
            var windowHours = Math.Max(0.0001, (windowEnd - windowStart).TotalHours);
            var report = new UtilizationReport { WindowStart = windowStart, WindowEnd = windowEnd };

            var byChannel = (records ?? Enumerable.Empty<TestRecord>())
                .Where(r => r.StartTime < windowEnd && (r.EndTime ?? DateTime.Now) > windowStart)
                .GroupBy(r => r.ChannelIndex);

            double totalOccupied = 0;
            int totalRecords = 0, totalPassed = 0;

            foreach (var g in byChannel.OrderBy(g => g.Key))
            {
                double occupiedHours = 0;
                foreach (var r in g)
                {
                    // 记录的实际起止时间可能跨出统计窗口两端（测试提前开始/仍未结束），
                    // 这里把每条记录的区间裁剪到 [windowStart, windowEnd] 内再计入占用时长，
                    // 避免把窗口外的时间也算进稼动率。
                    var s = r.StartTime > windowStart ? r.StartTime : windowStart;
                    var e = (r.EndTime ?? DateTime.Now) < windowEnd ? (r.EndTime ?? DateTime.Now) : windowEnd;
                    if (e > s) occupiedHours += (e - s).TotalHours;
                }

                var recordsCount = g.Count();
                var passedCount = g.Count(r => r.Grade != CapacityGrade.Reject);

                totalOccupied += occupiedHours;
                totalRecords += recordsCount;
                totalPassed += passedCount;

                report.Channels.Add(new ChannelUtilization
                {
                    ChannelIndex = g.Key,
                    OccupiedHours = Math.Round(occupiedHours, 3),
                    WindowHours = Math.Round(windowHours, 3),
                    UtilizationPercent = Math.Round(Math.Clamp(occupiedHours / windowHours * 100, 0, 100), 2),
                    TotalRecords = recordsCount,
                    PassedRecords = passedCount,
                    PassRatePercent = recordsCount > 0 ? Math.Round((double)passedCount / recordsCount * 100, 2) : 0
                });
            }

            var channelCount = Math.Max(1, report.Channels.Count);
            report.OverallUtilizationPercent = Math.Round(Math.Clamp(totalOccupied / (windowHours * channelCount) * 100, 0, 100), 2);
            report.OverallPassRatePercent = totalRecords > 0 ? Math.Round((double)totalPassed / totalRecords * 100, 2) : 0;
            return report;
        }

        public EnergyCostReport ComputeEnergyCost(IEnumerable<TestRecord> records, double electricityPricePerKwh, double feedbackEfficiency = 0.85)
        {
            var list = (records ?? Enumerable.Empty<TestRecord>()).ToList();
            return new EnergyCostReport
            {
                TotalChargeEnergyWh = Math.Round(list.Sum(r => r.TotalChargeEnergy), 4),
                TotalDischargeEnergyWh = Math.Round(list.Sum(r => r.TotalDischargeEnergy), 4),
                FeedbackEfficiency = feedbackEfficiency,
                ElectricityPricePerKwh = electricityPricePerKwh
            };
        }
    }
}
