// NpgsqlConnectionFactory.cs
using System.Data;
using BasicApi.Storage.Interfaces;
using Npgsql;

namespace BasicApi.Storage.Services;

public class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection CreateConnection()
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}