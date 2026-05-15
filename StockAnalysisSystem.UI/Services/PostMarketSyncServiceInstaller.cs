using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StockAnalysisSystem.UI.Services;

/// <summary>
/// 将「盘后同步 Worker」注册为 Windows 服务（需管理员 UAC）。
/// </summary>
public static class PostMarketSyncServiceInstaller
{
    public const string ServiceName = "StockAnalysisSystemPostMarketSync";

    /// <summary>主程序目录下存放 Worker 发布文件的子目录名。</summary>
    public const string WorkerSubFolder = "PostMarketSyncService";

    public static string GetWorkerDirectory() =>
        Path.Combine(AppContext.BaseDirectory, WorkerSubFolder);

    public static string GetWorkerExePath() =>
        Path.Combine(GetWorkerDirectory(), "StockAnalysisSystem.SyncWorker.exe");

    /// <summary>把主程序 appsettings.json 中的 MySql 连接串合并到 Worker 的 appsettings.json。</summary>
    public static void MergeConnectionStringFromMainApp(string mainAppSettingsPath, string workerDirectory)
    {
        if (!File.Exists(mainAppSettingsPath))
            return;

        var main = JsonNode.Parse(File.ReadAllText(mainAppSettingsPath)) as JsonObject;
        var mysql = main?["ConnectionStrings"]?.AsObject()?["MySql"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(mysql))
            return;

        Directory.CreateDirectory(workerDirectory);
        var workerPath = Path.Combine(workerDirectory, "appsettings.json");
        JsonObject worker;
        if (File.Exists(workerPath))
            worker = JsonNode.Parse(File.ReadAllText(workerPath)) as JsonObject ?? new JsonObject();
        else
            worker = new JsonObject();

        worker["ConnectionStrings"] ??= new JsonObject();
        worker["ConnectionStrings"]!.AsObject()!["MySql"] = mysql;

        var opt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(workerPath, worker.ToJsonString(opt));
    }

    /// <summary>启动提升权限的安装脚本；成功发起后返回 true（不等待子进程结束）。</summary>
    public static bool TryLaunchInstall(string workerExeFullPath, out string? error)
    {
        error = null;
        if (!File.Exists(workerExeFullPath))
        {
            error = $"未找到同步服务程序：{workerExeFullPath}";
            return false;
        }

        var workerDir = Path.GetDirectoryName(workerExeFullPath)!;
        var batPath = Path.Combine(Path.GetTempPath(), $"sas_install_{ServiceName}_{Guid.NewGuid():N}.bat");
        var exeQuoted = QuoteForCmd(workerExeFullPath);

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine($"net stop {ServiceName} >nul 2>&1");
        sb.AppendLine($"sc stop {ServiceName} >nul 2>&1");
        sb.AppendLine($"sc delete {ServiceName} >nul 2>&1");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        sb.AppendLine($"sc create {ServiceName} binPath= {exeQuoted} start= auto DisplayName= \"StockAnalysisSystem PostMarketSync\"");
        sb.AppendLine($"sc description {ServiceName} \"Daily post-market sync: Tencent daily snapshot, CLS limit-up, plate data.\"");
        sb.AppendLine($"sc start {ServiceName}");
        sb.AppendLine("echo Done.");
        sb.AppendLine("timeout /t 3 /nobreak >nul");
        File.WriteAllText(batPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = workerDir
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { File.Delete(batPath); } catch { /* ignore */ }
            return false;
        }
    }

    public static bool TryLaunchUninstall(out string? error)
    {
        error = null;
        var batPath = Path.Combine(Path.GetTempPath(), $"sas_uninstall_{ServiceName}_{Guid.NewGuid():N}.bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"net stop {ServiceName} >nul 2>&1");
        sb.AppendLine($"sc stop {ServiceName} >nul 2>&1");
        sb.AppendLine($"sc delete {ServiceName}");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        File.WriteAllText(batPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { File.Delete(batPath); } catch { /* ignore */ }
            return false;
        }
    }

    /// <summary>sc create 的 binPath 参数：路径含空格时用外层双引号包裹。</summary>
    private static string QuoteForCmd(string path)
    {
        if (path.Contains('"', StringComparison.Ordinal))
            path = path.Replace("\"", "\\\"", StringComparison.Ordinal);
        return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
    }
}
