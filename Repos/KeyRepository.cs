using Microsoft.Data.Sqlite;
using Dapper;
using VetraHub;

public class KeyRepository
{
    private readonly string connectionID;

    public KeyRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
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
    
    public string GetAlertDevice()
    {
        using var connection = new SqliteConnection(connectionID);
        string query = "SELECT Value FROM Configuration WHERE Key = 'AlertDevice'";
        return connection.ExecuteScalar<string>(query) ?? "";
    }
    
    public void SetAlertDevice(string endpoint, LogRepository logs)
    {
        using var connection = new SqliteConnection(connectionID);
        string query = "UPDATE Configuration SET Value = @Endpoint WHERE Key = 'AlertDevice'";
        connection.Execute(query, new { Endpoint = endpoint });
        
        logs.AddLog($"Alert device set to {endpoint[^10..]}");
    }
}