using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VietVan.API.Data;

// Sinh migration không khởi động API, gửi email hoặc kết nối database đang sử dụng.
public sealed class VietVanDesignTimeFactory : IDesignTimeDbContextFactory<VietVanDbContext>
{
    public VietVanDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<VietVanDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable("VIETVAN_MIGRATION_CONNECTION") ?? "Server=localhost;Database=VietVan_DesignOnly;Integrated Security=True;TrustServerCertificate=True")
        .Options);
}
