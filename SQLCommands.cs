using Microsoft.Data.Sqlite;
using Dapper;

public class SubscriberRepository
{
    private readonly string connectionID;

    public SubscriberRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }
    
    public bool SubscriberExists(string endpoint)
    {
        using var connection = new SqliteConnection(connectionID);
        var count = connection.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM Subscribers WHERE Endpoint = @Endpoint", 
            new { Endpoint = endpoint }
        );
        return count > 0;
    }

    public void UpdateActivity(string endpoint)
    {
        using var connection = new SqliteConnection(connectionID);
        connection.Execute("UPDATE Subscribers SET LastActive = @LastActive WHERE Endpoint = @Endpoint", 
            new { LastActive = DateTime.UtcNow, Endpoint = endpoint });
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
    
    public int GetSubscriberCount()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Subscribers");
    }
    
    public void ClearSubscribers(LogRepository logs)
    {
        using var connection = new SqliteConnection(connectionID);
        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM Subscribers");
        connection.Execute("DELETE FROM Subscribers");

        logs.AddLog($"{count} subscribers cleared");
    }
    
    public int GetMaxSubscribers()
    {
        using var connection = new SqliteConnection(connectionID);
        string query = "SELECT Value FROM Configuration WHERE Key = 'MaxSubscribers'";
        return int.Parse(connection.ExecuteScalar<string>(query) ?? "100");
    }
    
    public void SetMaxSubscribers(int newLimit, LogRepository logs)
    {
        if (newLimit < 0)
        {
            throw new ArgumentException("Subscriber limit must be a positive integer.");
        }

        int oldLimit = GetMaxSubscribers();

        using var connection = new SqliteConnection(connectionID);
        string query = "UPDATE Configuration SET Value = @NewLimit WHERE Key = 'MaxSubscribers'";
        connection.Execute(query, new { NewLimit = newLimit });
        
        logs.AddLog($"Max subscriber limit changed from {oldLimit} to {newLimit}");
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
            new { Message = message, Timestamp = DateTime.UtcNow });

        string localTimeFormatted = DateTime.UtcNow.ToLocalTime().ToString("HH-mm-ss-fff");
        Console.WriteLine($"[{localTimeFormatted}]: {message}");
    }

    public IEnumerable<DataNotificationLog> GetAllLogs()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataNotificationLog>("SELECT * FROM NotificationLogs ORDER BY id DESC");
    }
    
    public IEnumerable<DataNotificationLog> GetLogs(int count)
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataNotificationLog>(
            "SELECT * FROM NotificationLogs ORDER BY id DESC LIMIT @Count",
            new { Count = count });
    }

    public void ClearLogs()
    {
        using var connection = new SqliteConnection(connectionID);
        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM NotificationLogs");
        connection.Execute("DELETE FROM NotificationLogs");

        AddLog($"{count} logs cleared");
    }
}
