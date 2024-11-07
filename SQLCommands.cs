using Microsoft.Data.Sqlite;
using Dapper;

public class SubscriberRepository
{
    private readonly string connectionID;

    public SubscriberRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }
    
    public bool SubscriberExists(DataSubscriber subscriber)
    {
        using var connection = new SqliteConnection(connectionID);
        var count = connection.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM Subscribers WHERE Endpoint = @Endpoint", 
            new { subscriber.Endpoint }
        );
        return count > 0;
    }

    public void AddSubscriber(DataSubscriber subscriber)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("INSERT INTO Subscribers (Endpoint, P256DH, Auth) VALUES (@Endpoint, @P256DH, @Auth)", subscriber);
    }

    public bool RemoveSubscriber(DataSubscriber subscriber)
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Execute("DELETE FROM Subscribers WHERE Endpoint = @Endpoint", new { subscriber.Endpoint }) > 0;
    }

    public IEnumerable<DataSubscriber> GetSubscribers()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataSubscriber>("SELECT * FROM Subscribers");
    }
    
    public int GetSubscriberCount()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Subscribers");
    }
    
    public void ClearSubscribers(LogRepository logs)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("DELETE FROM Subscribers");

        logs.AddLog("All subscribers cleared");
    }
}

public class LogRepository
{
    private readonly string connectionID;

    public LogRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }

    public void AddLog(string message)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("INSERT INTO NotificationLogs (Message, Timestamp) VALUES (@Message, @Timestamp)",
            new { Message = message, Timestamp = DateTime.Now });

        Console.WriteLine(message);
    }

    public IEnumerable<DataNotificationLog> GetLogs()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataNotificationLog>("SELECT * FROM NotificationLogs");
    }
    
    public void ClearLogs()
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("DELETE FROM NotificationLogs");

        AddLog("All logs cleared");
    }
}
