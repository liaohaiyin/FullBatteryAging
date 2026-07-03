using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BatteryAging.Core.Models;

namespace BatteryAging.Services
{
    /// <summary>
    /// 正式报表导出（Excel / PDF）—— 补充数据查询页原有的 CSV 导出，
    /// 生成可直接交付客户/存档的机柜-工步-循环信息完整报表。
    /// </summary>
    public interface IReportExportService
    {
        void ExportRecordToExcel(TestRecord record, IReadOnlyList<DataPoint> points, IReadOnlyList<CycleData> cycles, string filePath);
        void ExportRecordToPdf(TestRecord record, IReadOnlyList<DataPoint> points, IReadOnlyList<CycleData> cycles, string filePath);
    }

    public class ReportExportService : IReportExportService
    {
        public ReportExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public void ExportRecordToExcel(TestRecord record, IReadOnlyList<DataPoint> points, IReadOnlyList<CycleData> cycles, string filePath)
        {
            using var wb = new XLWorkbook();

            var info = wb.Worksheets.Add("概要");
            int r = 1;
            void Row(string label, string value)
            {
                info.Cell(r, 1).Value = label;
                info.Cell(r, 2).Value = value;
                r++;
            }
            Row("条码", record.BarCode);
            Row("测试方案", record.RecipeName);
            Row("机柜", record.CabinetId);
            Row("通道", record.ChannelIndex.ToString());
            Row("开始时间", record.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            Row("结束时间", record.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-");
            Row("状态", record.Status.ToString());
            Row("保护触发", record.ProtectionTrigger.ToString());
            Row("总充电容量(Ah)", record.TotalChargeCapacity.ToString("F5"));
            Row("总放电容量(Ah)", record.TotalDischargeCapacity.ToString("F5"));
            Row("总充电能量(Wh)", record.TotalChargeEnergy.ToString("F5"));
            Row("总放电能量(Wh)", record.TotalDischargeEnergy.ToString("F5"));
            Row("完成循环数", record.CompletedCycles.ToString());
            Row("SOH", $"{record.SohEstimate * 100:F2}%");
            Row("容量分档", record.Grade.ToString());
            Row("操作员", record.Operator);
            info.Columns().AdjustToContents();

            var dataSheet = wb.Worksheets.Add("数据点");
            string[] headers = { "时间", "通道", "工步", "工步类型", "循环", "累计秒", "电压V", "电流A", "容量Ah", "能量Wh", "温度C", "SOC%" };
            for (int c = 0; c < headers.Length; c++) dataSheet.Cell(1, c + 1).Value = headers[c];
            int row = 2;
            foreach (var p in points)
            {
                dataSheet.Cell(row, 1).Value = p.Timestamp;
                dataSheet.Cell(row, 2).Value = p.ChannelIndex;
                dataSheet.Cell(row, 3).Value = p.StepSequence;
                dataSheet.Cell(row, 4).Value = p.StepType.ToString();
                dataSheet.Cell(row, 5).Value = p.LoopIndex;
                dataSheet.Cell(row, 6).Value = p.ElapsedSeconds;
                dataSheet.Cell(row, 7).Value = p.Voltage;
                dataSheet.Cell(row, 8).Value = p.Current;
                dataSheet.Cell(row, 9).Value = p.Capacity;
                dataSheet.Cell(row, 10).Value = p.Energy;
                dataSheet.Cell(row, 11).Value = p.Temperature;
                dataSheet.Cell(row, 12).Value = p.Soc;
                row++;
            }
            if (points.Count > 0) dataSheet.SheetView.FreezeRows(1);

            if (cycles != null && cycles.Count > 0)
            {
                var cycleSheet = wb.Worksheets.Add("循环数据");
                string[] cHeaders = { "循环", "充电Ah", "放电Ah", "充电Wh", "放电Wh", "库伦效率", "能量效率" };
                for (int c = 0; c < cHeaders.Length; c++) cycleSheet.Cell(1, c + 1).Value = cHeaders[c];
                int cr = 2;
                foreach (var c in cycles)
                {
                    cycleSheet.Cell(cr, 1).Value = c.CycleIndex;
                    cycleSheet.Cell(cr, 2).Value = c.ChargeCapacity;
                    cycleSheet.Cell(cr, 3).Value = c.DischargeCapacity;
                    cycleSheet.Cell(cr, 4).Value = c.ChargeEnergy;
                    cycleSheet.Cell(cr, 5).Value = c.DischargeEnergy;
                    cycleSheet.Cell(cr, 6).Value = c.CoulombicEfficiency;
                    cycleSheet.Cell(cr, 7).Value = c.EnergyEfficiency;
                    cr++;
                }
                cycleSheet.SheetView.FreezeRows(1);
            }

            wb.SaveAs(filePath);
        }

        public void ExportRecordToPdf(TestRecord record, IReadOnlyList<DataPoint> points, IReadOnlyList<CycleData> cycles, string filePath)
        {
            var cycleList = (cycles ?? Array.Empty<CycleData>()).OrderBy(c => c.CycleIndex).ToList();

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontFamily("Microsoft YaHei").FontSize(10));

                    page.Header().Text("电池测试报告").FontSize(18).Bold();

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn(2);
                                c.RelativeColumn();
                                c.RelativeColumn(2);
                            });

                            void Cell(string text, bool label)
                            {
                                var c = table.Cell().Padding(3);
                                if (label) c.Text(text).SemiBold();
                                else c.Text(text ?? "-");
                            }

                            Cell("条码", true); Cell(record.BarCode, false);
                            Cell("测试方案", true); Cell(record.RecipeName, false);
                            Cell("机柜/通道", true); Cell($"{record.CabinetId} / CH{record.ChannelIndex}", false);
                            Cell("状态", true); Cell(record.Status.ToString(), false);
                            Cell("开始时间", true); Cell(record.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), false);
                            Cell("结束时间", true); Cell(record.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-", false);
                            Cell("总充电容量(Ah)", true); Cell(record.TotalChargeCapacity.ToString("F5"), false);
                            Cell("总放电容量(Ah)", true); Cell(record.TotalDischargeCapacity.ToString("F5"), false);
                            Cell("总充电能量(Wh)", true); Cell(record.TotalChargeEnergy.ToString("F5"), false);
                            Cell("总放电能量(Wh)", true); Cell(record.TotalDischargeEnergy.ToString("F5"), false);
                            Cell("SOH", true); Cell($"{record.SohEstimate * 100:F2}%", false);
                            Cell("容量分档", true); Cell(record.Grade.ToString(), false);
                            Cell("保护触发", true); Cell(record.ProtectionTrigger.ToString(), false);
                            Cell("操作员", true); Cell(record.Operator, false);
                        });

                        if (cycleList.Count > 0)
                        {
                            col.Item().PaddingTop(10).Text("循环数据").Bold().FontSize(12);
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                    c.RelativeColumn();
                                });
                                table.Header(header =>
                                {
                                    header.Cell().Text("循环").SemiBold();
                                    header.Cell().Text("放电Ah").SemiBold();
                                    header.Cell().Text("充电Ah").SemiBold();
                                    header.Cell().Text("库伦效率").SemiBold();
                                    header.Cell().Text("能量效率").SemiBold();
                                });
                                foreach (var c in cycleList.Take(300))
                                {
                                    table.Cell().Text(c.CycleIndex.ToString());
                                    table.Cell().Text(c.DischargeCapacity.ToString("F4"));
                                    table.Cell().Text(c.ChargeCapacity.ToString("F4"));
                                    table.Cell().Text(c.CoulombicEfficiency.ToString("F3"));
                                    table.Cell().Text(c.EnergyEfficiency.ToString("F3"));
                                }
                            });
                            if (cycleList.Count > 300)
                                col.Item().Text($"（仅展示前 300 个循环，共 {cycleList.Count} 个）").FontSize(8).Italic();
                        }

                        col.Item().PaddingTop(6).Text($"数据点采样总数: {points?.Count ?? 0}").FontSize(9);
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("生成时间: ");
                        x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    });
                });
            }).GeneratePdf(filePath);
        }
    }
}
