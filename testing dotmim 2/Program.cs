// See https://aka.ms/new-console-template for more information\

// First provider on the server side, is using the Sql change tracking feature.
var serverProvider = new SqlSyncChangeTrackingProvider(serverConnectionString);

// IF you want to try with a MySql Database, use the [MySqlSyncProvider] instead
// var serverProvider = new MySqlSyncProvider(serverConnectionString);

// Second provider on the client side, is the [SqliteSyncProvider] used for SQLite databases
// relying on triggers and tracking tables to create the sync environment
var clientProvider = new SqliteSyncProvider(clientConnectionString);

// Tables involved in the sync process:
var setup = new SyncSetup("ProductCategory", "ProductModel", "Product",
    "Address", "Customer", "CustomerAddress", "SalesOrderHeader", "SalesOrderDetail");

// Creating an agent that will handle all the process
var agent = new SyncAgent(clientProvider, serverProvider);

do
{
    // Launch the sync process
    var s1 = await agent.SynchronizeAsync(setup);
    // Write results
    Console.WriteLine(s1);

} while (Console.ReadKey().Key != ConsoleKey.Escape);

Console.WriteLine("End");

Console.WriteLine("Hello, World!");
