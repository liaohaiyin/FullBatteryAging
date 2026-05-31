using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BatteryAging.Core.Enums;
using BatteryAging.Core.Models;
using BatteryAging.Data.Context;

namespace BatteryAging.Services
{
    public interface IDataService
    {
        Task<List<TestRecipe>> GetAllRecipesAsync();
        Task<TestRecipe> GetRecipeAsync(string id);
        Task SaveRecipeAsync(TestRecipe recipe);
        Task DeleteRecipeAsync(string id);

        Task<TestRecord> CreateRecordAsync(TestRecord record);
        Task UpdateRecordAsync(TestRecord record);
        Task UpdateRecordCheckpointAsync(int recordId, int stepIndex, int loopIndex,
            double totalElapsed, double chargeAh, double dischargeAh, double chargeWh, double dischargeWh);
        Task<List<TestRecord>> QueryRecordsAsync(DateTime? start, DateTime? end, int? channel, string barCode);
        Task<List<TestRecord>> GetInterruptedRecordsAsync();
        Task<List<DataPoint>> GetDataPointsAsync(int recordId);
        Task SaveDataPointsAsync(IEnumerable<DataPoint> points);

        // ── Cabinet ──
        Task<List<Cabinet>> GetAllCabinetsAsync();
        Task SaveCabinetAsync(Cabinet cabinet);
        Task DeleteCabinetAsync(string id);

        // ── 分析 ──
        Task<List<TestRecord>> GetRecordsByBarCodePrefixAsync(string barCodePrefix);
        Task SaveCycleDataAsync(CycleData cycle);
        Task<List<CycleData>> GetCycleDataAsync(int recordId);
    }

    public class DataService : IDataService
    {
        private readonly IDbContextFactory<BatteryDbContext> _factory;

        public DataService(IDbContextFactory<BatteryDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<TestRecipe>> GetAllRecipesAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.TestRecipes
                .Include(r => r.Steps.OrderBy(s => s.Sequence))
                .Where(r => r.IsActive)
                .OrderByDescending(r => r.UpdateTime)
                .ToListAsync();
        }

        public async Task<TestRecipe> GetRecipeAsync(string id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.TestRecipes
                .Include(r => r.Steps.OrderBy(s => s.Sequence))
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task SaveRecipeAsync(TestRecipe recipe)
        {
            await using var db = await _factory.CreateDbContextAsync();
            recipe.UpdateTime = DateTime.Now;

            var existing = await db.TestRecipes
                .Include(r => r.Steps.OrderBy(s => s.Sequence))
                .FirstOrDefaultAsync(r => r.Id == recipe.Id);

            if (existing == null)
            {
                foreach (var s in recipe.Steps)
                    if (string.IsNullOrEmpty(s.Id)) s.Id = Guid.NewGuid().ToString();
                db.TestRecipes.Add(recipe);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(recipe);
                db.TestSteps.RemoveRange(existing.Steps);
                await db.SaveChangesAsync();

                foreach (var step in recipe.Steps)
                {
                    step.Id = Guid.NewGuid().ToString();
                    db.Entry(step).Property("TestRecipeId").CurrentValue = recipe.Id;
                    db.TestSteps.Add(step);
                }
            }
            await db.SaveChangesAsync();
        }

        public async Task DeleteRecipeAsync(string id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var recipe = await db.TestRecipes.FindAsync(id);
            if (recipe != null) { recipe.IsActive = false; await db.SaveChangesAsync(); }
        }

        public async Task<TestRecord> CreateRecordAsync(TestRecord record)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.TestRecords.Add(record);
            await db.SaveChangesAsync();
            return record;
        }

        public async Task UpdateRecordAsync(TestRecord record)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.TestRecords.Update(record);
            await db.SaveChangesAsync();
        }

        public async Task UpdateRecordCheckpointAsync(int recordId, int stepIndex, int loopIndex,
            double totalElapsed, double chargeAh, double dischargeAh, double chargeWh, double dischargeWh)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var r = await db.TestRecords.FindAsync(recordId);
            if (r == null) return;
            r.LastStepIndex = stepIndex;
            r.LastLoopIndex = loopIndex;
            r.LastTotalElapsed = totalElapsed;
            r.LastCheckpointTime = DateTime.Now;
            r.TotalChargeCapacity = chargeAh;
            r.TotalDischargeCapacity = dischargeAh;
            r.TotalChargeEnergy = chargeWh;
            r.TotalDischargeEnergy = dischargeWh;
            await db.SaveChangesAsync();
        }

        public async Task<List<TestRecord>> QueryRecordsAsync(
            DateTime? start, DateTime? end, int? channel, string barCode)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var q = db.TestRecords.AsQueryable();
            if (start.HasValue) q = q.Where(r => r.StartTime >= start.Value);
            if (end.HasValue) q = q.Where(r => r.StartTime <= end.Value);
            if (channel.HasValue) q = q.Where(r => r.ChannelIndex == channel.Value);
            if (!string.IsNullOrWhiteSpace(barCode))
                q = q.Where(r => r.BarCode.Contains(barCode));
            return await q.OrderByDescending(r => r.StartTime).Take(500).ToListAsync();
        }

        /// <summary>查询未正常结束的测试记录（用于掉电续测）</summary>
        public async Task<List<TestRecord>> GetInterruptedRecordsAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.TestRecords
                .Where(r => r.Status == ChannelStatus.Running || r.Status == ChannelStatus.Paused)
                .OrderByDescending(r => r.StartTime)
                .ToListAsync();
        }

        public async Task<List<DataPoint>> GetDataPointsAsync(int recordId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.DataPoints
                .Where(d => d.TestRecordId == recordId)
                .OrderBy(d => d.Timestamp)
                .ToListAsync();
        }

        public async Task SaveDataPointsAsync(IEnumerable<DataPoint> points)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.DataPoints.AddRange(points);
            await db.SaveChangesAsync();
        }

        // ──────────────── Cabinet ────────────────
        public async Task<List<Cabinet>> GetAllCabinetsAsync()
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Cabinets.OrderBy(c => c.CabinetIndex).ToListAsync();
        }

        public async Task SaveCabinetAsync(Cabinet cabinet)
        {
            await using var db = await _factory.CreateDbContextAsync();
            cabinet.UpdateTime = DateTime.Now;
            var existing = await db.Cabinets.FindAsync(cabinet.Id);
            if (existing == null) db.Cabinets.Add(cabinet);
            else db.Entry(existing).CurrentValues.SetValues(cabinet);
            await db.SaveChangesAsync();
        }

        public async Task DeleteCabinetAsync(string id)
        {
            await using var db = await _factory.CreateDbContextAsync();
            var c = await db.Cabinets.FindAsync(id);
            if (c != null) { db.Cabinets.Remove(c); await db.SaveChangesAsync(); }
        }

        public async Task<List<TestRecord>> GetRecordsByBarCodePrefixAsync(string barCodePrefix)
        {
            await using var db = await _factory.CreateDbContextAsync();
            if (string.IsNullOrWhiteSpace(barCodePrefix))
                return await db.TestRecords.OrderByDescending(r => r.StartTime).Take(2000).ToListAsync();
            return await db.TestRecords
                .Where(r => r.BarCode.StartsWith(barCodePrefix))
                .OrderByDescending(r => r.StartTime)
                .Take(500).ToListAsync();
        }

        public async Task SaveCycleDataAsync(CycleData cycle)
        {
            await using var db = await _factory.CreateDbContextAsync();
            db.Set<CycleData>().Add(cycle);
            await db.SaveChangesAsync();
        }

        public async Task<List<CycleData>> GetCycleDataAsync(int recordId)
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Set<CycleData>()
                .Where(c => c.TestRecordId == recordId)
                .OrderBy(c => c.CycleIndex)
                .ToListAsync();
        }
    }
}
