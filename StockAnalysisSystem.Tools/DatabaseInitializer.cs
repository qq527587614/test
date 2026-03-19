using MySql.Data.MySqlClient;
using System.Text;

namespace StockAnalysisSystem.Tools;

/// <summary>
/// 数据库初始化工具
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            Console.WriteLine("数据库连接成功！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"数据库连接失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 执行SQL脚本文件
    /// </summary>
    public async Task ExecuteScriptAsync(string scriptPath)
    {
        try
        {
            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"SQL脚本文件不存在: {scriptPath}");
                return;
            }

            var scriptContent = await File.ReadAllTextAsync(scriptPath);

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            Console.WriteLine($"开始执行SQL脚本...");

            int successCount = 0;

            // 逐行处理SQL脚本
            var lines = scriptContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var currentStatement = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // 跳过空行和注释
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("--"))
                    continue;

                // 添加到当前语句
                currentStatement.Append(trimmedLine);

                // 如果以分号结尾，执行当前语句
                if (trimmedLine.EndsWith(";"))
                {
                    try
                    {
                        var statement = currentStatement.ToString().TrimEnd(';');
                        if (!string.IsNullOrWhiteSpace(statement))
                        {
                            using var command = new MySqlCommand(statement, connection);
                            await command.ExecuteNonQueryAsync();
                            successCount++;
                            Console.WriteLine($"执行成功 ({successCount}): {statement.Substring(0, Math.Min(30, statement.Length))}...");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"执行失败: {ex.Message}");
                    }

                    currentStatement.Clear();
                }
            }

            Console.WriteLine($"SQL脚本执行完成，成功{successCount}条语句");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"执行SQL脚本失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 检查表是否存在
    /// </summary>
    public async Task<bool> TableExistsAsync(string tableName)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"SELECT COUNT(*) FROM information_schema.tables 
                         WHERE table_schema = DATABASE() AND table_name = @tableName";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@tableName", tableName);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            return count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"检查表存在性失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 显示数据库信息
    /// </summary>
    public async Task ShowDatabaseInfoAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 获取数据库名称
            var dbName = connection.Database;
            Console.WriteLine($"数据库名称: {dbName}");

            // 获取表列表
            var query = @"SELECT table_name, table_comment FROM information_schema.tables 
                         WHERE table_schema = DATABASE() ORDER BY table_name";

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            Console.WriteLine("\n数据库中的表:");
            while (await reader.ReadAsync())
            {
                var tableName = reader.GetString(0);
                var comment = reader.GetString(1);
                Console.WriteLine($"  - {tableName}: {comment}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"显示数据库信息失败: {ex.Message}");
        }
    }
}
