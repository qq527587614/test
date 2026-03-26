using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockAnalysisSystem.Core;
using StockAnalysisSystem.Core.Entities;

namespace StockAnalysisSystem.Tests;

/// <summary>
/// 测试基类，提供通用测试基础设施
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly IServiceProvider _serviceProvider;
    protected readonly AppDbContext _inMemoryDbContext;
    protected bool _disposed = false;

    protected TestBase()
    {
        // 创建 In-Memory 数据库上下文
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _inMemoryDbContext = new AppDbContext(options);

        // 创建服务提供者
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // 可以在这里添加测试所需的服务
    }

    /// <summary>
    /// 创建 Mock 对象
    /// </summary>
    protected Mock<T> CreateMock<T>() where T : class
    {
        return new Mock<T>();
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    public virtual void Dispose()
    {
        if (!_disposed)
        {
            _inMemoryDbContext?.Database.EnsureDeleted();
            _inMemoryDbContext?.Dispose();
            _disposed = true;
        }
    }
}
