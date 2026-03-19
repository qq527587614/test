# 股票分析系统 - 运行和配置说明

## 1. 系统概述

股票分析系统是一个基于C# WinForms和MySQL数据库开发的综合股票分析平台，提供技术指标计算、策略回测、参数优化和AI智能评分等功能。

**技术栈：**
- .NET 8.0
- Entity Framework Core
- MySQL数据库
- WinForms UI
- DeepSeek API（可选）

## 2. 环境要求

### 2.1 运行环境
- Windows 10/11
- .NET 8.0 Runtime
- MySQL 5.7+ 或 MariaDB 10.3+

### 2.2 开发环境
- Visual Studio 2022 或 VS Code
- .NET 8.0 SDK
- Git（可选）

## 3. 安装步骤

### 3.1 从源码编译

```powershell
# 克隆或下载项目代码
cd StockAnalysisSystem

# 还原依赖
dotnet restore

# 编译项目
dotnet build -c Release

# 运行主程序
cd StockAnalysisSystem.UI/bin/Release/net8.0-windows
StockAnalysisSystem.UI.exe
```

### 3.2 数据库初始化

确保MySQL服务正在运行，然后执行初始化脚本：

```sql
-- 执行Scripts/init_database.sql脚本创建表结构
-- 确保数据库gudata已存在，或修改连接字符串中的数据库名
```

## 4. 配置说明

### 4.1 数据库配置

编辑`StockAnalysisSystem.UI/appsettings.json`：

```json
{
  "ConnectionStrings": {
    "MySql": "Server=your-server;Port=3306;Database=gudata;Uid=username;Pwd=password;CharSet=utf8;Connection Timeout=30;Connection Idle Timeout=60;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100;Connection Reset=true;Allow User Variables=true;"
  }
}
```

**连接字符串参数说明：**
- `Server`: MySQL服务器地址
- `Port`: MySQL端口（默认3306）
- `Database`: 数据库名称
- `Uid`: 用户名
- `Pwd`: 密码
- `Connection Timeout`: 连接超时时间（秒）
- `Connection Idle Timeout`: 空闲连接超时时间（秒）
- `Pooling`: 是否启用连接池
- `Minimum Pool Size`: 最小连接池大小
- `Maximum Pool Size`: 最大连接池大小

### 4.2 DeepSeek API配置（可选）

如需使用AI智能评分功能，配置DeepSeek API：

```json
{
  "DeepSeek": {
    "ApiKey": "your-deepseek-api-key",
    "Endpoint": "https://api.deepseek.com/v1/chat/completions",
    "TimeoutSeconds": 30
  }
}
```

**获取API密钥：**
1. 访问 https://platform.deepseek.com/
2. 注册账号并登录
3. 在控制台获取API密钥
4. 将密钥粘贴到配置文件中

### 4.3 回测配置

```json
{
  "Backtest": {
    "DefaultInitialCapital": 1000000,
    "DefaultCommission": 0.00025,
    "DefaultSlippage": 0.001,
    "MaxPositions": 10
  }
}
```

## 5. 功能使用

### 5.1 主界面

程序启动后显示主界面，包含以下菜单：
- **文件**: 退出程序
- **策略管理**: 管理交易策略
- **数据管理**: 导入和查看股票数据
- **回测**: 执行策略回测
- **优化**: 参数优化
- **每日选股**: 使用AI智能选股

### 5.2 策略管理

支持多种技术指标策略：
- MA策略（移动平均线）
- MACD策略
- RSI策略
- KDJ策略
- BOLL策略（布林带）

**操作步骤：**
1. 点击菜单"策略管理"
2. 添加新策略或编辑现有策略
3. 配置策略参数
4. 设置策略状态（启用/禁用）

### 5.3 数据管理

**数据导入：**
1. 点击菜单"数据管理"
2. 选择数据源格式
3. 导入股票基本信息和日线数据

**数据查看：**
- 查看股票基本信息
- 查看历史K线数据
- 查看技术指标数据

### 5.4 回测功能

**执行回测：**
1. 点击菜单"回测"
2. 选择策略
3. 设置回测参数：
   - 起始日期
   - 结束日期
   - 初始资金
   - 手续费
   - 滑点
4. 点击"开始回测"
5. 查看回测结果和图表

### 5.5 参数优化

**优化策略参数：**
1. 点击菜单"优化"
2. 选择策略和参数范围
3. 设置优化方法（网格搜索/遗传算法）
4. 执行优化
5. 查看优化结果

### 5.6 每日选股（AI智能评分）

**执行选股：**
1. 点击菜单"每日选股"
2. 选择选股日期
3. 可选：勾选"启用DeepSeek评分"
4. 点击"刷新选股"
5. 查看选股结果和评分

**导出结果：**
- 右键点击选中的股票
- 选择"导出选中"
- 保存为CSV文件

## 6. 常见问题

### 6.1 数据库连接失败

**错误提示：** "无法连接到数据库"

**解决方法：**
1. 检查MySQL服务是否运行
2. 验证连接字符串配置是否正确
3. 检查网络连接
4. 确认数据库用户权限

### 6.2 选股失败

**错误提示：** "加载数据失败: Connection must be Open"

**解决方法：**
1. 确认数据库配置正确（已在appsettings.json中配置连接池参数）
2. 检查数据库中是否有股票数据
3. 验证数据完整性

### 6.3 DeepSeek评分不可用

**现象：** DeepSeek评分显示为"-"

**解决方法：**
1. 检查API密钥是否配置
2. 验证网络连接
3. 检查API配额是否充足
4. 查看日志文件获取详细错误

### 6.4 编译警告

**警告类型：**
- 可空性警告（CS8629）
- 包兼容性警告（NU1701）
- 异步方法警告（CS1998）

**说明：** 这些警告不影响功能运行，可以忽略。

## 7. 技术支持

### 7.1 日志查看

应用程序日志会输出到控制台，包括：
- 数据库连接日志
- API调用日志
- 错误信息

### 7.2 性能优化建议

1. **数据库性能：**
   - 使用索引优化查询
   - 定期清理历史数据
   - 适当调整连接池大小

2. **选股性能：**
   - 减少股票数量筛选
   - 缓存常用数据
   - 限制DeepSeek调用频率

3. **回测性能：**
   - 使用合理的时间范围
   - 避免过多的策略组合

## 8. 版本信息

**版本：** 1.0.0  
**发布日期：** 2026年3月  
**.NET版本：** .NET 8.0

## 9. 许可证

本项目仅供学习和研究使用。
