using System.Data;

namespace BasicApi.Storage.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}