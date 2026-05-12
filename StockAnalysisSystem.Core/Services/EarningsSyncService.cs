using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;

namespace StockAnalysisSystem.Core.Services;

/// <summary>
/// 业绩数据同步（业绩报表/财务摘要）。
/// 当前实现：东方财富数据中心 datacenter-web（reportName=RPT_LICO_FN_CPD）。
/// </summary>
public sealed class EarningsSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

    public EarningsSyncService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://data.eastmoney.com/");
    }

    public sealed record EarningsSyncResult
    {
        public int OkPeriods { get; init; }
        public int InsertedRows { get; init; }
        public int UpdatedRows { get; init; }
        public int FailedPeriods { get; init; }
        public List<DateTime> Periods { get; init; } = new();
    }

    /// <summary>
    /// 同步最近 N 个报告期（季度末日期）：例如 2025-12-31、2025-09-30…
    /// </summary>
    public async Task<EarningsSyncResult> SyncRecentReportPeriodsAsync(
        int periodCount = 8,
        DateTime? asOfDate = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = (asOfDate ?? DateTime.Today).Date;
        periodCount = Math.Clamp(periodCount, 1, 40);
        var periods = BuildRecentQuarterEnds(asOf, periodCount);

        var ok = 0;
        var failed = 0;
        var inserted = 0;
        var updated = 0;

        using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        for (var i = 0; i < periods.Count; i++)
        {
            var p = periods[i].Date;
            progress?.Report($"同步业绩：报告期 {p:yyyy-MM-dd}（{i + 1}/{periods.Count}）…");
            try
            {
                var rows = await FetchOnePeriodAsync(p, progress, cancellationToken);
                rows = SanitizeAndDedupe(rows, progress);
                if (rows.Count == 0)
                {
                    ok++;
                    continue;
                }

                // 批量 upsert（按 uk_stock_report_date）
                foreach (var r in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var existing = await db.StockEarningsReports
                        .FirstOrDefaultAsync(x => x.stock_code == r.stock_code && x.report_date == r.report_date, cancellationToken);
                    if (existing == null)
                    {
                        db.StockEarningsReports.Add(r);
                        inserted++;
                    }
                    else
                    {
                        existing.stock_name = r.stock_name;
                        existing.notice_date = r.notice_date;
                        existing.report_date = r.report_date;
                        existing.revenue = r.revenue;
                        existing.revenue_yoy = r.revenue_yoy;
                        existing.net_profit = r.net_profit;
                        existing.net_profit_yoy = r.net_profit_yoy;
                        existing.net_profit_after_nrgal = r.net_profit_after_nrgal;
                        existing.eps = r.eps;
                        existing.roe = r.roe;
                        existing.revenue_qoq = r.revenue_qoq;
                        existing.net_profit_qoq = r.net_profit_qoq;
                        existing.deduct_basic_eps = r.deduct_basic_eps;
                        existing.bps = r.bps;
                        existing.eps_operating_cf = r.eps_operating_cf;
                        existing.gross_margin = r.gross_margin;
                        existing.trade_market = r.trade_market;
                        existing.security_type = r.security_type;
                        existing.org_code = r.org_code;
                        existing.board_name = r.board_name;
                        existing.board_code = r.board_code;
                        existing.qdate = r.qdate;
                        existing.period_label = r.period_label;
                        existing.datayear = r.datayear;
                        existing.publish_name = r.publish_name;
                        existing.update_date_api = r.update_date_api;
                        existing.updated_time = DateTime.Now;
                        updated++;
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                ok++;
                progress?.Report($"  完成 {p:yyyy-MM-dd}：{rows.Count} 条（insert={inserted} update={updated}）");
            }
            catch (Exception ex)
            {
                failed++;
                ErrorLogger.Log(ex, "EarningsSyncService.SyncRecentReportPeriodsAsync", new { period = p });
                progress?.Report($"  失败 {p:yyyy-MM-dd}：{FormatExceptionChain(ex)}");
            }
        }

        progress?.Report($"业绩同步结束：成功期数={ok} 失败期数={failed} 新增={inserted} 更新={updated}");
        return new EarningsSyncResult
        {
            OkPeriods = ok,
            FailedPeriods = failed,
            InsertedRows = inserted,
            UpdatedRows = updated,
            Periods = periods
        };
    }

    private static List<DateTime> BuildRecentQuarterEnds(DateTime asOf, int count)
    {
        // 找到 asOf 之前最近一个季度末
        DateTime QuarterEnd(DateTime d)
        {
            var y = d.Year;
            var m = d.Month;
            if (m <= 3) return new DateTime(y - 1, 12, 31);
            if (m <= 6) return new DateTime(y, 3, 31);
            if (m <= 9) return new DateTime(y, 6, 30);
            return new DateTime(y, 9, 30);
        }

        var cur = QuarterEnd(asOf);
        var list = new List<DateTime>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(cur);
            cur = cur.Month switch
            {
                12 => new DateTime(cur.Year, 9, 30),
                9 => new DateTime(cur.Year, 6, 30),
                6 => new DateTime(cur.Year, 3, 31),
                3 => new DateTime(cur.Year - 1, 12, 31),
                _ => cur.AddMonths(-3)
            };
        }
        return list;
    }

    /// <summary>
    /// 同一报告期接口可能返回重复代码；同一 SaveChanges 内重复插入会触发唯一索引冲突。
    /// 同时对字段长度、DECIMAL 精度做裁剪，避免 MySQL 写入失败。
    /// 去重键为规范化后的 stock_code（默认对应接口 SECURITY_CODE）；同一代码多条（快报/正式/修订）按公告日择优合并。
    /// </summary>
    private static List<StockEarningsReport> SanitizeAndDedupe(List<StockEarningsReport> rows, IProgress<string>? progress)
    {
        // decimal(20,2) / decimal(10,4) 安全范围（略保守）
        const decimal maxMoney = 999999999999999999.99m;
        const decimal maxPct = 999999.9999m;

        static decimal? ClampMoney(decimal? v)
        {
            if (!v.HasValue) return null;
            var x = decimal.Round(v.Value, 2, MidpointRounding.AwayFromZero);
            if (x > maxMoney) return maxMoney;
            if (x < -maxMoney) return -maxMoney;
            return x;
        }

        static decimal? ClampPct(decimal? v)
        {
            if (!v.HasValue) return null;
            var x = decimal.Round(v.Value, 4, MidpointRounding.AwayFromZero);
            if (x > maxPct) return maxPct;
            if (x < -maxPct) return -maxPct;
            return x;
        }

        static decimal? ClampEps(decimal? v)
        {
            if (!v.HasValue) return null;
            var x = decimal.Round(v.Value, 6, MidpointRounding.AwayFromZero);
            const decimal maxE = 999999.999999m;
            if (x > maxE) return maxE;
            if (x < -maxE) return -maxE;
            return x;
        }

        var rawIn = rows.Count;
        var skippedEmptyCode = 0;
        var map = new Dictionary<string, StockEarningsReport>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            r.stock_code = NormalizeStockCode(r.stock_code);
            if (string.IsNullOrEmpty(r.stock_code))
            {
                skippedEmptyCode++;
                continue;
            }

            if (!string.IsNullOrEmpty(r.stock_name) && r.stock_name.Length > 100)
                r.stock_name = r.stock_name[..100];

            r.trade_market = Truncate(r.trade_market, 64);
            r.security_type = Truncate(r.security_type, 64);
            r.org_code = Truncate(r.org_code, 32);
            r.board_name = Truncate(r.board_name, 128);
            r.board_code = Truncate(r.board_code, 32);
            r.qdate = Truncate(r.qdate, 16);
            r.period_label = Truncate(r.period_label, 64);
            r.datayear = Truncate(r.datayear, 8);
            r.publish_name = Truncate(r.publish_name, 128);

            r.revenue = ClampMoney(r.revenue);
            r.net_profit = ClampMoney(r.net_profit);
            r.net_profit_after_nrgal = ClampMoney(r.net_profit_after_nrgal);
            r.revenue_yoy = ClampPct(r.revenue_yoy);
            r.net_profit_yoy = ClampPct(r.net_profit_yoy);
            r.roe = ClampPct(r.roe);
            r.eps = ClampEps(r.eps);
            r.revenue_qoq = ClampPct(r.revenue_qoq);
            r.net_profit_qoq = ClampPct(r.net_profit_qoq);
            r.deduct_basic_eps = ClampEps(r.deduct_basic_eps);
            r.bps = ClampEps(r.bps);
            r.eps_operating_cf = ClampEps(r.eps_operating_cf);
            r.gross_margin = ClampPct(r.gross_margin);

            if (!map.TryGetValue(r.stock_code, out var existing))
                map[r.stock_code] = r;
            else
                map[r.stock_code] = PickBetterEarningsRow(existing, r);
        }

        var deduped = map.Values.ToList();
        var mergedDupes = rawIn - skippedEmptyCode - deduped.Count;
        if (mergedDupes > 0 || skippedEmptyCode > 0)
            progress?.Report($"  去重说明：拉取{rawIn}条 → 空代码跳过{skippedEmptyCode} → 唯一证券{deduped.Count}条（同一证券合并约{mergedDupes}条，多为多次披露/修订）");

        return deduped;
    }

    private static string? Truncate(string? s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var t = s.Trim();
        return t.Length > maxLen ? t[..maxLen] : t;
    }

    /// <summary>同一证券多条记录时：优先公告日更新；其次财务字段更完整。</summary>
    private static StockEarningsReport PickBetterEarningsRow(StockEarningsReport a, StockEarningsReport b)
    {
        static int MetricScore(StockEarningsReport r)
        {
            var n = 0;
            if (r.revenue.HasValue) n++;
            if (r.net_profit.HasValue) n++;
            if (r.net_profit_after_nrgal.HasValue) n++;
            if (r.eps.HasValue) n++;
            if (r.roe.HasValue) n++;
            if (r.revenue_yoy.HasValue) n++;
            if (r.net_profit_yoy.HasValue) n++;
            if (r.revenue_qoq.HasValue) n++;
            if (r.net_profit_qoq.HasValue) n++;
            if (r.deduct_basic_eps.HasValue) n++;
            if (r.bps.HasValue) n++;
            if (r.eps_operating_cf.HasValue) n++;
            if (r.gross_margin.HasValue) n++;
            return n;
        }

        var da = a.notice_date;
        var db = b.notice_date;
        if (da.HasValue && db.HasValue)
        {
            var cmp = db.Value.CompareTo(da.Value);
            if (cmp != 0)
                return cmp > 0 ? b : a;
        }
        else if (db.HasValue && !da.HasValue)
            return b;
        else if (da.HasValue && !db.HasValue)
            return a;

        var sa = MetricScore(a);
        var sb = MetricScore(b);
        if (sb != sa)
            return sb > sa ? b : a;

        return b;
    }

    private static string NormalizeStockCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "";
        var c = code.Trim();
        if (c.Length > 30)
            c = c[..30];
        if (c.All(char.IsDigit) && c.Length > 0 && c.Length <= 6)
            return c.PadLeft(6, '0');
        return c;
    }

    private static string FormatExceptionChain(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0)
                sb.Append(" → ");
            sb.Append(e.Message);
        }

        if (ex is DbUpdateException dbEx && dbEx.Entries.Count > 0)
        {
            sb.Append(" | Entries=");
            sb.Append(string.Join(",", dbEx.Entries.Select(en => en.Entity.GetType().Name)));
        }

        return sb.ToString();
    }

    private static string BuildUrl(DateTime reportDate, int pageNumber, int pageSize)
    {
        // 东方财富 datacenter-web：业绩报表
        // reportName=RPT_LICO_FN_CPD  filter 用 REPORTDATE
        var rd = reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var filter = Uri.EscapeDataString($"(REPORTDATE='{rd}')");
        // 次要排序：大量记录 NOTICE_DATE 相同，仅按公告日排序时分页顺序不稳定，可能出现跨页重复/漏行。
        // NOTICE_DATE 降序 + SECURITY_CODE 升序，形成尽量稳定的总分页顺序（服务端支持多列排序）。
        return "https://datacenter-web.eastmoney.com/api/data/v1/get" +
               $"?reportName=RPT_LICO_FN_CPD&columns=ALL&pageNumber={pageNumber}&pageSize={pageSize}" +
               $"&sortColumns=NOTICE_DATE,SECURITY_CODE&sortTypes=-1,1&filter={filter}";
    }

    private static async Task<List<StockEarningsReport>> FetchOnePeriodAsync(
        DateTime reportDate,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var all = new List<StockEarningsReport>(capacity: 6000);
        const int pageSize = 500;
        var page = 1;
        var printedSchemaHint = false;
        int? apiReportedTotal = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = BuildUrl(reportDate, page, pageSize);
            string json;
            try
            {
                json = await _http.GetStringAsync(url, cancellationToken);
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(ex, "EarningsSyncService.FetchOnePeriodAsync", url);
                progress?.Report($"  拉取失败：{reportDate:yyyy-MM-dd} 第{page}页 请求异常：{ex.Message}");
                break;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                progress?.Report($"  拉取失败：{reportDate:yyyy-MM-dd} 第{page}页 返回空内容");
                break;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (Exception ex)
            {
                var snippet = json.Length > 200 ? json[..200] : json;
                progress?.Report($"  拉取失败：{reportDate:yyyy-MM-dd} 第{page}页 JSON解析失败：{ex.Message} | {snippet}");
                ErrorLogger.Log(ex, "EarningsSyncService.FetchOnePeriodAsync.JsonParse", new { url, snippet });
                break;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                {
                    var snippet = json.Length > 200 ? json[..200] : json;
                    progress?.Report($"  拉取失败：{reportDate:yyyy-MM-dd} 第{page}页 result缺失/异常 | {snippet}");
                    break;
                }

                // 仅提示一次，便于判断接口返回的总量是否符合预期
                if (page == 1)
                {
                    var totalCount = result.TryGetProperty("count", out var cntEl) && cntEl.ValueKind == JsonValueKind.Number && cntEl.TryGetInt32(out var total)
                        ? total
                        : (int?)null;
                    var pages = result.TryGetProperty("pages", out var pagesEl) && pagesEl.ValueKind == JsonValueKind.Number && pagesEl.TryGetInt32(out var ps)
                        ? ps
                        : (int?)null;
                    if (totalCount.HasValue || pages.HasValue)
                        progress?.Report($"  接口统计：count={totalCount?.ToString() ?? "?"} pages={pages?.ToString() ?? "?"} pageSize={pageSize}");
                    apiReportedTotal = totalCount;
                }
                if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    var snippet = json.Length > 200 ? json[..200] : json;
                    progress?.Report($"  拉取失败：{reportDate:yyyy-MM-dd} 第{page}页 data缺失/异常 | {snippet}");
                    break;
                }

                var rawCount = data.GetArrayLength();
                var added = 0;
                foreach (var row in data.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!printedSchemaHint)
                    {
                        var secuCode = GetString(row, "SECUCODE");
                        var rawCode = GetString(row, "SECURITY_CODE");
                        var hasSecuCode = row.TryGetProperty("SECUCODE", out _);
                        var hasRevenueYoy = row.TryGetProperty("YSTZ", out _)
                                            || row.TryGetProperty("TOTAL_OPERATE_INCOME_YOY", out _);
                        var hasProfitYoy = row.TryGetProperty("SJLTZ", out _)
                                           || row.TryGetProperty("PARENT_NETPROFIT_YOY", out _);
                        var hasRoe = row.TryGetProperty("WEIGHTAVG_ROE", out _)
                                     || row.TryGetProperty("ROEWEIGHTED", out _);
                        var hasDeductAmt = row.TryGetProperty("DEDUCT_PARENT_NETPROFIT", out _)
                                           || row.TryGetProperty("PARENT_NETPROFIT_DEDUCT", out _);

                        if (!hasRevenueYoy || !hasProfitYoy || !hasRoe || !hasDeductAmt)
                        {
                            var hint = string.Join(",",
                                row.EnumerateObject()
                                    .Select(p => p.Name)
                                    .Where(n => n.Contains("YOY", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("YSTZ", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("SJLTZ", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("TZ", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("HZ", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("ROE", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("DEDUCT", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("NETPROFIT", StringComparison.OrdinalIgnoreCase)
                                                || n.Contains("OPERATE", StringComparison.OrdinalIgnoreCase))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                    .Take(80));

                            if (!string.IsNullOrWhiteSpace(hint))
                                progress?.Report($"  字段线索(仅一次)：{hint}");
                        }

                        progress?.Report($"  字段检查(仅一次)：hasSECUCODE={hasSecuCode} sampleSECUCODE={secuCode ?? "(null)"} sampleSECURITY_CODE={rawCode ?? "(null)"}");

                        printedSchemaHint = true;
                    }

                    var mapped = TryMapEastMoneyRow(row, reportDate.Date);
                    if (mapped == null)
                        continue;

                    all.Add(mapped);
                    added++;
                }

                progress?.Report($"  拉取 {reportDate:yyyy-MM-dd} 第{page}页：接口{rawCount}条/入库{added}条");

                // 注意：不能用 added 与 pageSize 比较，否则“跳过了无代码行”会导致提前停止翻页
                if (rawCount < pageSize)
                    break;
                page++;
                if (page > 200) // 安全阈值
                    break;
            }
        }

        if (apiReportedTotal.HasValue || all.Count > 0)
        {
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in all)
            {
                var k = NormalizeStockCode(r.stock_code);
                if (!string.IsNullOrEmpty(k))
                    distinct.Add(k);
            }

            string anomaly = "";
            if (apiReportedTotal.HasValue)
            {
                if (all.Count > apiReportedTotal.Value + 50)
                    anomaly = " | 提示：累计原始行明显高于接口count，可能存在分页重叠或count口径不同";
                else if (all.Count > 0 && all.Count < apiReportedTotal.Value - 50)
                    anomaly = " | 提示：累计原始行明显低于接口count，可能中途失败或未拉全";
            }

            progress?.Report($"  拉取汇总 {reportDate:yyyy-MM-dd}：接口count={apiReportedTotal?.ToString() ?? "?"} 累计原始行={all.Count} 不同SECURITY_CODE={distinct.Count}{anomaly}");
        }

        return all;
    }

    /// <summary>将东方财富 RPT_LICO_FN_CPD 单行 JSON 映射为实体（供同步与单测复用）。</summary>
    internal static StockEarningsReport? TryMapEastMoneyRow(JsonElement row, DateTime reportDateFallback)
    {
        // 业务主键/去重：以 SECURITY_CODE（证券代码）为准；与 uk_stock_report_date 一致。
        // 仅当接口未给 SECURITY_CODE 时，才退回 SECUCODE（如部分新三板/港股样式）。
        var rawCode = GetString(row, "SECURITY_CODE");
        var secuCode = GetString(row, "SECUCODE");
        var code = !string.IsNullOrWhiteSpace(rawCode)
            ? rawCode.Trim()
            : (!string.IsNullOrWhiteSpace(secuCode) ? secuCode.Trim() : null);
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var name = GetString(row, "SECURITY_NAME_ABBR");
        var notice = GetDate(row, "NOTICE_DATE");
        var reportFromApi = GetDate(row, "REPORTDATE");
        var reportDate = (reportFromApi ?? reportDateFallback).Date;

        return new StockEarningsReport
        {
            stock_code = code,
            stock_name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            report_date = reportDate,
            notice_date = notice,
            revenue = GetDecimal(row, "TOTAL_OPERATE_INCOME"),
            revenue_yoy = GetDecimalAny(row,
                "YSTZ",
                "TOTAL_OPERATE_INCOME_YOY",
                "TOTAL_OPERATE_INCOME_YOY_RATIO",
                "TOTAL_OPERATE_INCOME_YOY_RATE",
                "TOTAL_OPERATE_INCOME_YOYRT"),
            net_profit = GetDecimal(row, "PARENT_NETPROFIT"),
            net_profit_yoy = GetDecimalAny(row,
                "SJLTZ",
                "PARENT_NETPROFIT_YOY",
                "PARENT_NETPROFIT_YOY_RATIO",
                "PARENT_NETPROFIT_YOY_RATE",
                "PARENT_NETPROFIT_YOYRT"),
            net_profit_after_nrgal = GetDecimalAny(row,
                "DEDUCT_PARENT_NETPROFIT",
                "PARENT_NETPROFIT_DEDUCT",
                "KFJLR",
                "NETPROFIT_DEDUCT",
                "DEDUCT_PARENT_NETPROFIT_ADJ"),
            eps = GetDecimal(row, "BASIC_EPS"),
            roe = GetDecimalAny(row,
                "WEIGHTAVG_ROE",
                "ROEWEIGHTED"),
            revenue_qoq = GetDecimal(row, "YSHZ"),
            net_profit_qoq = GetDecimal(row, "SJLHZ"),
            deduct_basic_eps = GetDecimal(row, "DEDUCT_BASIC_EPS"),
            bps = GetDecimal(row, "BPS"),
            eps_operating_cf = GetDecimal(row, "MGJYXJJE"),
            gross_margin = GetDecimal(row, "XSMLL"),
            trade_market = GetString(row, "TRADE_MARKET"),
            security_type = GetString(row, "SECURITY_TYPE"),
            org_code = GetString(row, "ORG_CODE"),
            board_name = GetStringAny(row, "BOARD_NAME", "PUBLISHNAME"),
            board_code = GetString(row, "BOARD_CODE"),
            qdate = GetString(row, "QDATE"),
            period_label = GetString(row, "DATATYPE"),
            datayear = GetString(row, "DATAYEAR"),
            publish_name = GetString(row, "PUBLISHNAME"),
            update_date_api = GetDateTime(row, "UPDATE_DATE"),
            created_time = DateTime.Now,
            updated_time = DateTime.Now
        };
    }

    private static string? GetStringAny(JsonElement row, params string[] names)
    {
        foreach (var n in names)
        {
            var s = GetString(row, n);
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }

        return null;
    }

    /// <summary>解析带时间的日期（如 UPDATE_DATE）。</summary>
    private static DateTime? GetDateTime(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var ms))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var n) ? n.ToString(CultureInfo.InvariantCulture) : el.GetRawText(),
            _ => null
        };
    }

    private static DateTime? GetDate(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        // 毫秒时间戳
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var ms))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).Date;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
            return d;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    private static decimal? GetDecimalAny(JsonElement row, params string[] names)
    {
        foreach (var n in names)
        {
            var v = GetDecimal(row, n);
            if (v.HasValue)
                return v;
        }

        return null;
    }
}

