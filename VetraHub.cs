using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Dapper;
using System.Diagnostics;
using System.Net;

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
                Auth TEXT NOT NULL,
                LastActive DATETIME DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS NotificationLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Message TEXT NOT NULL,
                Timestamp TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Configuration (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO Configuration (Key, Value) VALUES ('MaxSubscribers', '100');
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
        
        //dotnet publish -c Release -r linux-arm64 --self-contained -o ./publish
        //dotnet publish -c Release -r win-x86 --self-contained -o ./publish

        builder.Services.AddSingleton<SubscriberRepository>();
        builder.Services.AddSingleton<LogRepository>();
        
        builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

        builder.Services.AddHostedService<NotificationService>();

        var app = builder.Build();
        
        // Log shutdown time when the application stops
        var lifetime = app.Lifetime;
        lifetime.ApplicationStopping.Register(() =>
        {
            SubscriberRepository repo = app.Services.GetRequiredService<SubscriberRepository>();
            LogRepository logs = app.Services.GetRequiredService<LogRepository>();
            logs.AddLog($"Server shutdown with {repo.GetSubscriberCount()} subscribers and {repo.GetMaxSubscribers()} sub cap");
        });
        
        SubscriberRepository repo = app.Services.GetRequiredService<SubscriberRepository>();
        LogRepository logs = app.Services.GetRequiredService<LogRepository>();
        logs.AddLog($"Server started with {repo.GetSubscriberCount()} subscribers and {repo.GetMaxSubscribers()} sub cap");

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
                return Results.StatusCode(401); //Unauthorized
            }

            lifetime.StopApplication();
            
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/restart", (PasswordMessage message, IOptions<WebPushConfig> config, IHostApplicationLifetime lifetime, IWebHostEnvironment env) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to restart server");
                return Results.StatusCode(401); //Unauthorized
            }

            logs.AddLog("Server restarting...");

            if (env.EnvironmentName == "Development")
            {
                // Detect if running in development and restart using `dotnet run`
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "run", // Adjust as needed
                    UseShellExecute = false
                };

                Process.Start(startInfo);
            }
            else
            {
                // Relaunch the application normally
                var executable = Process.GetCurrentProcess().MainModule?.FileName;
                if (executable != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = executable,
                        UseShellExecute = false
                    });
                }
            }

            // Stop the current instance
            lifetime.StopApplication();

            return Results.StatusCode(200); //OK
        });


        app.MapPost("/api/logs", (WebLogRequest request, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (request.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to read logs");
                return Results.StatusCode(401); //Unauthorized
            }

            if (request.Count < 0)
                return Results.StatusCode(400); //Bad Request

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
                return Results.StatusCode(401); //Unauthorized
            }
            
            logs.ClearLogs();
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/clearsubscribers", (PasswordMessage message, SubscriberRepository repo, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to clear subscribers");
                return Results.StatusCode(401); //Unauthorized
            }

            repo.ClearSubscribers(logs);
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/setlimit", (SubscriberLimitMessage message, SubscriberRepository repo, LogRepository logs, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to change subscriber limit");
                return Results.StatusCode(401); //Unauthorized
            }
            
            if (message.Limit < 0)
                return Results.StatusCode(400); //Bad Request

            repo.SetMaxSubscribers(message.Limit, logs);
            return Results.StatusCode(200); //OK
        });

        app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, SubscriberRepository repo, LogRepository logs) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.StatusCode(400); //Bad Request
            }

            if (repo.GetSubscriberCount() >= repo.GetMaxSubscribers())
            {
                logs.AddLog("Subscriber limit reached, cannot add new device");
                return Results.StatusCode(507); //Insufficient Storage
            }
            
            if (repo.SubscriberExists(subscription.Endpoint))
            {
                repo.UpdateActivity(subscription.Endpoint);
                return Results.StatusCode(204); //No Content
            }
            
            DataSubscriber subscriber = new DataSubscriber
            {
                Endpoint = subscription.Endpoint,
                P256DH = subscription.P256DH,
                Auth = subscription.Auth,
                LastActive = DateTime.UtcNow
            };

            repo.AddSubscriber(subscriber);
            logs.AddLog("New Device: " + subscription.Endpoint[^10..]);
            return Results.StatusCode(201); //Created
        });

        app.MapPost("/api/notifications/unsubscribe", (PushSubscription subscription, SubscriberRepository repo, LogRepository logs) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.StatusCode(400); //Bad Request
            }
            
            if (repo.SubscriberExists(subscription.Endpoint))
            {
                repo.RemoveSubscriber(subscription.Endpoint);
                logs.AddLog("Removed Device: " + subscription.Endpoint[^10..]);
                return Results.StatusCode(201); //Created
            }

            return Results.StatusCode(204); //No Content
        });

        app.MapPost("/api/notifications/send", async (WebNotificationRequest request, SubscriberRepository repo, LogRepository logs, IOptions<WebPushConfig> config) =>
        {            
            if (request.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempted to send notification");
                return Results.StatusCode(401); //Unauthorized
            }

            if (request.Content.Title == "" || request.Content.Body == "")
            {
                return Results.StatusCode(400); //Bad Request
            }

            VapidDetails vapidDetails = new VapidDetails(
                config.Value.Subject,
                config.Value.PublicVapidKey,
                config.Value.PrivateVapidKey);

            int successfulCount = 0;
            bool failed = false;

            foreach (DataSubscriber subscription in repo.GetSubscribers())
            {
                PushSubscription subscriber = new PushSubscription()
                {
                    Endpoint = subscription.Endpoint,
                    P256DH = subscription.P256DH,
                    Auth = subscription.Auth
                };
                
                WebPushClient pushService = new WebPushClient();
                try
                {
                    await pushService.SendNotificationAsync(subscriber, JsonSerializer.Serialize(request.Content), vapidDetails);
                    successfulCount++;
                }
                catch (WebPushException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                    {
                        logs.AddLog("Notification content too large for subscriber");
                        failed = true;
                        break;
                    }
                    else if (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
                    {
                        logs.AddLog("Failed to send notification to subscriber...removing");
                        repo.RemoveSubscriber(subscription.Endpoint);
                    }
                    else
                    {
                        logs.AddLog($"WebPushException: {ex.Message}");
                        failed = true;
                        break;
                    }
                }catch (Exception ex)
                {
                    logs.AddLog($"Error sending notification to subscriber: {ex.Message}");
                    failed = true;
                    break;
                }
            }

            logs.AddLog($"Sent {successfulCount} subscribers {request.Content.Title}");
            return failed ? Results.StatusCode(500) : Results.StatusCode(200); //Internal Server Error - OK
        });

        app.Run();
    }
}