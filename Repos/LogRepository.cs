using Microsoft.Data.Sqlite;
using Dapper;
using VetraHub;

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

        string localTimeFormatted = DateTime.UtcNow.ToLocalTime().ToString("HH:mm:ss:fff");
        Console.WriteLine($"[{localTimeFormatted}] {message}");
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