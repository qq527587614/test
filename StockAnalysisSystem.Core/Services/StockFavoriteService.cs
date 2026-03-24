using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Repositories;
using StockAnalysisSystem.Core.RealtimeData;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 自选股服务
/// </summary>
public class StockFavoriteService
{
    private readonly IStockFavoriteRepository _favoriteRepository;
    private readonly IStockRepository _stockRepository;
    private readonly TencentRealtimeService _realtimeService;

    public StockFavoriteService(
        IStockFavoriteRepository favoriteRepository,
        IStockRepository stockRepository,
        TencentRealtimeService realtimeService)
    {
        _favoriteRepository = favoriteRepository;
        _stockRepository = stockRepository;
        _realtimeService = realtimeService;
    }

    /// <summary>
    /// 获取所有自选股
    /// </summary>
    public async Task<List<StockFavorite>> GetAllFavoritesAsync()
    {
        return await _favoriteRepository.GetAllAsync();
    }

    /// <summary>
    /// 添加自选股
    /// </summary>
    /// <param name="stockCode">股票代码（支持6位或带sz/sh前缀）</param>
    /// <param name="remark">备注</param>
    /// <returns>操作结果消息</returns>
    public async Task<string> AddFavoriteAsync(string stockCode, string? remark = null)
    {
        // 标准化股票代码
        var normalizedCode = NormalizeStockCode(stockCode);

        // 检查是否已存在
        if (await _favoriteRepository.ExistsAsync(normalizedCode))
        {
            return "股票已在自选股中";
        }

        // 验证股票是否存在
        var stock = await _stockRepository.GetByCodeAsync(normalizedCode);
        if (stock == null)
        {
            return "股票不存在，请先同步股票数据";
        }

        // 添加自选股
        var favorite = new StockFavorite
        {
            StockCode = normalizedCode,
            AddedDate = DateTime.Now,
            Remark = remark
        };

        await _favoriteRepository.AddAsync(favorite);
        return "添加成功";
    }

    /// <summary>
    /// 移除自选股
    /// </summary>
    /// <param name="stockCode">股票代码</param>
    public async Task RemoveFavoriteAsync(string stockCode)
    {
        var normalizedCode = NormalizeStockCode(stockCode);
        await _favoriteRepository.DeleteAsync(normalizedCode);
    }

    /// <summary>
    /// 获取带实时行情的自选股列表
    /// </summary>
    public async Task<List<StockFavorite>> GetFavoritesWithRealtimeDataAsync()
    {
        var favorites = await _favoriteRepository.GetAllAsync();

        if (favorites.Count == 0)
        {
            return favorites;
        }

        // 构建股票代码列表（带前缀）
        var stockCodes = new List<string>();
        foreach (var f in favorites)
        {
            var code6 = NormalizeStockCode(f.StockCode);
            var prefix = code6.StartsWith("60") || code6.StartsWith("68") ? "sh" : "sz";
            stockCodes.Add($"{prefix}{code6}");
        }

        // 批量获取实时数据
        var realtimeData = await _realtimeService.GetRealtimeDataAsync(stockCodes);

        // 创建实时数据映射
        var realtimeMap = new Dictionary<string, RealtimeStockData>();
        foreach (var rd in realtimeData)
        {
            var code = rd.StockCode;
            if (code.StartsWith("sz") || code.StartsWith("sh"))
            {
                code = code.Substring(2);
            }
            realtimeMap[code] = rd;
        }

        // 合并数据
        foreach (var favorite in favorites)
        {
            var code6 = NormalizeStockCode(favorite.StockCode);
            if (realtimeMap.TryGetValue(code6, out var rd))
            {
                favorite.StockName = rd.StockName;
                favorite.CurrentPrice = rd.CurrentPrice;
                favorite.ChangePercent = rd.ChangePercent;
                favorite.TurnoverRate = rd.TurnoverRate;
            }
        }

        return favorites;
    }

    /// <summary>
    /// 标准化股票代码（统一为6位数字）
    /// </summary>
    private string NormalizeStockCode(string stockCode)
    {
        if (string.IsNullOrEmpty(stockCode))
            return "";

        // 去掉前缀
        var code = stockCode.Replace("sz", "").Replace("sh", "").Trim();
        return code.PadLeft(6, '0');
    }
}
