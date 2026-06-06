using System.Threading;
using System.Threading.Tasks;

namespace BatteryAging.Services.Mes
{
    public interface IMesService
    {
        bool IsEnabled { get; }          // Provider != None
        bool CheckInEnabled { get; }     // 是否需要开测前过站

        Task<MesCheckResult> CheckInAsync(MesCheckRequest req, CancellationToken ct = default);
        Task<MesAck> UploadResultAsync(MesTestResult result, CancellationToken ct = default);
    }
}