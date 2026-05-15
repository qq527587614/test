using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.SyncWorker;

// Windows 服务进程工作目录常为 System32，必须固定到 exe 目录以便加载 appsettings.json
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddStockAnalysisServices(builder.Configuration);
builder.Services.Configure<PostMarketSyncOptions>(
    builder.Configuration.GetSection(PostMarketSyncOptions.SectionName));
builder.Services.AddHostedService<PostMarketSyncWorker>();

builder.Services.AddWindowsService(o =>
{
    o.ServiceName = "StockAnalysisSystemPostMarketSync";
});

if (OperatingSystem.IsWindows())
{
    try
    {
        builder.Logging.AddEventLog(settings =>
        {
            settings.SourceName = "StockAnalysisSystem.SyncWorker";
            settings.LogName = "Application";
        });
    }
    catch
    {
        // 无权限注册事件源时忽略，仍可使用文件日志
    }
}

var host = builder.Build();
host.Run();
