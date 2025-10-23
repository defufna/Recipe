using VeloxDB.Protocol;

namespace VlxAPI;

public class APIArgumentException : DbAPIErrorException
{
    public APIArgumentException(string message) : base(message)
    {
    }
}

