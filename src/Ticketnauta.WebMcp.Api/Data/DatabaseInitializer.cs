using Npgsql;

namespace Ticketnauta.WebMcp.Data;

public sealed class DatabaseInitializer(
    NpgsqlDataSource dataSource,
    IWebHostEnvironment environment,
    ILogger<DatabaseInitializer> logger)
{
    private const long AdvisoryLockKey = 8_428_110_026;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(environment.ContentRootPath, "Database", "schema.sql");
        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(AppContext.BaseDirectory, "Database", "schema.sql");
        }

        var schemaSql = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 15; attempt++)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                await using var lockCommand = new NpgsqlCommand(
                    "SELECT pg_advisory_lock(@key)", connection);
                lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);

                try
                {
                    await using var schemaCommand = new NpgsqlCommand(schemaSql, connection)
                    {
                        CommandTimeout = 60
                    };
                    await schemaCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                finally
                {
                    await using var unlockCommand = new NpgsqlCommand(
                        "SELECT pg_advisory_unlock(@key)", connection);
                    unlockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
                    await unlockCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                logger.LogInformation(
                    "Demo schema webmcp_demo is ready. No tables outside this schema were changed.");
                return;
            }
            catch (Exception ex) when (attempt < 15 && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                logger.LogWarning(
                    "PostgreSQL is not ready yet (attempt {Attempt}/15). Retrying in two seconds.",
                    attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "Could not initialize the isolated webmcp_demo schema after 15 attempts.", lastError);
    }
}
