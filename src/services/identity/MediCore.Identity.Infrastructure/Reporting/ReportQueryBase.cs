using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Identity.Infrastructure.Reporting;

public abstract class ReportQueryBase<TFilter, TRow> : IDisposable
{
    private readonly DbContext _dbContext;
    private bool _disposed;

    protected ReportQueryBase(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected async Task<IReadOnlyList<TRow>> ExecuteAsync(
        TFilter filter,
        CancellationToken cancellationToken)
    {
        var conn = (NpgsqlConnection)_dbContext.Database.GetDbConnection();

        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();

        BuildQuery(cmd, filter);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var results = new List<TRow>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(MapRow(reader));

        return results;
    }

    protected abstract void BuildQuery(NpgsqlCommand cmd, TFilter filter);

    protected abstract TRow MapRow(NpgsqlDataReader reader);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
