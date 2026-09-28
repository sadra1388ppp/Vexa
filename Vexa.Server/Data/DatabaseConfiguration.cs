using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace NovaChat.Server.Data;

public static class DatabaseConfiguration
{
    public static DbContextOptionsBuilder UseVexaDatabase(
        this DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set ConnectionStrings:DefaultConnection to a MariaDB connection string.");

        var connection = new MySqlConnectionStringBuilder(connectionString)
        {
            ConnectionTimeout = 5
        };
        if (string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("The MariaDB connection string must specify Database.");

        if (connection.Server is "localhost" or "127.0.0.1" or "::1")
            connection.SslMode = MySqlSslMode.None;

        // MariaDB DATETIME has no timezone. NovaChat stores application timestamps as Iran local time.
        // Unspecified prevents the connector from applying an additional UTC/local conversion.
        connection.DateTimeKind = MySqlDateTimeKind.Unspecified;
        var versionText = configuration["Database:ServerVersion"] ?? "11.8.0";
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidOperationException("Database:ServerVersion must be a version such as 11.8.0.");

        return options.UseMySql(
            connection.ConnectionString,
            new MariaDbServerVersion(version),
            mysqlOptions =>
            {
                mysqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 1,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorNumbersToAdd: null);
            });
    }
}
