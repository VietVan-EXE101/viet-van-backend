-- Generated from FreshBaseline. Run only against a confirmed empty VietVan_New database.
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRY
  IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;
  IF ISNULL(CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')), N'') <> N'SQLEXPRESS' THROW 51001, 'Expected SQL Server instance SQLEXPRESS.', 1;
  IF SCHEMA_NAME() <> N'dbo' THROW 51002, 'The executing database user must have default schema dbo.', 1;
  IF @@TRANCOUNT <> 0 THROW 51003, 'Run outside an existing transaction.', 1;
  IF EXISTS (SELECT 1 FROM sys.objects WHERE is_ms_shipped=0 AND type IN ('U','V','P','FN','IF','TF','SN','SO')) THROW 51004, 'Database contains user objects; no changes made.', 1;
  BEGIN TRANSACTION;
  EXEC sys.sp_executesql N'IF OBJECT_ID(N''[__EFMigrationsHistory_Bootstrap]'') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory_Bootstrap] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory_Bootstrap] PRIMARY KEY ([MigrationId])
    );
END;';
  EXEC sys.sp_executesql N'IF DB_NAME() <> N''VietVan_New'' THROW 51000, ''Fresh baseline requires VietVan_New.'', 1;
IF EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
           WHERE NOT (s.name=N''dbo'' AND t.name=N''__EFMigrationsHistory_Bootstrap''))
    THROW 51001, ''Fresh baseline requires an empty database.'', 1;';
  EXEC sys.sp_executesql N'CREATE TABLE [LiteraryWorks] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(250) NOT NULL,
    [Author] nvarchar(150) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Content] nvarchar(max) NOT NULL,
    [CoverImageUrl] nvarchar(max) NULL,
    [RequiredTierLevel] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_LiteraryWorks] PRIMARY KEY ([Id])
);';
  EXEC sys.sp_executesql N'CREATE TABLE [PaymentWebhookEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [Provider] nvarchar(20) NOT NULL,
    [ProviderEventId] nvarchar(100) NOT NULL,
    [PayloadHash] nvarchar(64) NOT NULL,
    [Outcome] nvarchar(100) NOT NULL,
    [ReceivedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PaymentWebhookEvents] PRIMARY KEY ([Id])
);';
  EXEC sys.sp_executesql N'CREATE TABLE [SubscriptionPackages] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [Code] nvarchar(32) NULL,
    [BillingPeriod] nvarchar(16) NULL,
    [Price] decimal(18,2) NOT NULL,
    [DurationInDays] int NOT NULL,
    [TierLevel] int NOT NULL,
    [Description] nvarchar(1000) NOT NULL,
    [Benefits] nvarchar(3000) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_SubscriptionPackages] PRIMARY KEY ([Id])
);';
  EXEC sys.sp_executesql N'CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Username] nvarchar(50) NOT NULL,
    [Email] nvarchar(150) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [VipExpiresAt] datetime2 NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);';
  EXEC sys.sp_executesql N'CREATE TABLE [DramaProductions] (
    [Id] int NOT NULL IDENTITY,
    [LiteraryWorkId] int NULL,
    [Title] nvarchar(250) NOT NULL,
    [Description] nvarchar(3000) NULL,
    [IsPublished] bit NOT NULL,
    [PreviewDurationMs] int NOT NULL,
    [AccessLevel] nvarchar(30) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_DramaProductions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DramaProductions_LiteraryWorks_LiteraryWorkId] FOREIGN KEY ([LiteraryWorkId]) REFERENCES [LiteraryWorks] ([Id])
);';
  EXEC sys.sp_executesql N'CREATE TABLE [PaymentOrders] (
    [Id] bigint NOT NULL IDENTITY,
    [OrderCode] nvarchar(30) NOT NULL,
    [UserId] int NOT NULL,
    [PackageId] int NOT NULL,
    [PackageNameSnapshot] nvarchar(100) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [DurationDaysSnapshot] int NOT NULL,
    [TierLevelSnapshot] int NOT NULL,
    [BillingPeriodSnapshot] nvarchar(16) NULL,
    [Currency] nvarchar(3) NOT NULL,
    [Provider] nvarchar(20) NOT NULL,
    [Status] nvarchar(25) NOT NULL,
    [IdempotencyKey] nvarchar(100) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    [PaidAt] datetime2 NULL,
    [ReviewReason] nvarchar(500) NULL,
    CONSTRAINT [PK_PaymentOrders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentOrders_SubscriptionPackages_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [SubscriptionPackages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PaymentOrders_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);';
  EXEC sys.sp_executesql N'CREATE TABLE [RevenueTransactions] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [PackageId] int NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [PaymentDate] datetime2 NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_RevenueTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RevenueTransactions_SubscriptionPackages_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [SubscriptionPackages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RevenueTransactions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);';
  EXEC sys.sp_executesql N'CREATE TABLE [DramaAssets] (
    [Id] int NOT NULL IDENTITY,
    [DramaProductionId] int NOT NULL,
    [Kind] nvarchar(30) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [OriginalFileName] nvarchar(260) NOT NULL,
    [RelativePath] nvarchar(600) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [IsPreview] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DramaAssets] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DramaAssets_DramaProductions_DramaProductionId] FOREIGN KEY ([DramaProductionId]) REFERENCES [DramaProductions] ([Id]) ON DELETE CASCADE
);';
  EXEC sys.sp_executesql N'CREATE TABLE [PaymentAuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [PaymentOrderId] bigint NULL,
    [ActorUserId] int NULL,
    [Action] nvarchar(100) NOT NULL,
    [Detail] nvarchar(1000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PaymentAuditLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentAuditLogs_PaymentOrders_PaymentOrderId] FOREIGN KEY ([PaymentOrderId]) REFERENCES [PaymentOrders] ([Id]) ON DELETE NO ACTION
);';
  EXEC sys.sp_executesql N'CREATE TABLE [PaymentBankTransactions] (
    [Id] bigint NOT NULL IDENTITY,
    [Provider] nvarchar(20) NOT NULL,
    [ProviderTransactionId] nvarchar(100) NOT NULL,
    [PaymentOrderId] bigint NULL,
    [Amount] decimal(18,2) NOT NULL,
    [Currency] nvarchar(3) NOT NULL,
    [AccountNumberMasked] nvarchar(30) NULL,
    [ClaimedOrderCode] nvarchar(30) NULL,
    [Status] nvarchar(25) NOT NULL,
    [ReviewReason] nvarchar(500) NOT NULL,
    [ReceivedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PaymentBankTransactions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PaymentBankTransactions_PaymentOrders_PaymentOrderId] FOREIGN KEY ([PaymentOrderId]) REFERENCES [PaymentOrders] ([Id]) ON DELETE NO ACTION
);';
  EXEC sys.sp_executesql N'CREATE TABLE [UserSubscriptions] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [PackageId] int NOT NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [ActivationPaymentOrderId] bigint NULL,
    [GrantedTierLevel] int NULL,
    CONSTRAINT [PK_UserSubscriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UserSubscriptions_PaymentOrders_ActivationPaymentOrderId] FOREIGN KEY ([ActivationPaymentOrderId]) REFERENCES [PaymentOrders] ([Id]),
    CONSTRAINT [FK_UserSubscriptions_SubscriptionPackages_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [SubscriptionPackages] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_UserSubscriptions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
);';
  EXEC sys.sp_executesql N'CREATE TABLE [DramaScenes] (
    [Id] int NOT NULL IDENTITY,
    [DramaProductionId] int NOT NULL,
    [SceneKey] nvarchar(80) NOT NULL,
    [Title] nvarchar(250) NOT NULL,
    [SortOrder] int NOT NULL,
    [StartMs] int NOT NULL,
    [EndMs] int NOT NULL,
    [AudioOffsetMs] int NOT NULL,
    [BackgroundAssetId] int NULL,
    [AudioAssetId] int NULL,
    [VideoAssetId] int NULL,
    [PreviewAudioAssetId] int NULL,
    [PreviewVideoAssetId] int NULL,
    CONSTRAINT [PK_DramaScenes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaAssets_AudioAssetId] FOREIGN KEY ([AudioAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaAssets_BackgroundAssetId] FOREIGN KEY ([BackgroundAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaAssets_PreviewAudioAssetId] FOREIGN KEY ([PreviewAudioAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaAssets_PreviewVideoAssetId] FOREIGN KEY ([PreviewVideoAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaAssets_VideoAssetId] FOREIGN KEY ([VideoAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaScenes_DramaProductions_DramaProductionId] FOREIGN KEY ([DramaProductionId]) REFERENCES [DramaProductions] ([Id]) ON DELETE CASCADE
);';
  EXEC sys.sp_executesql N'CREATE TABLE [DramaHotspots] (
    [Id] int NOT NULL IDENTITY,
    [DramaSceneId] int NOT NULL,
    [VisualAssetId] int NULL,
    [Kind] nvarchar(30) NOT NULL,
    [NameVi] nvarchar(200) NOT NULL,
    [NameEn] nvarchar(200) NULL,
    [MeaningVi] nvarchar(max) NOT NULL,
    [MeaningEn] nvarchar(max) NULL,
    [X] float NOT NULL,
    [Y] float NOT NULL,
    [Width] float NOT NULL,
    [Height] float NOT NULL,
    [StartMs] int NULL,
    [EndMs] int NULL,
    [ZIndex] int NOT NULL,
    [IsEnabled] bit NOT NULL,
    [ActionKey] nvarchar(120) NULL,
    [TargetSegmentIdsJson] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_DramaHotspots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DramaHotspots_DramaAssets_VisualAssetId] FOREIGN KEY ([VisualAssetId]) REFERENCES [DramaAssets] ([Id]),
    CONSTRAINT [FK_DramaHotspots_DramaScenes_DramaSceneId] FOREIGN KEY ([DramaSceneId]) REFERENCES [DramaScenes] ([Id]) ON DELETE CASCADE
);';
  EXEC sys.sp_executesql N'CREATE TABLE [CulturalInsightViews] (
    [UserId] int NOT NULL,
    [DramaProductionId] int NOT NULL,
    [DramaHotspotId] int NOT NULL,
    [ViewedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_CulturalInsightViews] PRIMARY KEY ([UserId], [DramaProductionId], [DramaHotspotId]),
    CONSTRAINT [FK_CulturalInsightViews_DramaHotspots_DramaHotspotId] FOREIGN KEY ([DramaHotspotId]) REFERENCES [DramaHotspots] ([Id]),
    CONSTRAINT [FK_CulturalInsightViews_DramaProductions_DramaProductionId] FOREIGN KEY ([DramaProductionId]) REFERENCES [DramaProductions] ([Id]),
    CONSTRAINT [FK_CulturalInsightViews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_CulturalInsightViews_DramaHotspotId] ON [CulturalInsightViews] ([DramaHotspotId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_CulturalInsightViews_DramaProductionId] ON [CulturalInsightViews] ([DramaProductionId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaAssets_DramaProductionId_Kind] ON [DramaAssets] ([DramaProductionId], [Kind]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaHotspots_DramaSceneId_ZIndex] ON [DramaHotspots] ([DramaSceneId], [ZIndex]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaHotspots_VisualAssetId] ON [DramaHotspots] ([VisualAssetId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaProductions_LiteraryWorkId] ON [DramaProductions] ([LiteraryWorkId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_AudioAssetId] ON [DramaScenes] ([AudioAssetId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_BackgroundAssetId] ON [DramaScenes] ([BackgroundAssetId]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_DramaScenes_DramaProductionId_SceneKey] ON [DramaScenes] ([DramaProductionId], [SceneKey]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_DramaProductionId_SortOrder] ON [DramaScenes] ([DramaProductionId], [SortOrder]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_PreviewAudioAssetId] ON [DramaScenes] ([PreviewAudioAssetId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_PreviewVideoAssetId] ON [DramaScenes] ([PreviewVideoAssetId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_DramaScenes_VideoAssetId] ON [DramaScenes] ([VideoAssetId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_LiteraryWorks_IsActive_RequiredTierLevel] ON [LiteraryWorks] ([IsActive], [RequiredTierLevel]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_PaymentAuditLogs_PaymentOrderId_CreatedAt] ON [PaymentAuditLogs] ([PaymentOrderId], [CreatedAt]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_PaymentBankTransactions_PaymentOrderId] ON [PaymentBankTransactions] ([PaymentOrderId]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_PaymentBankTransactions_Provider_ProviderTransactionId] ON [PaymentBankTransactions] ([Provider], [ProviderTransactionId]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_PaymentOrders_OrderCode] ON [PaymentOrders] ([OrderCode]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_PaymentOrders_PackageId] ON [PaymentOrders] ([PackageId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_PaymentOrders_Status_ExpiresAt] ON [PaymentOrders] ([Status], [ExpiresAt]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_PaymentOrders_UserId_IdempotencyKey] ON [PaymentOrders] ([UserId], [IdempotencyKey]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_PaymentWebhookEvents_Provider_ProviderEventId] ON [PaymentWebhookEvents] ([Provider], [ProviderEventId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_RevenueTransactions_PackageId] ON [RevenueTransactions] ([PackageId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_RevenueTransactions_Status_PaymentDate] ON [RevenueTransactions] ([Status], [PaymentDate]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_RevenueTransactions_UserId] ON [RevenueTransactions] ([UserId]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_SubscriptionPackages_Code] ON [SubscriptionPackages] ([Code]) WHERE [Code] IS NOT NULL;';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_SubscriptionPackages_TierLevel] ON [SubscriptionPackages] ([TierLevel]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);';
  EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_UserSubscriptions_ActivationPaymentOrderId] ON [UserSubscriptions] ([ActivationPaymentOrderId]) WHERE [ActivationPaymentOrderId] IS NOT NULL;';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_UserSubscriptions_PackageId] ON [UserSubscriptions] ([PackageId]);';
  EXEC sys.sp_executesql N'CREATE INDEX [IX_UserSubscriptions_UserId_IsActive] ON [UserSubscriptions] ([UserId], [IsActive]);';
  EXEC sys.sp_executesql N'ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Amount CHECK(Amount > 0 AND Amount = FLOOR(Amount));
ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Duration CHECK(DurationDaysSnapshot > 0);
ALTER TABLE dbo.PaymentOrders ADD CONSTRAINT CK_PO_Status CHECK(Status IN(''Pending'',''Paid'',''Expired'',''Cancelled'',''Failed'',''RequiresReview'',''Refunded''));
ALTER TABLE dbo.DramaScenes ADD CONSTRAINT CK_DramaScenes_Time CHECK (StartMs >= 0 AND EndMs > StartMs AND AudioOffsetMs >= 0);
ALTER TABLE dbo.DramaHotspots ADD CONSTRAINT CK_DramaHotspots_TargetsJson CHECK(ISJSON(TargetSegmentIdsJson) = 1);
INSERT INTO dbo.SubscriptionPackages (Name,Code,BillingPeriod,Price,DurationInDays,TierLevel,Description,Benefits,IsActive,CreatedAt)
SELECT v.Name,v.Code,v.Period,v.Price,v.Days,v.Tier,v.Description,v.Benefits,1,SYSUTCDATETIME()
FROM (VALUES
 (N''Free'',N''FREE'',N''Free'',0,0,0,N''Xem thử'',N''Preview và 5 insight mỗi vở''),
 (N''Plus Monthly'',N''PLUS_MONTHLY'',N''Monthly'',50000,30,2,N''Thanh toán hàng tháng'',N''Xem trọn vẹn và 5 insight mỗi vở''),
 (N''Pro Monthly'',N''PRO_MONTHLY'',N''Monthly'',131000,30,3,N''Thanh toán hàng tháng'',N''Không giới hạn insight và công cụ học tập Pro''),
 (N''Pro Yearly'',N''PRO_YEARLY'',N''Yearly'',1287000,365,3,N''Thanh toán hàng năm'',N''Không giới hạn insight và công cụ học tập Pro'')
) v(Name,Code,Period,Price,Days,Tier,Description,Benefits)
WHERE NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPackages p WHERE p.Code=v.Code);';
  EXEC sys.sp_executesql N'INSERT INTO [__EFMigrationsHistory_Bootstrap] ([MigrationId], [ProductVersion])
VALUES (N''20261124071000_FreshBaseline'', N''8.0.10'');';
  COMMIT TRANSACTION;
  PRINT N'VietVan_New baseline completed.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
