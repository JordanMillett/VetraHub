using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;

public class VetraHub
{
    public void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowSpecificOrigins", policy =>
            {
                policy.WithOrigins("https://jordanmillett.github.io", "http://localhost:5108")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        builder.WebHost.UseUrls("http://localhost:5109");
        //https://mole-factual-pleasantly.ngrok-free.app/api/pulse
        //ngrok http --url=mole-factual-pleasantly.ngrok-free.app 5109   

        builder.Services.AddSingleton<SubscriberRepository>();
        builder.Services.AddSingleton<NotificationLogRepository>();
        
        builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

        var app = builder.Build();

        app.UseCors("AllowSpecificOrigins");

        app.MapGet("/api/pulse", () =>
        {
            return Results.Ok(true);
        });

        app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, SubscriberRepository repo) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.BadRequest("Invalid subscription.");
            }
            
            /*
            // Check if the subscription already exists
            if (store.GetSubscriptions().Any(s => s.Endpoint == subscription.Endpoint))
            {
                return Results.Conflict("Already subscribed.");
            }*/
            
            DataSubscriber subscriber = new DataSubscriber
            {
                Endpoint = subscription.Endpoint,
                P256DH = subscription.P256DH,
                Auth = subscription.Auth
            };

            Console.WriteLine("New Device: " + subscription.Endpoint);

            repo.AddSubscriber(subscriber);
            return Results.Ok("Subscribed successfully.");
        });

        app.MapPost("/api/notifications/unsubscribe", (PushSubscription subscription, SubscriberRepository repo) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.BadRequest("Invalid subscription.");
            }
            /*
            // Check if the subscription already exists
            if (store.GetSubscriptions().Any(s => s.Endpoint == subscription.Endpoint))
            {
                store.Remove(subscription);
                Console.WriteLine("Removed Device: " + subscription.Endpoint);
                return Results.Ok("Unsubscribed successfully.");
            }*/

            return Results.Conflict("Already unsubscribed.");
        });

        app.MapPost("/api/notifications/send", async (WebNotificationRequest request, SubscriberRepository repo, IOptions<WebPushConfig> webPushConfig) =>
        {
            var expectedHash = webPushConfig.Value.NotificationPasswordHash;

            if (request.Password != expectedHash)
            {
                return Results.Unauthorized();
            }

            var vapidDetails = new VapidDetails(
                webPushConfig.Value.Subject,
                webPushConfig.Value.PublicVapidKey,
                webPushConfig.Value.PrivateVapidKey);

            //Console.WriteLine(request.Content.Title + " - " + request.Content.Body);

            int successfulCount = 0;

            foreach (DataSubscriber subscription in repo.GetSubscribers())
            {
                PushSubscription subscriber = new PushSubscription()
                {
                    Endpoint = subscription.Endpoint,
                    P256DH = subscription.P256DH,
                    Auth = subscription.Auth
                };
                
                var pushService = new WebPushClient();
                try
                {
                    await pushService.SendNotificationAsync(subscriber, JsonSerializer.Serialize(request.Content), vapidDetails);
                    successfulCount++;
                }
                catch (WebPushException ex)
                {
                    Console.WriteLine($"Failed to send notification to subscription: {subscription.Endpoint}. Error: {ex.Message}");
                    //repo.RemoveSubscriber(subscription);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                }
            }

            Console.WriteLine($"Successfully sent {request.Content.Title} to {successfulCount}");
            return Results.Ok("Notification sent.");
        });

        app.Run();
    }
}