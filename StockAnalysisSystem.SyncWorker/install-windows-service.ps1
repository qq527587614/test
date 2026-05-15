#Requires -RunAsAdministrator
<#
 安装 StockAnalysisSystem 盘后同步为 Windows 服务（开机自启）。
 使用前请：
 1) 发布： dotnet publish StockAnalysisSystem.SyncWorker -c Release -r win-x64 --self-contained false
 2) 编辑发布目录下的 appsettings.json，填写 MySql 连接串与 PostMarketSync 时间。
 3) 以管理员运行本脚本，并视情况修改 $PublishDir。
#>
$ErrorActionPreference = "Stop"
$PublishDir = Join-Path $PSScriptRoot "publish"
$Exe = Join-Path $PublishDir "StockAnalysisSystem.SyncWorker.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "未找到 $Exe ，请先 dotnet publish（见脚本注释）。" -ForegroundColor Red
    exit 1
}

$ServiceName = "StockAnalysisSystemPostMarketSync"
$DisplayName = "StockAnalysisSystem 盘后数据同步"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq "Running") { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$Exe`"" `
    -DisplayName $DisplayName `
    -StartupType Automatic `
    -Description "每日盘后同步：腾讯日线快照、财联社涨停、板块成分与板块日线（与程序数据管理一致）。"

Set-Service -Name $ServiceName -StartupType Automatic
Start-Service -Name $ServiceName
Write-Host "已安装并启动服务：$ServiceName" -ForegroundColor Green
