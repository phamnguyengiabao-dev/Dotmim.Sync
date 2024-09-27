using Dotmim.Sync.SqlServer.ChangeTracking;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Sqlite;
using Dotmim.Sync;

// First provider on the server side, is using the Sql change tracking feature.
var serverConnectionString = "Server=BAO;Database=AdventureWorks;Integrated Security=True;TrustServerCertificate=True;";
var serverProvider = new SQL(serverConnectionString);

// IF you want to try with a MySql Database, use the [MySqlSyncProvider] instead
// var serverProvider = new MySqlSyncProvider(serverConnectionString);

// Second provider on the client side, is the [SqliteSyncProvider] used for SQLite databases
// relying on triggers and tracking tables to create the sync environment
var clientConnectionString = "Server=BAO;Database=Client;Integrated Security=True;TrustServerCertificate=True;";
var clientProvider = new SqliteSyncProvider(clientConnectionString);

// Tables involved in the sync process:
var setup1 = new SyncSetup(
    "Address",
    "Customer",
    "CustomerAddress"
);

var setup2 = new SyncSetup(
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

do
{
    // Launch the sync process
    var s1 = await agent.SynchronizeAsync(setup2);
    // Write results
    Console.WriteLine(s1);

} while (Console.ReadKey().Key != ConsoleKey.Escape);

Console.WriteLine("End");