using Microsoft.Extensions.Options;
using WebPush;
using System.Text.Json;

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

builder.Services.AddSingleton<PushSubscriptionStore>();
builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

var app = builder.Build();

app.UseCors("AllowSpecificOrigins");

app.MapGet("/api/pulse", () =>
{
    return Results.StatusCode(225);
});

app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
{
    if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
    {
        return Results.BadRequest("Invalid subscription.");
    }
    
    // Check if the subscription already exists
    if (store.GetSubscriptions().Any(s => s.Endpoint == subscription.Endpoint))
    {
        return Results.Conflict("Already subscribed.");
    }

    Console.WriteLine("New Device: " + subscription.Endpoint);
    
    store.Add(subscription);
    return Results.Ok("Subscribed successfully.");
});

app.MapPost("/api/notifications/unsubscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
{
    if (string.IsNullOrEmpty(subscription.P256DH) || string.IsNullOrEmpty(subscription.Auth))
    {
        return Results.BadRequest("Invalid subscription.");
    }
    
    // Check if the subscription already exists
    if (store.GetSubscriptions().Any(s => s.Endpoint == subscription.Endpoint))
    {
        store.Remove(subscription);
        Console.WriteLine("Removed Device: " + subscription.Endpoint);
        return Results.Ok("Unsubscribed successfully.");
    }

    return Results.Conflict("Already unsubscribed.");
});

app.MapPost("/api/notifications/send", async (NotificationRequest request, PushSubscriptionStore store, IOptions<WebPushConfig> webPushConfig) =>
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

    foreach (var subscription in store.GetSubscriptions())
    {
        var pushService = new WebPushClient();
        try
        {
            await pushService.SendNotificationAsync(subscription, JsonSerializer.Serialize(request.Content), vapidDetails);
            successfulCount++;
        }
        catch (WebPushException ex)
        {
            Console.WriteLine($"Failed to send notification to subscription: {subscription.Endpoint}. Error: {ex.Message}");
            store.Remove(subscription);
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

public class PushSubscriptionStore
{
    private readonly List<PushSubscription> _subscriptions = new List<PushSubscription>();

    public void Add(PushSubscription subscription) => _subscriptions.Add(subscription);
    
    public bool Remove(PushSubscription subscription)
    {
        return _subscriptions.Remove(subscription);
    }
    
    public IEnumerable<PushSubscription> GetSubscriptions() => _subscriptions;
}

public class WebPushConfig
{
    public required string Subject { get; set; }
    public required string PublicVapidKey { get; set; }
    public required string PrivateVapidKey { get; set; }
    public required string NotificationPasswordHash { get; set; }
}

//SYNC
public class NotificationRequest
{
    public required NotificationMessage Content { get; set; }
    public required string Password { get; set; }
}

public class NotificationMessage
{
    public required string Title { get; set; }
    public required string Body { get; set; }
}