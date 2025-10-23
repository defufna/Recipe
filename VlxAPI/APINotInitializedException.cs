using VeloxDB.Protocol;

namespace VlxAPI;

public class APINotInitializedException : DbAPIErrorException
{
    public APINotInitializedException() : base("Database hasn't been initialized. Please call the Initialize method first.")
    {

    }
}

