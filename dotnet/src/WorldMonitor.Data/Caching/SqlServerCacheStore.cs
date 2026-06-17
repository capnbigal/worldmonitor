using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data;

namespace WorldMonitor.Data.Caching;

public sealed class SqlServerCacheStore(WorldMonitorDbContext db) : ICacheStore
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxRetries = 2;

    private SqlConnection Conn => (SqlConnection)db.Database.GetDbConnection();

    private async Task<SqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        if (Conn.State != System.Data.ConnectionState.Open) await Conn.OpenAsync(ct);
        var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    public async Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await using var cmd = await CommandAsync(
                "SELECT Value, ExpiresAtUtc, FetchedAt, RecordCount FROM CacheEntries " +
                "WHERE CacheKey = @k AND ExpiresAtUtc > SYSUTCDATETIME();", ct);
            cmd.Parameters.Add(new SqlParameter("@k", key));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return CacheReadResult.Miss;
            return CacheReadResult.Hit(
                r.GetString(0), r.GetDateTime(1),
                r.IsDBNull(2) ? null : r.GetDateTime(2),
                r.IsDBNull(3) ? null : r.GetInt32(3));
        }
        catch (SqlException)
        {
            return CacheReadResult.Error; // tri-state: store failure is NEVER a miss
        }
    }

    public async Task UpsertAsync(CacheUpsert e, CancellationToken ct = default)
    {
        var bytes = System.Text.Encoding.Unicode.GetByteCount(e.Value);
        if (bytes > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(e), $"payload {bytes}B exceeds {MaxBytes}B cap");

        const string sql =
            "MERGE CacheEntries WITH (HOLDLOCK) AS t " +
            "USING (VALUES(@k)) AS s(CacheKey) ON t.CacheKey = s.CacheKey " +
            "WHEN MATCHED THEN UPDATE SET Value=@v, ExpiresAtUtc=DATEADD(second,@ttl,SYSUTCDATETIME()), " +
            "  FetchedAt=COALESCE(@fetched,SYSUTCDATETIME()), RecordCount=@rc, State=@state, " +
            "  SourceVersion=@sv, NewestItemAt=@nia, MaxContentAgeMin=@mca, UpdatedAt=SYSUTCDATETIME() " +
            "WHEN NOT MATCHED THEN INSERT (CacheKey,Value,ExpiresAtUtc,FetchedAt,RecordCount,State,SourceVersion,NewestItemAt,MaxContentAgeMin,UpdatedAt) " +
            "  VALUES (@k,@v,DATEADD(second,@ttl,SYSUTCDATETIME()),COALESCE(@fetched,SYSUTCDATETIME()),@rc,@state,@sv,@nia,@mca,SYSUTCDATETIME());";

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var cmd = await CommandAsync(sql, ct);
                cmd.Parameters.Add(new SqlParameter("@k", e.Key));
                cmd.Parameters.Add(new SqlParameter("@v", e.Value));
                cmd.Parameters.Add(new SqlParameter("@ttl", (long)e.Ttl.TotalSeconds));
                cmd.Parameters.Add(new SqlParameter("@fetched", (object?)e.FetchedAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@rc", (object?)e.RecordCount ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@state", (object?)e.State ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@sv", (object?)e.SourceVersion ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@nia", (object?)e.NewestItemAt ?? DBNull.Value));
                cmd.Parameters.Add(new SqlParameter("@mca", (object?)e.MaxContentAgeMin ?? DBNull.Value));
                await cmd.ExecuteNonQueryAsync(ct);
                return;
            }
            catch (SqlException ex) when (attempt < MaxRetries && SqlExceptionClassifier.IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
    }

    public async Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(
            "UPDATE CacheEntries SET ExpiresAtUtc = DATEADD(second,@ttl,SYSUTCDATETIME()) " +
            "WHERE CacheKey = @k AND ExpiresAtUtc > SYSUTCDATETIME();", ct);
        cmd.Parameters.Add(new SqlParameter("@ttl", (long)ttl.TotalSeconds));
        cmd.Parameters.Add(new SqlParameter("@k", key));
        return await cmd.ExecuteNonQueryAsync(ct) > 0; // @@ROWCOUNT == 0 ⇒ no live row to extend
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        if (keys.Count == 0) return result;
        var (inClause, ps) = BuildInList(keys);
        await using var cmd = await CommandAsync(
            $"SELECT CacheKey, Value FROM CacheEntries WHERE ExpiresAtUtc > SYSUTCDATETIME() AND CacheKey IN ({inClause});", ct);
        cmd.Parameters.AddRange(ps);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    public async Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var result = new List<CachePresence>();
        if (keys.Count == 0) return result;
        var (inClause, ps) = BuildInList(keys);
        await using var cmd = await CommandAsync(
            $"SELECT CacheKey, ByteLength, ExpiresAtUtc FROM CacheEntries WHERE ExpiresAtUtc > SYSUTCDATETIME() AND CacheKey IN ({inClause});", ct);
        cmd.Parameters.AddRange(ps);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(new CachePresence(r.GetString(0), r.GetInt64(1), r.GetDateTime(2)));
        return result;
    }

    private static (string, SqlParameter[]) BuildInList(IReadOnlyCollection<string> keys)
    {
        var ps = keys.Select((k, i) => new SqlParameter($"@k{i}", k)).ToArray();
        var inClause = string.Join(",", ps.Select(p => p.ParameterName));
        return (inClause, ps);
    }
}
