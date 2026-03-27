using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Models;
using System.Globalization;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// K线数据服务实现
/// </summary>
public class KLineDataService : IKLineDataService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public KLineDataService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public async Task<List<KLineData>> GetKLineDataAsync(string stockCode, PeriodType period, int count = 500)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        return period switch
        {
            PeriodType.Daily => await GetDailyKLineDataAsync(dbContext, stockCode, count),
            PeriodType.Weekly => await GetWeeklyKLineDataAsync(dbContext, stockCode, count),
            PeriodType.Monthly => await GetMonthlyKLineDataAsync(dbContext, stockCode, count),
            _ => await GetDailyKLineDataAsync(dbContext, stockCode, count)
        };
    }

    /// <summary>
    /// 获取日K线数据
    /// </summary>
    private async Task<List<KLineData>> GetDailyKLineDataAsync(AppDbContext dbContext, string stockCode, int count)
    {
        var dailyData = await dbContext.StockDailyData
            .Where(d => d.StockID == stockCode)
            .OrderByDescending(d => d.TradeDate)
            .Take(count)
            .OrderBy(d => d.TradeDate)
            .ToListAsync();

        return dailyData.Select(d => new KLineData
        {
            Date = d.TradeDate,
            Open = d.OpenPrice,
            High = d.HighPrice,
            Low = d.LowPrice,
            Close = d.ClosePrice,
            Volume = d.Volume
        }).ToList();
    }

    /// <summary>
    /// 获取周K线数据
    /// </summary>
    private async Task<List<KLineData>> GetWeeklyKLineDataAsync(AppDbContext dbContext, string stockCode, int count)
    {
        var dailyData = await dbContext.StockDailyData
            .Where(d => d.StockID == stockCode)
            .OrderByDescending(d => d.TradeDate)
            .Take(count * 7)
            .ToListAsync();

        var weeklyData = dailyData
            .GroupBy(d => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                d.TradeDate,
                CalendarWeekRule.FirstDay,
                DayOfWeek.Monday))
            .Select(g => new KLineData
            {
                Date = g.OrderBy(d => d.TradeDate).First().TradeDate,
                Open = g.OrderBy(d => d.TradeDate).First().OpenPrice,
                High = g.Max(d => d.HighPrice),
                Low = g.Min(d => d.LowPrice),
                Close = g.OrderByDescending(d => d.TradeDate).First().ClosePrice,
                Volume = g.Sum(d => d.Volume)
            })
            .OrderByDescending(d => d.Date)
            .Take(count)
            .OrderBy(d => d.Date)
            .ToList();

        return weeklyData;
    }

    /// <summary>
    /// 获取月K线数据
    /// </summary>
    private async Task<List<KLineData>> GetMonthlyKLineDataAsync(AppDbContext dbContext, string stockCode, int count)
    {
        var dailyData = await dbContext.StockDailyData
            .Where(d => d.StockID == stockCode)
            .OrderByDescending(d => d.TradeDate)
            .Take(count * 30)
            .ToListAsync();

        var monthlyData = dailyData
            .GroupBy(d => new { d.TradeDate.Year, d.TradeDate.Month })
            .Select(g => new KLineData
            {
                Date = g.OrderBy(d => d.TradeDate).First().TradeDate,
                Open = g.OrderBy(d => d.TradeDate).First().OpenPrice,
                High = g.Max(d => d.HighPrice),
                Low = g.Min(d => d.LowPrice),
                Close = g.OrderByDescending(d => d.TradeDate).First().ClosePrice,
                Volume = g.Sum(d => d.Volume)
            })
            .OrderByDescending(d => d.Date)
            .Take(count)
            .OrderBy(d => d.Date)
            .ToList();

        return monthlyData;
    }
}
