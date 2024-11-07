using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Dapper;

public class VetraHub
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        using var connection = new SqliteConnection(builder.Configuration.GetConnectionString("DefaultConnection"));
        connection.Execute(@"
            CREATE TABLE IF NOT EXISTS Subscribers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Endpoint TEXT NOT NULL,
                P256DH TEXT NOT NULL,
                Auth TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS NotificationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Message TEXT NOT NULL,
                Timestamp TEXT NOT NULL
            );
        ");

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
        builder.Services.AddSingleton<LogRepository>();
        
        builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

        var app = builder.Build();
        
        // Log shutdown time when the application stops
        var lifetime = app.Lifetime;
        lifetime.ApplicationStopping.Register(() =>
        {
            SubscriberRepository repo = app.Services.GetRequiredService<SubscriberRepository>();
            LogRepository logs = app.Services.GetRequiredService<LogRepository>();
            logs.AddLog($"Server shutdown with {repo.GetSubscriberCount()} subscribers");
        });
        
        SubscriberRepository repo = app.Services.GetRequiredService<SubscriberRepository>();
        LogRepository logs = app.Services.GetRequiredService<LogRepository>();
        logs.AddLog($"Server started with {repo.GetSubscriberCount()} subscribers");

        app.UseCors("AllowSpecificOrigins");

        app.MapGet("/api/pulse", () =>
        {
            return Results.Ok(true);
        });
        
        app.MapPost("/api/shutdown", (PasswordMessage message, IOptions<WebPushConfig> config, IHostApplicationLifetime lifetime) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to shut down server");
                return Results.Unauthorized();
            }

            lifetime.StopApplication();
            
            return Results.Ok();
        });

        app.MapPost("/api/logs", (WebLogRequest request, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (request.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to read logs");
                return Results.Unauthorized();
            }

            if (request.Count == 0)
                return Results.Ok(logs.GetAllLogs());
            else
                return Results.Ok(logs.GetLogs(request.Count));
        });

        app.MapPost("/api/clearlogs", (PasswordMessage message, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to clear logs");
                return Results.Unauthorized();
            }
            
            logs.ClearLogs();
            return Results.Ok();
        });
        
        app.MapPost("/api/clearsubscribers", (PasswordMessage message, SubscriberRepository repo, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to clear subscribers");
                return Results.Unauthorized();
            }

            repo.ClearSubscribers(logs);
            return Results.Ok();
        });

        app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, SubscriberRepository repo, LogRepository logs) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.BadRequest("Invalid subscription.");
            }
            
            DataSubscriber subscriber = new DataSubscriber
            {
                Endpoint = subscription.Endpoint,
                P256DH = subscription.P256DH,
                Auth = subscription.Auth
            };
            
            if (repo.SubscriberExists(subscriber))
            {
                return Results.Conflict("Already subscribed.");
            }

            repo.AddSubscriber(subscriber);
            logs.AddLog("New Device: " + subscription.Endpoint[^10..]);
            return Results.Ok("Subscribed successfully.");
        });

        app.MapPost("/api/notifications/unsubscribe", (PushSubscription subscription, SubscriberRepository repo, LogRepository logs) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.BadRequest("Invalid subscription.");
            }
            
            DataSubscriber subscriber = new DataSubscriber
            {
                Endpoint = subscription.Endpoint,
                P256DH = subscription.P256DH,
                Auth = subscription.Auth
            };
            
            if (repo.SubscriberExists(subscriber))
            {
                repo.RemoveSubscriber(subscriber);
                logs.AddLog("Removed Device: " + subscription.Endpoint[^10..]);
                return Results.Ok("Unsubscribed successfully.");
            }
            
            return Results.Conflict("Already unsubscribed.");
        });

        app.MapPost("/api/notifications/send", async (WebNotificationRequest request, SubscriberRepository repo, LogRepository logs, IOptions<WebPushConfig> config) =>
        {            
            if (request.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempted to send notification");
                return Results.Unauthorized();
            }

            VapidDetails vapidDetails = new VapidDetails(
                config.Value.Subject,
                config.Value.PublicVapidKey,
                config.Value.PrivateVapidKey);

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
                catch (WebPushException)
                {
                    logs.AddLog("Failed to send notification to subscriber...removing");
                    repo.RemoveSubscriber(subscription);
                }catch (Exception)
                {
                    logs.AddLog("Error sending notification to subscriber");
                }
            }

            logs.AddLog($"Successfully sent {successfulCount} subscribers {request.Content.Title}");
            return Results.Ok("Notification sent.");
        });

        app.Run();
    }
}