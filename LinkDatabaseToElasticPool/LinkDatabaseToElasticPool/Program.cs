using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Management.Sql;
using Microsoft.Azure.Management.Sql.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;

namespace LinkDatabaseToElasticPool
{
    /// <summary>
    /// This Simple Console Application is a quick and easy way to understand the basics ->
    /// of how to create an Azure SQL Database from a BACPAC Import and then link it to an Elastic Pool through the use of the SqlManagementClient Client Library.
    /// 
    /// The following code assumes you have already setup an Azure Resource Group, SQL Server and Elastic Pool
    /// 
    /// Provide you own authentication data and run the project to see it in action.
    /// </summary>
    class Program
    {
        private static SqlManagementClient _client;

        static void Main(string[] args)
        {
            //Authenticate With Client ID and Secret (OR Implement MSI)
            ClientCredential Credentials = new ClientCredential("YOUR CLIENT ID HERE", "YOUR CLIENT SECRET HERE");
            AuthenticationContext AuthContext = new AuthenticationContext(String.Format("https://login.windows.net/{0}", "YOUR TENANT ID HERE"));

            AuthenticationResult AuthResult = AuthContext.AcquireTokenAsync("https://management.core.windows.net/", Credentials).Result;

            TokenCredentials AuthTokenCredentials = new TokenCredentials(AuthResult.AccessToken);

            _client = new SqlManagementClient(AuthTokenCredentials);
            _client.SubscriptionId = "YOU SUBSCRIPTION ID HERE";

        EnterResourceGroup:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("Enter Resource Group Name:");
            Console.ForegroundColor = ConsoleColor.White;

            String ResourceGroupName = Console.ReadLine();

            if (String.IsNullOrWhiteSpace(ResourceGroupName))
                goto EnterResourceGroup;

            IEnumerable<Server> Servers = _client.Servers.ListByResourceGroup(ResourceGroupName);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Available Servers:");
            Console.WriteLine("---------------------");

            Console.ForegroundColor = ConsoleColor.White;

            foreach (Server CurrentServer in Servers)
            {
                Console.WriteLine(CurrentServer.Name);
            }

        EnterServerName:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("Enter a Server Name:");
            Console.ForegroundColor = ConsoleColor.White;

            String ServerName = Console.ReadLine();

            if (String.IsNullOrWhiteSpace(ServerName) || Servers.FirstOrDefault(x => x.Name.ToLowerInvariant() == ServerName.ToLowerInvariant()) == null)
                goto EnterServerName;

            EnterLoginDetails:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("Enter Server Login Details (Username and Password Comma Separated (,)):");
            Console.ForegroundColor = ConsoleColor.White;

            String LoginDetails = Console.ReadLine();

            if (String.IsNullOrWhiteSpace(LoginDetails) || !LoginDetails.Contains(","))
                goto EnterLoginDetails;

            String[] LoginDetailsArray = LoginDetails.Split(',');

            if (LoginDetailsArray.Count() < 2)
                goto EnterLoginDetails;

            IEnumerable<ElasticPool> Pools =
                ListElasticPools(ResourceGroupName, ServerName);

            if (Pools == null || Pools.Count() == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("Please create an Elastic Pool first!");
                Console.ReadKey();
                return;
            }

        EnterElasticPoolName:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("Enter Elastic Pool Name:");
            Console.ForegroundColor = ConsoleColor.White;

            String ElasticPoolName = Console.ReadLine();
            ElasticPool currentElasticPool = null;

            if (String.IsNullOrWhiteSpace(ElasticPoolName) || Pools.FirstOrDefault(x => x.Name.ToLowerInvariant() == ElasticPoolName.ToLowerInvariant()) == null)
                goto EnterElasticPoolName;
            else
                currentElasticPool = Pools.FirstOrDefault(x => x.Name.ToLowerInvariant() == ElasticPoolName.ToLowerInvariant());

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Creating Template Database, This May Take Sometime...");

            //********************************************************************************//
            //Here is where you will actually Import the database,                            //
            //Provide the information to Blob Storage Account that holds your Database BACPAC //
            //Important Information: The Database and Elastic Pool must have compatible SKU's //
            //For Example: ElasticPool -> Standard, Database -> Standard S2                   //
            //********************************************************************************//

            ImportRequest ImportRequestInfo =
                new ImportRequest(StorageKeyType.StorageAccessKey,
                "YOUR STORAGE ACCOUNT ACCESS KEY HERE", "YOUR BLOBL STORAGE URI HERE",
                LoginDetailsArray[0], LoginDetailsArray[1],
               currentElasticPool.Name + "-templatedb", "Standard", "S2", "100", AuthenticationType.SQL);

            ImportExportResponse Response =
                _client.Databases.Import(ResourceGroupName, ServerName, ImportRequestInfo);

            Console.WriteLine("Adding Template Database To Elastic Pool, This May Take Sometime...");

            //***********************************************************************************//
            //Here we fetch the newly created Database out ready for updating the SKU and Pool ID//
            //***********************************************************************************//
            Database NewlyCreatedDatabase = _client.Databases.Get(ResourceGroupName, ServerName, Response.DatabaseName);

            //Modified The ElasticPoolId (Provide The Selected ElasticPool Id So We Can Link.
            NewlyCreatedDatabase.ElasticPoolId = currentElasticPool.Id;
            //Modified The SKU So That The Database Is Compatible With The ElasticPool
            NewlyCreatedDatabase.Sku = new Sku("ElasticPool", "Standard");

            //Update The Database With The New SKU Settings And Elastic Pool Id.
            _client.Databases.CreateOrUpdate(ResourceGroupName, ServerName, Response.DatabaseName, NewlyCreatedDatabase);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Created Database And Linked To Selected Elastic Pool Successfully !");
        }

        private static IEnumerable<ElasticPool> ListElasticPools(String resourceGroupName, String serverName)
        {
            //Fetch Elastic Pools And Count Of DBs
            IEnumerable<ElasticPool> Results =
                _client.ElasticPools.ListByServer(resourceGroupName, serverName);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Available Elastic Pools:");
            Console.WriteLine("--------------------------");
            Console.ForegroundColor = ConsoleColor.White;

            foreach (ElasticPool ElasticPool in Results)
            {
                Console.WriteLine(ElasticPool.Name);
            }

            return Results;
        }
    }
}