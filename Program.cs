using Microsoft.Extensions.Options;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5109");

builder.Services.AddSingleton<PushSubscriptionStore>();
builder.Services.Configure<WebPushConfig>(builder.Configuration.GetSection("WebPush"));

var app = builder.Build();

app.MapGet("/api/pulse", () =>
{
    return Results.Ok(true);
});

app.MapPost("/api/notifications/subscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
{
    store.Add(subscription);
    return Results.Ok("Subscribed successfully.");
});

app.MapPost("/api/notifications/send", async (NotificationMessage message, PushSubscriptionStore store, IOptions<WebPushConfig> webPushConfig) =>
{
    var vapidDetails = new VapidDetails(
        webPushConfig.Value.Subject,
        webPushConfig.Value.PublicVapidKey,
        webPushConfig.Value.PrivateVapidKey);

    foreach (var subscription in store.GetSubscriptions())
    {
        var pushService = new WebPushClient();
        await pushService.SendNotificationAsync(subscription, message.Content, vapidDetails);
    }
    return Results.Ok("Notification sent.");
});

app.Run();

public class PushSubscriptionStore
{
    private readonly List<PushSubscription> _subscriptions = new List<PushSubscription>();

    public void Add(PushSubscription subscription) => _subscriptions.Add(subscription);
    public IEnumerable<PushSubscription> GetSubscriptions() => _subscriptions;
}

public class WebPushConfig
{
    public required string Subject { get; set; }
    public required string PublicVapidKey { get; set; }
    public required string PrivateVapidKey { get; set; }
}

public class NotificationMessage
{
    public required string Content { get; set; }
}