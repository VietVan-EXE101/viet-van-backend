using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.SqlClient;
using VietVan.API.Data;
using VietVan.API.Entities;

namespace VietVan.DatabaseBootstrap;

// Context riêng để tạo baseline từ model hiện tại mà không sửa migration cũ.
public sealed class FreshDatabaseContext(DbContextOptions<VietVanDbContext> options) : VietVanDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<CulturalInsightView>(e => {
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<DramaProduction>().WithMany().HasForeignKey(x => x.DramaProductionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<DramaHotspot>().WithMany().HasForeignKey(x => x.DramaHotspotId).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<UserSubscription>().HasOne<PaymentOrder>().WithMany()
            .HasForeignKey(x => x.ActivationPaymentOrderId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class FreshDatabaseContextFactory : IDesignTimeDbContextFactory<FreshDatabaseContext>
{
    public FreshDatabaseContext CreateDbContext(string[] args)
    {
        // Chỉ phục vụ thiết kế migration; không đọc hoặc lưu credential trong source.
        var connection = Environment.GetEnvironmentVariable("VIETVAN_BOOTSTRAP_CONNECTION")
            ?? "Server=localhost\\SQLEXPRESS;Database=VietVan_New;Integrated Security=True;TrustServerCertificate=True";
        var target = new SqlConnectionStringBuilder(connection);
        if (!string.Equals(target.DataSource, @"localhost\SQLEXPRESS", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.InitialCatalog, "VietVan_New", StringComparison.Ordinal) || !target.IntegratedSecurity)
            throw new InvalidOperationException("Bootstrap chỉ được cấu hình cho localhost\\SQLEXPRESS/VietVan_New bằng Windows Authentication.");
        var options = new DbContextOptionsBuilder<VietVanDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(FreshDatabaseContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory_Bootstrap"))
            .Options;
        return new FreshDatabaseContext(options);
    }
}
