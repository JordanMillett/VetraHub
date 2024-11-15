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
    private readonly SubscriberRepository repo;
    private readonly KeyRepository keys;
    private readonly TrafficRepository traffic;
    private readonly Timer timer;
    
    DateTime ServerStartTime = DateTime.UtcNow;

    public static int CheckIntervalMinutes = 120;
    
    public HealthService(LogRepository logrepo, SubscriberRepository subrepo, KeyRepository keysrepo, TrafficRepository trafficrepo)
    {
        logs = logrepo;
        repo = subrepo;
        keys = keysrepo;
        traffic = trafficrepo;

        timer = new Timer(LogHealth, null, TimeSpan.FromMinutes(CheckIntervalMinutes), TimeSpan.FromMinutes(CheckIntervalMinutes));

        logs.AddLog("Health Service Running");
        logs.AddLog($"Checking health every {CheckIntervalMinutes} minutes");
    }

    public void LogHealth(object? state)
    {
        TimeSpan Uptime = DateTime.UtcNow - ServerStartTime;
        
        logs.AddLog($"System Name: {Environment.MachineName}");
        logs.AddLog($"Operating System: {RuntimeInformation.OSDescription}");
        
        logs.AddLog($"Server Uptime: {Uptime.Days} days {Uptime.Hours} hours {Uptime.Minutes} minutes {Uptime.Seconds} seconds");
        logs.AddLog($"{DatabaseSize()} database, {repo.GetSubscriberCount()} subscribers, {keys.GetMaxSubscribers()} sub cap, {traffic.GetTrafficCount()} unique visitors");
    
        logs.AddLog($"Memory Usage: {GetMemoryUsage()} MB");
    }
    
    static string DatabaseSize()
    {
        string fileName = "notifications.db";
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        
        if (File.Exists(filePath))
        {
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSizeInBytes = fileInfo.Length;
            double fileSizeInMB = fileSizeInBytes / (1024.0 * 1024.0);
            return $"{fileSizeInMB:F2} MB";
        }
        else
        {
            return "Empty";
        }
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
