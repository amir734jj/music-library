using Npgsql;

namespace MusicLibrary.Api.Infrastructure;

public static class DatabaseUrlConverter
{
    public static string ToConnectionString(string databaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseUrl);
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var databaseUri)
            || (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException("DATABASE_URL must be an absolute postgres:// or postgresql:// URL.");
        }

        var credentials = databaseUri.UserInfo.Split(':', 2);
        var database = Uri.UnescapeDataString(databaseUri.AbsolutePath.Trim('/'));
        if (credentials.Length != 2 || string.IsNullOrWhiteSpace(credentials[0]) || string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("DATABASE_URL must include a username, password, and database name.");
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.IsDefaultPort ? 5432 : databaseUri.Port,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            Database = database,
            Pooling = true
        };

        foreach (var pair in databaseUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = pair.Split('=', 2);
            if (keyValue.Length != 2) continue;
            var key = Uri.UnescapeDataString(keyValue[0]);
            var value = Uri.UnescapeDataString(keyValue[1]);
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<SslMode>(value, true, out var sslMode))
            {
                connection.SslMode = sslMode;
            }
        }

        return connection.ConnectionString;
    }
}