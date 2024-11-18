using Microsoft.Data.Sqlite;
using Dapper;
using VetraHub;

public class TrafficRepository
{
    private readonly string connectionID;

    public TrafficRepository(IConfiguration config)
    {
        connectionID = config.GetConnectionString("DefaultConnection")!;
    }

    public void AddTraffic(string address, TrafficMessage data, LogRepository logs)
    {
        using var connection = new SqliteConnection(connectionID);

        var alreadyExists = connection.QuerySingleOrDefault<int>(@"
            SELECT COUNT(1) 
            FROM Traffic 
            WHERE Address = @Address;
        ", new { Address = address });

        if (alreadyExists > 0)
            return;

        connection.Execute(@"
            INSERT INTO Traffic (Address, Timezone, Language)
            VALUES (@Address, @Timezone, @Language);
        ", new 
        {
            Address = address,
            Timezone = data.Timezone,
            Language = data.Language
        });

        logs.AddLog($"New traffic from {address} in {data.Timezone}");
    }
    
    public int GetTrafficCount()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.QuerySingle<int>("SELECT COUNT(*) FROM Traffic");
    }

    public IEnumerable<DataTraffic> GetAllTraffic()
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataTraffic>("SELECT * FROM Traffic ORDER BY id DESC");
    }
    
    public IEnumerable<DataTraffic> GetTraffic(int count)
    {
        using var connection = new SqliteConnection(connectionID);
        return connection.Query<DataTraffic>(
            "SELECT * FROM Traffic ORDER BY Id DESC LIMIT @Count",
            new { Count = count });
    }

    public void ClearTraffic(LogRepository logs)
    {
        using var connection = new SqliteConnection(connectionID);
        int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM Traffic");
        connection.Execute("DELETE FROM Traffic");

        logs.AddLog($"{count} traffic logs cleared");
    }
}