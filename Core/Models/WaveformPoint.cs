namespace BatteryAging.Core.Models
{
    /// <summary>
    /// 工况仿真波形点：相对工步起始的时间(s) + 目标电流(A，正充负放)
    /// </summary>
    public class WaveformPoint
    {
        public double TimeSeconds { get; set; }
        public double Current { get; set; }
    }
}
