namespace FeatBit.AppHost.Utils;

/// <summary>
/// Utility class for parsing PostgreSQL connection strings
/// </summary>
public static class PostgresConnectionStringParser
{
    /// <summary>
    /// Parses a PostgreSQL connection string and extracts host, user, password, and port components
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string to parse</param>
    /// <returns>A tuple containing the host, user, password, and port</returns>
    public static (string host, string? user, string? password, string port) ParseConnectionStringWithPort(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string host = "localhost";
        string? user = null;
        string? password = null;
        string port = "5432";

        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                var key = keyValue[0].Trim().ToLowerInvariant();
                var value = keyValue[1].Trim();

                switch (key)
                {
                    case "host":
                    case "server":
                        host = value;
                        break;
                    case "port":
                        port = value;
                        break;
                    case "username":
                    case "user":
                    case "uid":
                    case "user id":
                        user = value;
                        break;
                    case "password":
                    case "pwd":
                        password = value;
                        break;
                }
            }
        }

        return (host, user, password, port);
    }
}