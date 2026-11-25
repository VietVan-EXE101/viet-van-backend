using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietVan.DatabaseBootstrap.Migrations
{
    /// <inheritdoc />
    public partial class FreshBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Không cho phép baseline ghi vào database cũ hoặc database đã có bảng.
            migrationBuilder.Sql("""
IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Fresh baseline requires VietVan_New.', 1;
IF EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
           WHERE NOT (s.name=N'dbo' AND t.name=N'__EFMigrationsHistory_Bootstrap'))
    THROW 51001, 'Fresh baseline requires an empty database.', 1;
""");
            migrationBuilder.CreateTable(
                name: "LiteraryWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequiredTierLevel = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiteraryWorks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BillingPeriod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DurationInDays = table.Column<int>(type: "int", nullable: false),
                    TierLevel = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Benefits = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VipExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DramaProductions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LiteraryWorkId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PreviewDurationMs = table.Column<int>(type: "int", nullable: false),
                    AccessLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DramaProductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DramaProductions_LiteraryWorks_LiteraryWorkId",
                        column: x => x.LiteraryWorkId,
                        principalTable: "LiteraryWorks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    PackageNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DurationDaysSnapshot = table.Column<int>(type: "int", nullable: false),
                    TierLevelSnapshot = table.Column<int>(type: "int", nullable: false),
                    BillingPeriodSnapshot = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_SubscriptionPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "SubscriptionPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevenueTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevenueTransactions_SubscriptionPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "SubscriptionPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RevenueTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DramaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DramaProductionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsPreview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DramaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DramaAssets_DramaProductions_DramaProductionId",
                        column: x => x.DramaProductionId,
                        principalTable: "DramaProductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentOrderId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAuditLogs_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentBankTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PaymentOrderId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    AccountNumberMasked = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ClaimedOrderCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentBankTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentBankTransactions_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PackageId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ActivationPaymentOrderId = table.Column<long>(type: "bigint", nullable: true),
                    GrantedTierLevel = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_PaymentOrders_ActivationPaymentOrderId",
                        column: x => x.ActivationPaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_SubscriptionPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "SubscriptionPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DramaScenes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DramaProductionId = table.Column<int>(type: "int", nullable: false),
                    SceneKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    StartMs = table.Column<int>(type: "int", nullable: false),
                    EndMs = table.Column<int>(type: "int", nullable: false),
                    AudioOffsetMs = table.Column<int>(type: "int", nullable: false),
                    BackgroundAssetId = table.Column<int>(type: "int", nullable: true),
                    AudioAssetId = table.Column<int>(type: "int", nullable: true),
                    VideoAssetId = table.Column<int>(type: "int", nullable: true),
                    PreviewAudioAssetId = table.Column<int>(type: "int", nullable: true),
                    PreviewVideoAssetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DramaScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaAssets_AudioAssetId",
                        column: x => x.AudioAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaAssets_BackgroundAssetId",
                        column: x => x.BackgroundAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaAssets_PreviewAudioAssetId",
                        column: x => x.PreviewAudioAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaAssets_PreviewVideoAssetId",
                        column: x => x.PreviewVideoAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaAssets_VideoAssetId",
                        column: x => x.VideoAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaScenes_DramaProductions_DramaProductionId",
                        column: x => x.DramaProductionId,
                        principalTable: "DramaProductions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DramaHotspots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DramaSceneId = table.Column<int>(type: "int", nullable: false),
                    VisualAssetId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NameVi = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MeaningVi = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MeaningEn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    Width = table.Column<double>(type: "float", nullable: false),
                    Height = table.Column<double>(type: "float", nullable: false),
                    StartMs = table.Column<int>(type: "int", nullable: true),
                    EndMs = table.Column<int>(type: "int", nullable: true),
                    ZIndex = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TargetSegmentIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DramaHotspots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DramaHotspots_DramaAssets_VisualAssetId",
                        column: x => x.VisualAssetId,
                        principalTable: "DramaAssets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DramaHotspots_DramaScenes_DramaSceneId",
                        column: x => x.DramaSceneId,
                        principalTable: "DramaScenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CulturalInsightViews",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DramaProductionId = table.Column<int>(type: "int", nullable: false),
                    DramaHotspotId = table.Column<int>(type: "int", nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CulturalInsightViews", x => new { x.UserId, x.DramaProductionId, x.DramaHotspotId });
                    table.ForeignKey(
                        name: "FK_CulturalInsightViews_DramaHotspots_DramaHotspotId",
                        column: x => x.DramaHotspotId,
                        principalTable: "DramaHotspots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CulturalInsightViews_DramaProductions_DramaProductionId",
                        column: x => x.DramaProductionId,
                        principalTable: "DramaProductions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CulturalInsightViews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CulturalInsightViews_DramaHotspotId",
                table: "CulturalInsightViews",
                column: "DramaHotspotId");

            migrationBuilder.CreateIndex(
                name: "IX_CulturalInsightViews_DramaProductionId",
                table: "CulturalInsightViews",
                column: "DramaProductionId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaAssets_DramaProductionId_Kind",
                table: "DramaAssets",
                columns: new[] { "DramaProductionId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_DramaHotspots_DramaSceneId_ZIndex",
                table: "DramaHotspots",
                columns: new[] { "DramaSceneId", "ZIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_DramaHotspots_VisualAssetId",
                table: "DramaHotspots",
                column: "VisualAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaProductions_LiteraryWorkId",
                table: "DramaProductions",
                column: "LiteraryWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_AudioAssetId",
                table: "DramaScenes",
                column: "AudioAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_BackgroundAssetId",
                table: "DramaScenes",
                column: "BackgroundAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_DramaProductionId_SceneKey",
                table: "DramaScenes",
                columns: new[] { "DramaProductionId", "SceneKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_DramaProductionId_SortOrder",
                table: "DramaScenes",
                columns: new[] { "DramaProductionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_PreviewAudioAssetId",
                table: "DramaScenes",
                column: "PreviewAudioAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_PreviewVideoAssetId",
                table: "DramaScenes",
                column: "PreviewVideoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DramaScenes_VideoAssetId",
                table: "DramaScenes",
                column: "VideoAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LiteraryWorks_IsActive_RequiredTierLevel",
                table: "LiteraryWorks",
                columns: new[] { "IsActive", "RequiredTierLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAuditLogs_PaymentOrderId_CreatedAt",
                table: "PaymentAuditLogs",
                columns: new[] { "PaymentOrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBankTransactions_PaymentOrderId",
                table: "PaymentBankTransactions",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBankTransactions_Provider_ProviderTransactionId",
                table: "PaymentBankTransactions",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_OrderCode",
                table: "PaymentOrders",
                column: "OrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PackageId",
                table: "PaymentOrders",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Status_ExpiresAt",
                table: "PaymentOrders",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_UserId_IdempotencyKey",
                table: "PaymentOrders",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_Provider_ProviderEventId",
                table: "PaymentWebhookEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueTransactions_PackageId",
                table: "RevenueTransactions",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueTransactions_Status_PaymentDate",
                table: "RevenueTransactions",
                columns: new[] { "Status", "PaymentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RevenueTransactions_UserId",
                table: "RevenueTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPackages_Code",
                table: "SubscriptionPackages",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPackages_TierLevel",
                table: "SubscriptionPackages",
                column: "TierLevel");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_ActivationPaymentOrderId",
                table: "UserSubscriptions",
                column: "ActivationPaymentOrderId",
                unique: true,
                filter: "[ActivationPaymentOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_PackageId",
                table: "UserSubscriptions",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_IsActive",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.Sql("""
ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Amount CHECK(Amount > 0 AND Amount = FLOOR(Amount));
ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Duration CHECK(DurationDaysSnapshot > 0);
ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Status CHECK(Status IN('Pending','Paid','Expired','Cancelled','Failed','RequiresReview','Refunded'));
ALTER TABLE dbo.DramaScenes ADD CONSTRAINT CK_DramaScenes_Time CHECK (StartMs >= 0 AND EndMs > StartMs AND AudioOffsetMs >= 0);
ALTER TABLE dbo.DramaHotspots ADD CONSTRAINT CK_DramaHotspots_TargetsJson CHECK(ISJSON(TargetSegmentIdsJson) = 1);
INSERT INTO dbo.SubscriptionPackages (Name,Code,BillingPeriod,Price,DurationInDays,TierLevel,Description,Benefits,IsActive,CreatedAt)
SELECT v.Name,v.Code,v.Period,v.Price,v.Days,v.Tier,v.Description,v.Benefits,1,SYSUTCDATETIME()
FROM (VALUES
 (N'Free',N'FREE',N'Free',0,0,0,N'Xem thử',N'Preview và 5 insight mỗi vở'),
 (N'Plus Monthly',N'PLUS_MONTHLY',N'Monthly',50000,30,2,N'Thanh toán hàng tháng',N'Xem trọn vẹn và 5 insight mỗi vở'),
 (N'Pro Monthly',N'PRO_MONTHLY',N'Monthly',131000,30,3,N'Thanh toán hàng tháng',N'Không giới hạn insight và công cụ học tập Pro'),
 (N'Pro Yearly',N'PRO_YEARLY',N'Yearly',1287000,365,3,N'Thanh toán hàng năm',N'Không giới hạn insight và công cụ học tập Pro')
) v(Name,Code,Period,Price,Days,Tier,Description,Benefits)
WHERE NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPackages p WHERE p.Code=v.Code);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Không tự động xóa database hoặc bảng; cần quy trình rollback riêng.");
        }
    }
}