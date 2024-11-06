// DATA

public class DataSubscriber
{
    public int Id { get; set; }
    public required string Endpoint { get; set; }
    public required string P256DH { get; set; }
    public required string Auth { get; set; }
}

public class DataNotificationLog
{
    public required int Id { get; set; }
    public required string Message { get; set; }
    public required DateTime Timestamp { get; set; }
}

// WEB

public class WebPushConfig
{
    public required string Subject { get; set; }
    public required string PublicVapidKey { get; set; }
    public required string PrivateVapidKey { get; set; }
    public required string NotificationPasswordHash { get; set; }
}

public class WebNotificationRequest
{
    public required WebNotificationMessage Content { get; set; }
    public required string Password { get; set; }
}

public class WebNotificationMessage
{
    public required string Title { get; set; }
    public required string Body { get; set; }
}