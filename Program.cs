using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using WebPush;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Dapper;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using VetraHub;

public class Program
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
            INSERT OR IGNORE INTO Configuration (Key, Value) VALUES ('AlertDevice', '');
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

        builder.Services.AddSingleton<KeyRepository>();
        builder.Services.AddSingleton<SubscriberRepository>();
        builder.Services.AddSingleton<LogRepository>();
        
        builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

        builder.Services.AddSingleton<AlertService>();
        builder.Services.AddHostedService<NotificationService>();
        builder.Services.AddSingleton<HealthService>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<HealthService>());

        var app = builder.Build();
        
        AlertService Alert = app.Services.GetRequiredService<AlertService>();
        HealthService Health = app.Services.GetRequiredService<HealthService>();
        
        var lifetime = app.Lifetime;
        lifetime.ApplicationStopping.Register(() =>
        {
            Health.LogHealth(null);
            Alert.NotifyAlertDevice("Server Shutdown");
        });

        Health.LogHealth(null);
        Alert.NotifyAlertDevice("Server Started");
        
        app.UseCors("AllowSpecificOrigins");

        app.MapGet("/api/pulse", () =>
        {
            return Results.Ok(true);
        });

        app.MapPost("/api/traffic", (TrafficMessage message, HttpContext context) =>
        {
            string address = context.Connection.RemoteIpAddress!.ToString();

            Console.WriteLine($"Client IP: {address}");
            
            Console.WriteLine($"Name: {message.Name}");
            Console.WriteLine($"Version: {message.Version}");
            Console.WriteLine($"Layout: {message.Layout}");
            Console.WriteLine($"Manufacturer: {message.Manufacturer}");
            Console.WriteLine($"Product: {message.Product}");
            Console.WriteLine($"Description: {message.Description}");
            Console.WriteLine($"Operating System: {message.System}");
            Console.WriteLine($"Timezone: {message.Timezone}");
            Console.WriteLine($"Language: {message.Language}");
        });
        
        app.MapPost("/api/update", async (HttpRequest message, IOptions<WebPushConfig> config, LogRepository logs) =>
        {
            string payload = await new StreamReader(message.Body).ReadToEndAsync();
            string signature = message.Headers["X-Hub-Signature-256"].ToString();
            
            if (string.IsNullOrEmpty(signature))
            {
                logs.AddLog("Missing GitHub signature");
                return Results.StatusCode(400); //Bad Request
            }
            
            // GitHub sends the signature in the form 'sha256=<hash>'
            string githubSignature = signature.StartsWith("sha256=") ? signature.Substring(7) : string.Empty;
            if (string.IsNullOrEmpty(githubSignature))
            {
                logs.AddLog("Invalid GitHub signature format");
                return Results.StatusCode(400); //Bad Request
            }
            
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(config.Value.PasswordHash)))
            {
                var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var computedSignature = BitConverter.ToString(computedHash).Replace("-", "").ToLower();

                // Compare the computed hash with the GitHub signature
                if (!computedSignature.Equals(githubSignature, StringComparison.OrdinalIgnoreCase))
                {
                    logs.AddLog("Unauthorized attempt to update server");
                    return Results.StatusCode(401); //Unauthorized
                }
            }

            logs.AddLog("Restarting server to update...");
        
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var process = new ProcessStartInfo("update-server.sh")
                    {
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    Process.Start(process);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var process = new ProcessStartInfo("cmd.exe", "/c update-server.bat")
                    {
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    Process.Start(process);
                }
                else
                {
                    logs.AddLog("Unsupported platform for update script");
                    return Results.StatusCode(500); //Internal Server Error
                }
            }
            catch (Exception ex)
            {
                logs.AddLog($"Error starting update script: {ex.Message}");
                return Results.StatusCode(500); //Internal Server Error
            }
            
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/shutdown", (PasswordMessage message, IOptions<WebPushConfig> config, LogRepository logs, IHostApplicationLifetime lifetime) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to shut down server");
                return Results.StatusCode(401); //Unauthorized
            }

            lifetime.StopApplication();
            
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/restart", (PasswordMessage message, IOptions<WebPushConfig> config, LogRepository logs, IHostApplicationLifetime lifetime) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to restart server");
                return Results.StatusCode(401); //Unauthorized
            }

            logs.AddLog("Server restarting...");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "run",
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var process = new ProcessStartInfo("update-server.sh")
                    {
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    Process.Start(process);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var process = new ProcessStartInfo("cmd.exe", "/c update-server.bat")
                    {
                        UseShellExecute = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    Process.Start(process);
                }
                else
                {
                    logs.AddLog("Unsupported platform for update script");
                    return Results.StatusCode(500); //Internal Server Error
                }
            }
            catch (Exception ex)
            {
                logs.AddLog($"Error starting update script: {ex.Message}");
                return Results.StatusCode(500); //Internal Server Error
            }

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
        
        app.MapPost("/api/clearsubscribers", (PasswordMessage message, SubscriberRepository repo, LogRepository logs, KeyRepository keys, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to clear subscribers");
                return Results.StatusCode(401); //Unauthorized
            }

            keys.SetAlertDevice("", logs);
            repo.ClearSubscribers(logs);
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/setlimit", (SubscriberLimitMessage message, SubscriberRepository repo, LogRepository logs, KeyRepository keys, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to change subscriber limit");
                return Results.StatusCode(401); //Unauthorized
            }
            
            if (message.Limit < 0)
                return Results.StatusCode(400); //Bad Request

            keys.SetMaxSubscribers(message.Limit, logs);
            return Results.StatusCode(200); //OK
        });
        
        app.MapPost("/api/setalertdevice", (AlertDeviceMessage message, SubscriberRepository repo, LogRepository logs, KeyRepository keys, IOptions<WebPushConfig> config) =>
        {
            if (message.Password != config.Value.PasswordHash)
            {
                logs.AddLog("Unauthorized attempt to change alert device");
                return Results.StatusCode(401); //Unauthorized
            }
            
            if (message.Endpoint == "")
                return Results.StatusCode(400); //Bad Request

            if (!repo.SubscriberExists(message.Endpoint))
            {
                logs.AddLog("Alert device is not a subscriber");
                return Results.StatusCode(409); //Conflict
            }

            Alert.NotifyAlertDevice("Alert device status removed");
          
            keys.SetAlertDevice(message.Endpoint, logs);
            
            Alert.NotifyAlertDevice("Alert device status added");
            return Results.StatusCode(200); //OK
        });

        app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, SubscriberRepository repo, LogRepository logs, KeyRepository keys) =>
        {
            if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
            {
                return Results.StatusCode(400); //Bad Request
            }

            if (repo.GetSubscriberCount() >= keys.GetMaxSubscribers())
            {
                //logs.AddLog("Subscriber limit reached, cannot add new device");
                Alert.NotifyAlertDevice("Subscriber limit reached, cannot add new device");
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