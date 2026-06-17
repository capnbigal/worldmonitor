using Microsoft.Data.SqlClient;

namespace WorldMonitor.Data.Locking;

/// <summary>sp_getapplock on a DEDICATED connection (not pooled-reused) so the session-scoped
/// lock is owned for exactly the handle's lifetime. @LockTimeout=0 ⇒ acquire-or-skip.</summary>
public sealed class SqlServerSeedLock(string connectionString) : ISeedLock
{
    public async Task<ISeedLockHandle?> TryAcquireAsync(string resource, CancellationToken ct = default)
    {
        // Pooling=false ⇒ this physical connection is never handed to another logical op.
        var csb = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        var conn = new SqlConnection(csb.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@res, @LockMode='Exclusive', " +
                "@LockOwner='Session', @LockTimeout=0; SELECT @r;";
            cmd.Parameters.Add(new SqlParameter("@res", resource));
            var rc = (int)(await cmd.ExecuteScalarAsync(ct))!;
            if (rc < 0) { await conn.DisposeAsync(); return null; } // contention ⇒ skip
            return new Handle(conn, resource);
        }
        catch (SqlException)
        {
            await conn.DisposeAsync();
            return null; // store unreachable ⇒ skip this cycle (do not crash the seeder)
        }
    }

    private sealed class Handle(SqlConnection conn, string resource) : ISeedLockHandle
    {
        public string Resource => resource;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "EXEC sp_releaseapplock @Resource=@res, @LockOwner='Session';";
                cmd.Parameters.Add(new SqlParameter("@res", resource));
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException) { /* session drop already released it */ }
            finally { await conn.DisposeAsync(); }
        }
    }
}
