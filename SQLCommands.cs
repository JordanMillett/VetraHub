using Microsoft.Data.Sqlite;
using Dapper;

public class SubscriberRepository
{
    private readonly string connectionID;

    public SubscriberRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }

    public void AddSubscriber(DataSubscriber subscriber)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("INSERT INTO Subscribers (Endpoint, P256DH, Auth) VALUES (@Endpoint, @P256DH, @Auth)", subscriber);
    }

    public bool RemoveSubscriber(string endpoint)
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Execute("DELETE FROM Subscribers WHERE Endpoint = @Endpoint", new { Endpoint = endpoint }) > 0;
    }

    public IEnumerable<DataSubscriber> GetSubscribers()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataSubscriber>("SELECT * FROM Subscribers");
    }
}

public class NotificationLogRepository
{
    private readonly string connectionID;

    public NotificationLogRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }

    public void AddLog(string message)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("INSERT INTO NotificationLogs (Message, Timestamp) VALUES (@Message, @Timestamp)",
            new { Message = message, Timestamp = DateTime.UtcNow });
    }

    public IEnumerable<DataNotificationLog> GetLogs()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataNotificationLog>("SELECT * FROM NotificationLogs");
    }
}
