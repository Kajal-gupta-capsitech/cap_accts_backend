using MongoDB.Driver;

namespace Capsitech.Data.MongoDB
{
    public class DBConfiguration
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public IMongoDatabase Database { get; }
        public IMongoClient Client { get; }

        public DBConfiguration(IMongoDatabase database, string connectionString = "", string databaseName = "")
        {
            Database = database;
            ConnectionString = connectionString;
            DatabaseName = databaseName;
            Client = database.Client;
        }

        public DBConfiguration(string connectionString, string databaseName)
        {
            ConnectionString = connectionString;
            DatabaseName = databaseName;
            Client = new MongoClient(connectionString);
            Database = Client.GetDatabase(databaseName);
        }
    }
}
