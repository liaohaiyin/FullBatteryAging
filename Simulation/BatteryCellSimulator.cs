using System;

namespace BatteryAging.Simulation
{
    /// <summary>
    /// 锂电池单体模拟器
    /// 模拟一节锂电池在充放电过程中的电压、电流、温度行为
    /// 使用一阶 RC 模型 + OCV-SOC 曲线
    /// </summary>
    public class BatteryCellSimulator
    {
        private readonly Random _rng;
        private readonly double _noise;

        /// <summary>标称容量 (Ah)</summary>
        public double NominalCapacity { get; set; } = 2.6;

        /// <summary>当前 SOC (0~1)</summary>
        public double Soc { get; set; } = 0.5;

        /// <summary>内阻 (Ω)</summary>
        public double InternalResistance { get; set; } = 0.05;

        /// <summary>当前温度 (°C)</summary>
        public double Temperature { get; private set; } = 25.0;

        /// <summary>环境温度 (°C)</summary>
        public double AmbientTemperature { get; set; } = 25.0;

        /// <summary>温度上升系数（每 W·s 损耗的温升 °C）</summary>
        public double ThermalCoefficient { get; set; } = 0.0008;

        /// <summary>散热系数（每秒散失到环境的温差比例）</summary>
        public double CoolingCoefficient { get; set; } = 0.003;

        public BatteryCellSimulator(double initialSoc = 0.5, double nominalCapacity = 2.6,
            double internalResistance = 0.05, double noiseLevel = 0.005)
        {
            Soc = initialSoc;
            NominalCapacity = nominalCapacity;
            InternalResistance = internalResistance;
            _noise = noiseLevel;
            _rng = new Random(Guid.NewGuid().GetHashCode());
        }

        /// <summary>
        /// 通过 SOC 估算 OCV（开路电压）
        /// 使用典型三元锂的 OCV-SOC 曲线（分段拟合）
        /// </summary>
        public double GetOcv(double soc)
        {
            soc = Math.Clamp(soc, 0, 1);
            // 简化 OCV-SOC 曲线：2.8V@0% → 4.2V@100%
            // 采用 sigmoid 形态贴近真实曲线
            if (soc <= 0.05) return 2.8 + (3.3 - 2.8) * (soc / 0.05);
            if (soc <= 0.15) return 3.3 + (3.55 - 3.3) * ((soc - 0.05) / 0.10);
            if (soc <= 0.30) return 3.55 + (3.65 - 3.55) * ((soc - 0.15) / 0.15);
            if (soc <= 0.50) return 3.65 + (3.78 - 3.65) * ((soc - 0.30) / 0.20);
            if (soc <= 0.70) return 3.78 + (3.92 - 3.78) * ((soc - 0.50) / 0.20);
            if (soc <= 0.85) return 3.92 + (4.05 - 3.92) * ((soc - 0.70) / 0.15);
            if (soc <= 0.95) return 4.05 + (4.15 - 4.05) * ((soc - 0.85) / 0.10);
            return 4.15 + (4.20 - 4.15) * ((soc - 0.95) / 0.05);
        }

        /// <summary>
        /// 计算端电压（带载电压）= OCV - I*R （充电时 I>0, 端电压上升）
        /// </summary>
        public double GetTerminalVoltage(double current)
        {
            var ocv = GetOcv(Soc);
            // 充电时电压抬升，放电时电压跌落
            var vTerminal = ocv + current * InternalResistance;
            // 加入小量噪声
            return vTerminal + (_rng.NextDouble() - 0.5) * 2 * _noise;
        }

        /// <summary>
        /// 步进模拟：根据电流和时长更新 SOC、温度
        /// </summary>
        /// <param name="current">实际通过电池的电流 (A)，正充负放</param>
        /// <param name="deltaSeconds">时间增量 (s)</param>
        public void Step(double current, double deltaSeconds)
        {
            if (deltaSeconds <= 0) return;

            // SOC 更新：库仑积分
            var deltaAh = current * deltaSeconds / 3600.0;
            Soc = Math.Clamp(Soc + deltaAh / NominalCapacity, 0, 1);

            // 温升计算：I²R 损耗产生热量
            var powerLoss = current * current * InternalResistance;  // W
            var heatGenerated = powerLoss * deltaSeconds;            // J
            Temperature += ThermalCoefficient * heatGenerated;

            // 散热：与环境温差驱动
            var tempDiff = Temperature - AmbientTemperature;
            Temperature -= CoolingCoefficient * tempDiff * deltaSeconds;

            // 加入小量温度噪声
            Temperature += (_rng.NextDouble() - 0.5) * 0.02;
        }

        /// <summary>
        /// 计算 CV 阶段的电流：随着电压逼近设定值，电流逐渐下降
        /// </summary>
        public double GetCvCurrent(double targetVoltage, double maxCurrent)
        {
            var ocv = GetOcv(Soc);
            // 端电压保持在 targetVoltage，求需要的电流
            // V_target = OCV + I*R  →  I = (V_target - OCV) / R
            var requiredCurrent = (targetVoltage - ocv) / InternalResistance;
            // 限幅
            return Math.Clamp(requiredCurrent, 0, maxCurrent);
        }
    }
}
