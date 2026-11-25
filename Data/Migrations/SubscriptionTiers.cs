using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VietVan.API.Data;

namespace VietVan.API.Data.Migrations;

[DbContext(typeof(VietVanDbContext))]
[Migration("20261124_SubscriptionTiers")]
public sealed class SubscriptionTiers : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
ALTER TABLE dbo.SubscriptionPackages ADD Code nvarchar(32) NULL, BillingPeriod nvarchar(16) NULL;
ALTER TABLE dbo.PaymentOrders ADD BillingPeriodSnapshot nvarchar(16) NULL;
""");
        m.Sql("""
CREATE UNIQUE INDEX UX_SubscriptionPackages_Code ON dbo.SubscriptionPackages(Code) WHERE Code IS NOT NULL;
ALTER TABLE dbo.DramaProductions ADD PreviewDurationMs int NOT NULL CONSTRAINT DF_Drama_PreviewDuration DEFAULT 120000;
ALTER TABLE dbo.DramaAssets ADD IsPreview bit NOT NULL CONSTRAINT DF_DramaAsset_IsPreview DEFAULT 0;
ALTER TABLE dbo.DramaScenes ADD PreviewAudioAssetId int NULL, PreviewVideoAssetId int NULL;
""");
        m.Sql("""
ALTER TABLE dbo.DramaScenes ADD CONSTRAINT FK_DramaScene_PreviewAudio FOREIGN KEY (PreviewAudioAssetId) REFERENCES dbo.DramaAssets(Id);
ALTER TABLE dbo.DramaScenes ADD CONSTRAINT FK_DramaScene_PreviewVideo FOREIGN KEY (PreviewVideoAssetId) REFERENCES dbo.DramaAssets(Id);
CREATE TABLE dbo.CulturalInsightViews (
 UserId int NOT NULL, DramaProductionId int NOT NULL, DramaHotspotId int NOT NULL,
 ViewedAt datetime2 NOT NULL,
 CONSTRAINT PK_CulturalInsightViews PRIMARY KEY (UserId, DramaProductionId, DramaHotspotId),
 CONSTRAINT FK_CIV_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
 CONSTRAINT FK_CIV_Drama FOREIGN KEY (DramaProductionId) REFERENCES dbo.DramaProductions(Id),
 CONSTRAINT FK_CIV_Hotspot FOREIGN KEY (DramaHotspotId) REFERENCES dbo.DramaHotspots(Id)
);
""");
        m.Sql("""
-- Tier 1 là VIP cũ; không thay đổi gói hoặc quyền đã bán.
INSERT INTO dbo.SubscriptionPackages (Name, Code, BillingPeriod, Price, DurationInDays, TierLevel, Description, Benefits, IsActive, CreatedAt)
VALUES (N'Free', N'FREE', N'Free', 0, 0, 0, N'Xem thử', N'Preview và 5 insight mỗi vở', 1, SYSUTCDATETIME()),
 (N'Plus Monthly', N'PLUS_MONTHLY', N'Monthly', 50000, 30, 2, N'Thanh toán hàng tháng', N'Xem trọn vẹn và 5 insight mỗi vở', 1, SYSUTCDATETIME()),
 (N'Pro Monthly', N'PRO_MONTHLY', N'Monthly', 131000, 30, 3, N'Thanh toán hàng tháng', N'Không giới hạn insight và công cụ học tập Pro', 1, SYSUTCDATETIME()),
 (N'Pro Yearly', N'PRO_YEARLY', N'Yearly', 1287000, 365, 3, N'Thanh toán hàng năm', N'Không giới hạn insight và công cụ học tập Pro', 1, SYSUTCDATETIME());
""");
    }
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Không tự động xóa dữ liệu subscription; cần quy trình rollback riêng.");
}
