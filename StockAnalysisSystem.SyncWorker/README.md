# 盘后数据同步 Windows 服务

在**不打开桌面程序**的情况下，按本地时间每天在配置时刻（默认 **15:05**）之后执行一次与「数据管理」相同的盘后同步：

1. **腾讯实时 → 当日日线快照**（全市场 `StockDailyData` 当日覆盖写入）
2. **财联社当日涨停**（`PlateAnalysisPickDataService`，与板块分析/热门板块选股同源）
3. **板块成分增量**（`PlateService.SyncPlatesFromLimitUpAsync`）
4. **板块日线增量计算**（`PlateService.CalcPlateDailyDataAsync`）

## 配置

编辑输出目录中的 `appsettings.json`：

- `ConnectionStrings:MySql`：与主程序相同的数据库连接串。
- `PostMarketSync`：`RunHour` / `RunMinute`（默认 15、5）、`RunOnlyWeekdays`（默认 true，跳过周六日）、各步骤 `Enable*` 开关。

成功跑完一天后会在 exe 同目录写入 `post-market-sync-state.json`，避免同一天重复执行。日志：`logs/post-market-sync.log`；若本机已注册事件源，还可查看 Windows「应用程序」事件日志。

## 安装服务（管理员 PowerShell）

也可在主程序「**数据管理**」窗口中点击「**安装/重装服务**」，将自动合并数据库连接串并弹出 UAC 完成注册（依赖编译时复制到 `PostMarketSyncService` 目录下的 Worker 文件）。

```powershell
cd StockAnalysisSystem.SyncWorker
dotnet publish -c Release -r win-x64 --self-contained false -o publish
# 编辑 publish\appsettings.json 中的数据库连接串
.\install-windows-service.ps1
```

若脚本中的 `$PublishDir` 与您的发布路径不一致，请修改 `install-windows-service.ps1` 顶部的 `$PublishDir`。

## 卸载

```powershell
Stop-Service StockAnalysisSystemPostMarketSync -Force -ErrorAction SilentlyContinue
sc.exe delete StockAnalysisSystemPostMarketSync
```

## 本机调试

```powershell
dotnet run --project StockAnalysisSystem.SyncWorker
```

将 `RunHour`/`RunMinute` 调到略早于当前时间，或暂时将 `RunOnlyWeekdays` 设为 `false`，便于验证；跑通后改回生产配置。
