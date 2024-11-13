using Microsoft.Data.Sqlite;
using Dapper;
using VetraHub;

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
    
    public DataSubscriber GetSubscriber(string endpoint)
    {
        using var connection = new SqliteConnection(connectionID);
        DataSubscriber subscriber = connection.QueryFirstOrDefault<DataSubscriber>(
            "SELECT * FROM Subscribers WHERE Endpoint = @Endpoint", 
            new { Endpoint = endpoint })!;
            
        return subscriber;
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
}