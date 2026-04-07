using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.Entities;
using StockAnalysisSystem.Core.Utils;
using StockAnalysisSystem.UI.Forms;

namespace StockAnalysisSystem.UI;

static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            // 构建配置 - 单文件发布模式下使用 AppContext.BaseDirectory
            var baseDirectory = AppContext.BaseDirectory
                ?? Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(baseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 验证配置
            var connectionString = configuration.GetConnectionString("MySql");
            if (string.IsNullOrEmpty(connectionString))
            {
                MessageBox.Show("数据库连接字符串配置为空，请检查appsettings.json", "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 配置服务
            var services = new ServiceCollection();

            // 添加配置
            services.AddSingleton<IConfiguration>(configuration);

            // 添加数据库上下文和核心服务
            services.AddStockAnalysisServices(configuration);

            // 添加日志
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });

            // 注册窗体
            services.AddTransient<MainForm>();
            services.AddTransient<StrategyManagerForm>();
            services.AddTransient<BacktestForm>();
            services.AddTransient<OptimizationForm>();
            services.AddTransient<DailyPickForm>();
            services.AddTransient<DataManagerForm>();
            services.AddTransient<FavoriteForm>();
            services.AddTransient<PlateAnalysisForm>();
            services.AddTransient<DeepSeekMarketAnalysisForm>();
            services.AddTransient<KLineForm>();

            // 构建服务提供者
            var serviceProvider = services.BuildServiceProvider();

            // 测试数据库连接
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var canConnect = dbContext.Database.CanConnect();
                if (!canConnect)
                {
                    MessageBox.Show("无法连接到数据库，请检查连接字符串和网络连接", "数据库连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库连接失败: {ex.Message}\n\n详细错误: {ex.InnerException?.Message}", "数据库错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 运行主窗体
            Application.Run(serviceProvider.GetRequiredService<MainForm>());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"应用程序启动失败: {ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}", "启动错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
