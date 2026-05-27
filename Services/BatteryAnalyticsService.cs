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
    }
}
