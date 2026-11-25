using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;

namespace VietVan.API.Services.Implementations;

public static class DatabaseLocks
{
    // Khóa thuộc transaction, có hiệu lực giữa các instance API dùng cùng SQL Server.
    public static Task DramaAsync(VietVanDbContext db, int id, CancellationToken ct = default) => AcquireAsync(db, $"drama:{id}", ct);
    public static async Task AcquireAsync(VietVanDbContext db, string resource, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Database lock timeout', 1;", ct);
}
