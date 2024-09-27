using Dotmim.Sync.SqlServer.ChangeTracking;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Sqlite;
using Dotmim.Sync;
using Microsoft.Extensions.DependencyInjection;
class Program
{
    static Timer _timer;
    static SyncAgent _agent;

    static async Task Main(string[] args)
    {

        // First provider on the server side, is using the Sql change tracking feature.
        var serverConnectionString = "Server=BAO;Database=AdventureWorks;Integrated Security=True;TrustServerCertificate=True;";
        var serverProvider = new SqlSyncChangeTrackingProvider(serverConnectionString);

        // IF you want to try with a MySql Database, use the [MySqlSyncProvider] instead
        // var serverProvider = new MySqlSyncProvider(serverConnectionString);

        // Second provider on the client side, is the [SqliteSyncProvider] used for SQLite databases
        // relying on triggers and tracking tables to create the sync environment
        var clientConnectionString = "Server=BAO;Database=Client;Integrated Security=True;TrustServerCertificate=True;";
        var clientProvider = new SqliteSyncProvider(clientConnectionString);

        // Tables involved in the sync process:

        var setup = new SyncSetup(
            "ProductCategory",
            "ProductModel",
            "Product",
            "Address",
            "Customer",
            "CustomerAddress",
            "SalesOrderHeader",
            "SalesOrderDetail"
        );

        // Creating an agent that will handle all the process
        var agent = new SyncAgent(clientProvider, serverProvider);
        var _timer = new Timer(DoSync, null, 0, 300000);
        do
        {
            // Launch the sync process
            var s1 = await agent.SynchronizeAsync(setup);
            // Write results
            Console.WriteLine(s1);

        } while (Console.ReadKey().Key != ConsoleKey.Escape);


        Console.WriteLine("End");
    }
    private static async void DoSync(object state)
    {
        try
        {
            // Thực hiện đồng bộ hóa
            var s1 = await _agent.SynchronizeAsync();
            Console.WriteLine("Sync completed at {DateTime.Now}: {s1.TotalChangesDownloaded} changes downloaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Sync failed: {ex.Message}");
        }
    }
}