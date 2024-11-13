using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;
using System.Net;
using VetraHub;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

public class HealthService : IHostedService, IDisposable
{
    private readonly LogRepository logs;
    private readonly Timer timer;

    public static int CheckIntervalMinutes = 120;
    
    public HealthService(LogRepository logrepo)
    {
        logs = logrepo;

        timer = new Timer(LogHealth, null, TimeSpan.Zero, TimeSpan.FromMinutes(CheckIntervalMinutes));

        logs.AddLog("Health Service Running");
        logs.AddLog($"Checking health every {CheckIntervalMinutes} minutes");
    }

    void LogHealth(object? state)
    {
        logs.AddLog($"System Name: {Environment.MachineName}");
        logs.AddLog($"Operating System: {RuntimeInformation.OSDescription}");
    
        logs.AddLog($"Memory Usage: {GetMemoryUsage()} MB");
    }

    private string GetMemoryUsage()
    {
        try
        {
            Process currentProcess = Process.GetCurrentProcess();

            long memoryUsageInBytes = currentProcess.WorkingSet64;

            float memoryUsageInMB = memoryUsageInBytes / (1024 * 1024);

            return memoryUsageInMB.ToString("F2");
        }
        catch
        {
            return "ERROR";
        }
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        timer?.Dispose();
    }
}
