namespace RecipeVectorSearch
{
    internal class DatabaseServiceCollection : IDisposable
    {
        private readonly List<IDatabaseService> dbServices;

        public IDatabaseService Default { get; private set; }

        public IReadOnlyList<IDatabaseService> Databases => dbServices.AsReadOnly();

        public DatabaseServiceCollection(IEnumerable<IDatabaseService> databases)
        {
            this.dbServices = databases?.ToList() ?? throw new ArgumentNullException(nameof(databases));
            if (!this.dbServices.Any())
            {
                throw new ArgumentException("At least one database service must be provided.", nameof(databases));
            }
            Default = this.dbServices[0];
        }

        public bool TrySetDefault(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return false;
            }

            var matchingDatabase = dbServices.FirstOrDefault(db => db.ShortName == shortName);
            if (matchingDatabase == null)
            {
                return false;
            }

            Default = matchingDatabase;
            return true;
        }

        public void Dispose()
        {
            foreach (var dbs in dbServices)
            {
                if (dbs is IDisposable)
                    ((IDisposable)dbs).Dispose();
            }
        }

        public int Index(IDatabaseService dbService)
        {
            return dbServices.FindIndex(dbs => dbs.Equals(dbService));
        }
    }
}