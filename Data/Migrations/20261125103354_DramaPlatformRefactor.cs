using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace VietVan.API.Data.Migrations;

public partial class DramaPlatformRefactor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(DramaPlatformRefactor).Assembly.GetManifestResourceStream("VietVan.API.scripts.Upgrade_Drama_Platform.sql")
            ?? throw new InvalidOperationException("Không tìm thấy SQL migration được nhúng.");
        using var reader = new StreamReader(stream);
        // Script tự quản lý transaction và có thể chạy lặp sau lỗi ghi migration history.
        migrationBuilder.Sql(reader.ReadToEnd(), suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Không tự động xóa dữ liệu kịch, phiên xem hoặc lịch sử bảo mật. Cần quy trình rollback riêng.");
}
