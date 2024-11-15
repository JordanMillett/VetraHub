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

        connection.Execute(@"
            INSERT INTO Traffic (Address, Name, Version, Layout, Manufacturer, Product, Description, System, Timezone, Language)
            VALUES (@Address, @Name, @Version, @Layout, @Manufacturer, @Product, @Description, @System, @Timezone, @Language);
        ", new 
        {
            Address = address,
            Name = data.Name,
            Version = data.Version,
            Layout = data.Layout,
            Manufacturer = data.Manufacturer,
            Product = data.Product,
            Description = data.Description,
            System = data.System,
            Timezone = data.Timezone,
            Language = data.Language
        });

        logs.AddLog($"New traffic from {address} in {data.Timezone}");
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