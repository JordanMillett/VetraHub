namespace  VetraHub
{
    // DATA
    public class DataSubscriber
    {
        public int Id { get; set; }
        public required string Endpoint { get; set; }
        public required string P256DH { get; set; }
        public required string Auth { get; set; }
        public DateTime LastActive { get; set; } = DateTime.UtcNow;
    }

    public class DataNotificationLog
    {
        public required int Id { get; set; }
        public required string Message { get; set; }
        public required DateTime Timestamp { get; set; }
    }
    
    public class DataTraffic
    {
        public required int Id { get; set; }
        public required string Address { get; set; }
        public required string Timezone { get; set; }
        public required string Language { get; set; }
    }

    // WEB
    public class WebPushConfig
    {
        public required string Subject { get; set; }
        public required string PublicVapidKey { get; set; }
        public required string PrivateVapidKey { get; set; }
        public required string PasswordHash { get; set; }
    }

    //SYNC ACROSS VETRA
    public class TrafficMessage
    {
        public required string Name { get; set; }
        public required string Version { get; set; }
        public required string Layout { get; set; }
        public required string Manufacturer { get; set; }
        public required string Product { get; set; }
        public required string Description { get; set; }
        public required string System { get; set; }
        public required string Timezone { get; set; }
        public required string Language { get; set; }
    }

    
    public class AlertDeviceMessage
    {
        public required string Endpoint { get; set; }
        public required string Password { get; set; }
    }

    public class PasswordMessage
    {
        public required string Password { get; set; }
    }

    public class SubscriberLimitMessage
    {
        public required int Limit { get; set; }
        public required string Password { get; set; }
    }

    public class WebLogRequest
    {
        public required int Count { get; set; }
        public required string Password { get; set; }
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
}

