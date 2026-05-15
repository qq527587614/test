# StockAnalysisSystem 用户需求与迭代记录

> 由 Agent 与用户协作维护。新需求写入 Backlog；交付后更新状态与变更流水。  
> 触发维护：见 Cursor Skill `stock-project-requirements-log`。

---

## 约定与口径

| 项 | 说明 |
|----|------|
| 涨跌幅百分点 | 财联社等小数值与行情百分点混用时，统一用 `PercentPointNormalization.ToChangePercentPoints`（参考价消歧）。 |
| 涨停分析盘中 | 日期选「今天」且本地时间 ≤15:00：涨停名单取**上一交易日**；分时与日 K 用**今天**（新浪实时 K 线 + 当日日线）。 |
| 分时质量分 | `LimitUpMinuteQualityAnalyzer`：封板段数、VWAP 上方占比、**分时 MA5** 上方占比、**有量阳线占比 + 尾/首段量比**、日线振幅加权；新浪 1 分钟 `AvgPrice` 为累计 VWAP。 |
| 分时均价 | 新浪 K 线：`AvgPrice` = 累计 VWAP（Σamount/Σvolume），缺 amount 时用收盘×量近似。 |
| 分时图 15:00 后 | 不展示 `MinutesFromStart > 240` 或墙上钟点晚于 15:00 的 K 线。 |
| 热门板块实时选股 | 可选**分析前同步财联社涨停**（与板块分析页同源）；热点优先按**板块分析**口径（涨停→板块映射 + 当日实时/日线均涨幅，板块最少家数可配）；失败则回退题材家数+腾讯涨幅；**剔除 ST 股及 ST 板块、其他/其它等归类与风险警示类题材名**；近 30 自然日涨停且题材命中者；日线位 + 分时质量 + 实时涨跌 + 东财热度启发式排序。 |
| AI 选股 | 菜单 AI→AI选股：可选「生成数据库摘要」（日线最近交易日、指定日涨停合并数、题材 Top、自选股代码）；用户目标与补充说明 + 摘要一并发送 DeepSeek；日志 UsedFor=`AiStockPick`。 |
| 盘后同步 Windows 服务 | `StockAnalysisSystem.SyncWorker`：本地时间过配置时刻（默认 15:05）后每日一次；腾讯当日日线快照、财联社涨停、`PlateService` 板块成分与板块日线；`post-market-sync-state.json` 防重复；详见项目 `README.md`。 |
| 免责声明 | 涨停复盘、板块持续性、次日观察、热门板块实时选股、AI 选股模型输出等均为参考，非投资建议。 |

---

## Backlog

| ID | 日期 | 摘要 | 类型 | 状态 | 说明 |
|----|------|------|------|------|------|
| B001 | 2026-05-12 | 分时图：涨跌幅显示、昨收、00/60 主板 ±10% 涨幅轴、15:00 后异常线 | 优化 | 已完成 | `MinuteChartForm`：规范化涨幅、VWAP 均价、过滤盘后 K 线等 |
| B002 | 2026-05-12 | 涨停分析：涨幅非 0.1% 量级、多日复盘板块持续性/次日观察启发式 | 功能 | 已完成 | `LimitUpReviewService`：`PercentPointNormalization`、持久性、`LimitUpPlatePersistenceRow` 等 |
| B003 | 2026-05-12 | 涨停分析盘中：前一交易日涨停名单 + 当日实时分时评分 | 功能 | 已完成 | `LimitUpReviewService.AnalyzeAsync`：选中今天且本地时间未过 15:00 → `limitUpDate` 为上一交易日、`sessionDate` 为今天；日线/分时过滤与质量分用 `sessionDate`；摘要横幅说明；`LimitUpReviewResult.TradeDate` 用 `sessionDate` |
| B005 | 2026-05-12 | 增加 Skill：记录用户问题为需求文档，改完后更新文档 | 文档 | 已完成 | 项目 Skill `stock-project-requirements-log` + `docs/USER_REQUIREMENTS_LOG.md` |
| B007 | 2026-05-12 | 一级菜单 AI + AI选股（DeepSeek + 本地摘要） | 功能 | 已完成 | `AiStockPickForm`、`DeepSeekClient.ChatCompletionTextAsync`；菜单 AI→AI选股 |
| B006 | 2026-05-12 | 选股：热门板块实时分析选股（可选财联社同步 + 板块分析口径热点 + 30 日涨停成分 + 日 K/分时推荐） | 功能 | 已完成 | `PlateAnalysisPickDataService`、`HotPlateRealtimePickService`、`HotPlateRealtimePickForm`；菜单「选股」→「热门板块实时分析选股」 |
| B008 | 2026-05-13 | Windows 服务：每日盘后自动同步日线快照、涨停、板块数据 | 功能 | 已完成 | `StockAnalysisSystem.SyncWorker`、`install-windows-service.ps1`；与「数据管理」步骤对齐 |

---

## 变更流水（摘要）

| 日期 | 摘要 | 主要路径 |
|------|------|-----------|
| 2026-05-12 | 新增需求记录 Skill 与本文档 | `docs/USER_REQUIREMENTS_LOG.md`, `.cursor/skills/stock-project-requirements-log/SKILL.md` |
| 2026-05-12 | 分时质量分：VWAP + MA5 + 量价 + 尾/首量比（`LimitUpMinuteQualityAnalyzer`） | `LimitUpMinuteQualityAnalyzer.cs`, `LimitUpMinuteQualityAnalyzerTests.cs`, `LimitUpReviewService.cs`, `USER_REQUIREMENTS_LOG.md` |
| 2026-05-12 | 热门板块选股：剔除 ST 股与 ST/其他/风险警示类题材板块 | `HotPlateRealtimePickService.cs`, `HotPlateRealtimePickForm.cs`, `docs/USER_REQUIREMENTS_LOG.md` |
| 2026-05-12 | 选股：热门板块实时分析（B006） | `HotPlateRealtimePickService.cs`, `HotPlateRealtimePickForm.cs`, `MainForm.cs`, `Program.cs`, `ServiceCollectionExtensions.cs` |
| 2026-05-12 | 热门板块选股：先财联社同步涨停，热点按板块分析聚合（`PlateAnalysisPickDataService`），表单暴露同步开关与最少家数 | `PlateAnalysisPickDataService.cs`, `HotPlateRealtimePickService.cs`, `HotPlateRealtimePickForm.cs`, `ServiceCollectionExtensions.cs`, `USER_REQUIREMENTS_LOG.md` |
| 2026-05-12 | 涨停分析盘中模式（B003）+ 持续性文案「名单交易日」 | `LimitUpReviewService.cs` |
| 2026-05-13 | Windows 盘后同步服务（B008）：Worker + 定时 + 安装脚本 | `StockAnalysisSystem.SyncWorker/*`, `StockAnalysisSystem.sln`, `docs/USER_REQUIREMENTS_LOG.md` |
| 2026-05-13 | 数据管理：一键安装/卸载盘后同步 Windows 服务（合并连接串 + UAC） | `DataManagerForm.cs`, `PostMarketSyncServiceInstaller.cs`, `StockAnalysisSystem.UI.csproj` |

---

## 待你补充

（可把产品优先级、接口账号范围、发布节奏写在这里。）
