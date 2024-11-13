using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;
using System.Net;
using VetraHub;

public class AlertService
{
    private readonly SubscriberRepository repo;
    private readonly LogRepository logs;
    private readonly KeyRepository keys;
    private readonly IOptions<WebPushConfig> config;

    public AlertService(SubscriberRepository subrepo, LogRepository logrepo, KeyRepository keyrepo, IOptions<WebPushConfig> configdata)
    {
        repo = subrepo;
        logs = logrepo;
        keys = keyrepo;
        config = configdata;

        logs.AddLog("Alert Service Running");
    }

    public void NotifyAlertDevice(string message)
    {
        string alertDevice = keys.GetAlertDevice();
        
        if (alertDevice != "")
        {
            VapidDetails vapidDetails = new VapidDetails(
                config.Value.Subject,
                config.Value.PublicVapidKey,
                config.Value.PrivateVapidKey
            );

            DataSubscriber subscriber = repo.GetSubscriber(alertDevice);
           
            PushSubscription pushSubscription = new PushSubscription
            {
                Endpoint = subscriber.Endpoint,
                P256DH = subscriber.P256DH,
                Auth = subscriber.Auth
            };

            WebPushClient pushService = new WebPushClient();
            WebNotificationMessage content = new WebNotificationMessage
            {
                Title = "VetraHub Alert",
                Body = message
            };
            string json = JsonSerializer.Serialize(content);
            
            try
            {
                pushService.SendNotificationAsync(pushSubscription, json, vapidDetails).Wait();
            }
            catch (WebPushException ex)
            {
                if (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    logs.AddLog("Notification content too large for alert device");
                }
                else if (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
                {
                    logs.AddLog("Failed to notify alert device...removing");
                    keys.SetAlertDevice("", logs);
                }
                else
                {
                    logs.AddLog($"WebPushException: {ex.Message}");
                }
            }catch (Exception ex)
            {
                logs.AddLog($"Error notifying alert device: {ex.Message}");
            }
            
            
            logs.AddLog($"Alert device notified");
        }else
        {
            logs.AddLog("No alert device set");
        }
    }
}
