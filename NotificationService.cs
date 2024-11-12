using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;
using System.Net;

public class NotificationService : IHostedService, IDisposable
{
    private readonly SubscriberRepository repo;
    private readonly LogRepository logs;
    private readonly IOptions<WebPushConfig> config;
    private readonly Timer timer;

    public static int CheckIntervalMinutes = 30;
    public static int RemindThresholdMinutes = 300;

    public NotificationService(SubscriberRepository subrepo, LogRepository logrepo, IOptions<WebPushConfig> configdata)
    {
        repo = subrepo;
        logs = logrepo;
        config = configdata;

        timer = new Timer(async _ => await NotifyInactiveSubscribers(null), null, TimeSpan.Zero, TimeSpan.FromMinutes(CheckIntervalMinutes));

        logs.AddLog("Notification Service Running");
        logs.AddLog($"Checking every {CheckIntervalMinutes} minutes and reminding after {RemindThresholdMinutes} minutes");
    }

    private async Task NotifyInactiveSubscribers(object? state)
    {
        logs.AddLog("Checking for inactive subscribers");
        
        List<DataSubscriber> inactiveSubscribers = repo.GetSubscribers()
            .Where(subscriber => DateTime.UtcNow.Subtract(subscriber.LastActive).TotalMinutes >= RemindThresholdMinutes)
            .ToList();

        if (inactiveSubscribers.Any())
        {
            logs.AddLog($"Found {inactiveSubscribers.Count} inactive subscribers");
            
            VapidDetails vapidDetails = new VapidDetails(
                config.Value.Subject,
                config.Value.PublicVapidKey,
                config.Value.PrivateVapidKey
            );
            
            int successfulCount = 0;

            foreach (DataSubscriber subscriber in inactiveSubscribers)
            {
                PushSubscription pushSubscription = new PushSubscription
                {
                    Endpoint = subscriber.Endpoint,
                    P256DH = subscriber.P256DH,
                    Auth = subscriber.Auth
                };

                WebPushClient pushService = new WebPushClient();
                WebNotificationMessage content = new WebNotificationMessage
                {
                    Title = "We miss you!",
                    Body = "Please come back!"
                };
                string json = JsonSerializer.Serialize(content);
                
                try
                {
                    await pushService.SendNotificationAsync(pushSubscription, json, vapidDetails);
                    repo.UpdateActivity(subscriber.Endpoint);
                    successfulCount++;
                }
                catch (WebPushException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                    {
                        logs.AddLog("Notification content too large for subscriber");
                        break;
                    }
                    else if (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
                    {
                        logs.AddLog("Failed to send notification to subscriber...removing");
                        repo.RemoveSubscriber(subscriber.Endpoint);
                    }
                    else
                    {
                        logs.AddLog($"WebPushException: {ex.Message}");
                        break;
                    }
                }catch (Exception ex)
                {
                    logs.AddLog($"Error sending notification to subscriber: {ex.Message}");
                    break;
                }
            }
            
            logs.AddLog($"Reminded {successfulCount} subscribers");
        }else
        {
            logs.AddLog("No inactive subscribers found");
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
