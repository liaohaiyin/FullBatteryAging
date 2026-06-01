using Microsoft.EntityFrameworkCore;
using BatteryAging.Core.Models;

namespace BatteryAging.Data.Context
{
    public class BatteryDbContext : DbContext
    {
        public BatteryDbContext(DbContextOptions<BatteryDbContext> options) : base(options) { }

        public DbSet<TestRecipe> TestRecipes { get; set; }
        public DbSet<TestStep> TestSteps { get; set; }
        public DbSet<TestRecord> TestRecords { get; set; }
        public DbSet<DataPoint> DataPoints { get; set; }
        public DbSet<Cabinet> Cabinets { get; set; }
        public DbSet<CycleData> CycleData { get; set; }
        public DbSet<DcirResult> DcirResults { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestRecipe>(e =>
            {
                e.HasKey(r => r.Id);
                e.Property(r => r.Name).HasMaxLength(100);
                e.HasMany(r => r.Steps).WithOne().OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TestStep>(e =>
            {
                e.HasKey(s => s.Id);
                e.Property(s => s.Name).HasMaxLength(100);
                e.Property<string>("TestRecipeId");
            });

            modelBuilder.Entity<TestRecord>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => r.StartTime);
                e.HasIndex(r => r.BarCode);
                e.HasIndex(r => r.ChannelIndex);
                e.HasIndex(r => r.Status);
            });

            modelBuilder.Entity<DataPoint>(e =>
            {
                e.HasKey(d => d.Id);
                e.HasIndex(d => new { d.TestRecordId, d.Timestamp });
            });

            modelBuilder.Entity<Cabinet>(e =>
            {
                e.HasKey(c => c.Id);
                e.Property(c => c.Name).HasMaxLength(100);
                e.HasIndex(c => c.CabinetIndex);
            });

            modelBuilder.Entity<CycleData>(e =>
            {
                e.HasKey(c => c.Id);
                e.HasIndex(c => new { c.TestRecordId, c.CycleIndex });
            });

            modelBuilder.Entity<DcirResult>(e => {
                e.HasKey(d => d.Id);
                e.HasIndex(d => new { d.TestRecordId, d.ChannelIndex });
                e.Ignore(d => d.ResistanceByTime);       // 字典不直接映射
                e.Ignore(d => d.Resistance);
                e.Property<string>("ResistanceJson");     // 用 JSON 列存字典
            });
            base.OnModelCreating(modelBuilder);
        }
    }
}
