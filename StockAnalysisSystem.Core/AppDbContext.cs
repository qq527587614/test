using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using DailyPickEntity = StockAnalysisSystem.Core.Entities.DailyPick;

namespace StockAnalysisSystem.Core;

/// <summary>
/// 股票分析系统数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // 现有表
    public DbSet<StockInfo> StockInfos { get; set; }
    public DbSet<StockDailyData> StockDailyData { get; set; }
    public DbSet<StockLimitUpAnalysis> StockLimitUpAnalysis { get; set; }

    // 板块相关表
    public DbSet<Plate> Plates { get; set; }
    public DbSet<PlateStock> PlateStocks { get; set; }
    public DbSet<PlateDailyData> PlateDailyData { get; set; }

    // 新增表
    public DbSet<Strategy> Strategies { get; set; }
    public DbSet<BacktestTask> BacktestTasks { get; set; }
    public DbSet<OptimizationTask> OptimizationTasks { get; set; }
    public DbSet<DailyPickEntity> DailyPicks { get; set; }
    public DbSet<DeepSeekLog> DeepSeekLogs { get; set; }
    public DbSet<StockDailyIndicator> StockDailyIndicators { get; set; }
    public DbSet<StockFavorite> StockFavorites { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // StockInfo 配置
        modelBuilder.Entity<StockInfo>(entity =>
        {
            entity.HasIndex(e => e.StockCode).HasDatabaseName("idx_stockcode");
            entity.HasIndex(e => e.Industry).HasDatabaseName("idx_industry");
        });

        // StockDailyData 配置
        modelBuilder.Entity<StockDailyData>(entity =>
        {
            entity.HasIndex(e => new { e.StockID, e.TradeDate })
                .IsUnique()
                .HasDatabaseName("uk_stock_date");
            entity.HasIndex(e => e.TradeDate).HasDatabaseName("idx_date");
            entity.HasIndex(e => new { e.StockID, e.TradeDate }).HasDatabaseName("idx_stock_date");
        });

        // StockDailyIndicator 配置
        modelBuilder.Entity<StockDailyIndicator>(entity =>
        {
            entity.HasIndex(e => new { e.StockId, e.TradeDate })
                .IsUnique()
                .HasDatabaseName("uk_stock_date");
            entity.HasIndex(e => e.TradeDate).HasDatabaseName("idx_date");
        });

        // DailyPick 配置
        modelBuilder.Entity<DailyPickEntity>(entity =>
        {
            entity.HasIndex(e => new { e.TradeDate, e.StockId, e.StrategyId })
                .IsUnique()
                .HasDatabaseName("uk_date_stock_strategy");
            entity.HasIndex(e => e.TradeDate).HasDatabaseName("idx_date");
            
            // 配置与Strategy的外键关系
            entity.HasOne(d => d.Strategy)
                .WithMany()
                .HasForeignKey(d => d.StrategyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // BacktestTask 配置
        modelBuilder.Entity<BacktestTask>(entity =>
        {
            entity.HasIndex(e => e.StrategyId).HasDatabaseName("idx_strategy");
        });

        // StockFavorite 配置
        modelBuilder.Entity<StockFavorite>(entity =>
        {
            entity.HasIndex(e => e.StockCode).IsUnique().HasDatabaseName("uk_stockcode");
        });

        // Plate 配置
        modelBuilder.Entity<Plate>(entity =>
        {
            entity.HasIndex(e => e.plate_code).IsUnique().HasDatabaseName("uk_plate_code");
        });

        // PlateStock 配置
        modelBuilder.Entity<PlateStock>(entity =>
        {
            entity.HasIndex(e => new { e.plate_id, e.stock_code }).IsUnique().HasDatabaseName("uk_plate_stock");
        });

        // PlateDailyData 配置
        modelBuilder.Entity<PlateDailyData>(entity =>
        {
            entity.HasIndex(e => new { e.plate_id, e.trade_date }).IsUnique().HasDatabaseName("uk_plate_date");
            entity.HasIndex(e => e.trade_date).HasDatabaseName("idx_trade_date");
        });
    }
}
