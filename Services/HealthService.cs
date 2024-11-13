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

    private DateTime _lastCpuCheckTime;
    private TimeSpan _lastCpuUsage;
    private double _cpuUsageSum;
    private int _cpuUsageCount;

    public static int CheckIntervalMinutes = 30;
    public static int CpuCheckIntervalSeconds = 1;
    
    public HealthService(LogRepository logrepo)
    {
        logs = logrepo;
        
        _lastCpuCheckTime = DateTime.Now;
        _lastCpuUsage = TimeSpan.Zero;
        _cpuUsageSum = 0;
        _cpuUsageCount = 0;

        timer = new Timer(LogHealth, null, TimeSpan.Zero, TimeSpan.FromMinutes(CheckIntervalMinutes));

        logs.AddLog("Health Service Running");
        logs.AddLog($"Checking health every {CheckIntervalMinutes} minutes");
    }

    void LogHealth(object? state)
    {
        logs.AddLog($"System Name: {Environment.MachineName}");
        logs.AddLog($"Operating System: {RuntimeInformation.OSDescription}");
    
        logs.AddLog($"Memory Usage: {GetMemoryUsage()} MB");
        
        logs.AddLog($"CPU Usage: {GetCpuUsage()} %");
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
    
    private string GetCpuUsage()
    {
        try
        {
            if (_cpuUsageCount == 0)
                return "0.00";

            double averageCpuUsage = _cpuUsageSum / _cpuUsageCount;
            
            _cpuUsageSum = 0;
            _cpuUsageCount = 0;
            
            return averageCpuUsage.ToString("F2");
        }
        catch
        {
            return "ERROR";
        }
    }
    
    void TrackCpuUsage()
    {
        try
        {
            Process currentProcess = Process.GetCurrentProcess();
            TimeSpan currentCpuUsage = currentProcess.TotalProcessorTime;

            // Calculate the time elapsed since the last check
            TimeSpan cpuUsageDelta = currentCpuUsage - _lastCpuUsage;
            TimeSpan timeElapsed = DateTime.Now - _lastCpuCheckTime;

            // Update the last check time and CPU usage for the next comparison
            _lastCpuCheckTime = DateTime.Now;
            _lastCpuUsage = currentCpuUsage;

            // Calculate CPU usage as a percentage of the total time elapsed
            double cpuUsagePercentage = (cpuUsageDelta.TotalMilliseconds / timeElapsed.TotalMilliseconds) * 100;

            // Accumulate the CPU usage for averaging later
            _cpuUsageSum += cpuUsagePercentage;
            _cpuUsageCount++;
        }
        catch
        {
            
        }
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Timer cpuTimer = new Timer(_ => TrackCpuUsage(), null, TimeSpan.Zero, TimeSpan.FromSeconds(CpuCheckIntervalSeconds));
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
