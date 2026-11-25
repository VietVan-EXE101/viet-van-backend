/*
  VIET VAN PAYMENT/SEPAY - SAFE ADDITIVE SQL PATCH
  Chạy SAU VietVanDb.sql và scripts/DramaUpgrade.sql nếu bật chức năng drama.
  Không xoá/chỉnh sửa đơn hàng/tài khoản cũ. BACKUP DATABASE TRƯỚC KHI CHẠY.
  Có thể chạy lặp lại; kiểm tra schema thực tế trước khi apply ở production.
*/
USE VietVanDb;
GO
IF COL_LENGTH('dbo.Users','VipExpiresAt') IS NULL
  ALTER TABLE dbo.Users ADD VipExpiresAt DATETIME2 NULL;
IF COL_LENGTH('dbo.SubscriptionPackages','Description') IS NULL
  ALTER TABLE dbo.SubscriptionPackages ADD Description NVARCHAR(1000) NOT NULL CONSTRAINT DF_PackageDescription DEFAULT N'';
IF COL_LENGTH('dbo.SubscriptionPackages','Benefits') IS NULL
  ALTER TABLE dbo.SubscriptionPackages ADD Benefits NVARCHAR(3000) NOT NULL CONSTRAINT DF_PackageBenefits DEFAULT N'';
IF COL_LENGTH('dbo.SubscriptionPackages','IsActive') IS NULL
  ALTER TABLE dbo.SubscriptionPackages ADD IsActive BIT NOT NULL CONSTRAINT DF_PackageIsActive DEFAULT 1;
IF COL_LENGTH('dbo.SubscriptionPackages','CreatedAt') IS NULL
  ALTER TABLE dbo.SubscriptionPackages ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PackageCreatedAt DEFAULT SYSUTCDATETIME();
IF COL_LENGTH('dbo.SubscriptionPackages','UpdatedAt') IS NULL
  ALTER TABLE dbo.SubscriptionPackages ADD UpdatedAt DATETIME2 NULL;
IF OBJECT_ID('dbo.DramaProductions','U') IS NOT NULL AND COL_LENGTH('dbo.DramaProductions','AccessLevel') IS NULL
  ALTER TABLE dbo.DramaProductions ADD AccessLevel NVARCHAR(30) NOT NULL CONSTRAINT DF_DramaAccessLevel DEFAULT N'Public';
GO
IF OBJECT_ID('dbo.PaymentOrders','U') IS NULL
BEGIN
 CREATE TABLE dbo.PaymentOrders (
    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentOrders PRIMARY KEY,
    OrderCode NVARCHAR(30) NOT NULL,
    UserId INT NOT NULL,
    PackageId INT NOT NULL,
    PackageNameSnapshot NVARCHAR(100) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    DurationDaysSnapshot INT NOT NULL,
    TierLevelSnapshot INT NOT NULL,
    Currency NVARCHAR(3) NOT NULL CONSTRAINT DF_PO_Currency DEFAULT 'VND',
    Provider NVARCHAR(20) NOT NULL CONSTRAINT DF_PO_Provider DEFAULT 'SePay',
    Status NVARCHAR(25) NOT NULL CONSTRAINT DF_PO_Status DEFAULT 'Pending',
    IdempotencyKey NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PO_Created DEFAULT SYSUTCDATETIME(),
    ExpiresAt DATETIME2 NOT NULL,
    PaidAt DATETIME2 NULL,
    ReviewReason NVARCHAR(500) NULL,
    CONSTRAINT FK_PO_User FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT FK_PO_Package FOREIGN KEY(PackageId) REFERENCES dbo.SubscriptionPackages(Id),
    CONSTRAINT CK_PO_Amount CHECK(Amount > 0 AND Amount = FLOOR(Amount)),
    CONSTRAINT CK_PO_Duration CHECK(DurationDaysSnapshot > 0),
    CONSTRAINT CK_PO_Status CHECK(Status IN('Pending','Paid','Expired','Cancelled','Failed','RequiresReview','Refunded'))
 );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PO_OrderCode' AND object_id=OBJECT_ID('dbo.PaymentOrders'))
 CREATE UNIQUE INDEX UX_PO_OrderCode ON dbo.PaymentOrders(OrderCode);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PO_IdemKey' AND object_id=OBJECT_ID('dbo.PaymentOrders'))
 CREATE UNIQUE INDEX UX_PO_IdemKey ON dbo.PaymentOrders(UserId, IdempotencyKey);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_PO_Status_Expires' AND object_id=OBJECT_ID('dbo.PaymentOrders'))
 CREATE INDEX IX_PO_Status_Expires ON dbo.PaymentOrders(Status,ExpiresAt);
GO
IF COL_LENGTH('dbo.UserSubscriptions','GrantedTierLevel') IS NULL
 ALTER TABLE dbo.UserSubscriptions ADD GrantedTierLevel INT NULL;
IF COL_LENGTH('dbo.UserSubscriptions','ActivationPaymentOrderId') IS NULL
 ALTER TABLE dbo.UserSubscriptions ADD ActivationPaymentOrderId BIGINT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_US_ActivationPaymentOrder')
 ALTER TABLE dbo.UserSubscriptions ADD CONSTRAINT FK_US_ActivationPaymentOrder FOREIGN KEY(ActivationPaymentOrderId) REFERENCES dbo.PaymentOrders(Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_US_ActivationPaymentOrder' AND object_id=OBJECT_ID('dbo.UserSubscriptions'))
 CREATE UNIQUE INDEX UX_US_ActivationPaymentOrder ON dbo.UserSubscriptions(ActivationPaymentOrderId)
   WHERE ActivationPaymentOrderId IS NOT NULL;
GO
IF OBJECT_ID('dbo.PaymentBankTransactions','U') IS NULL
BEGIN
 CREATE TABLE dbo.PaymentBankTransactions (
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentBankTx PRIMARY KEY,
   Provider NVARCHAR(20) NOT NULL,
   ProviderTransactionId NVARCHAR(100) NOT NULL,
   PaymentOrderId BIGINT NULL,
   Amount DECIMAL(18,2) NOT NULL,
   Currency NVARCHAR(3) NOT NULL,
   AccountNumberMasked NVARCHAR(30) NULL,
   ClaimedOrderCode NVARCHAR(30) NULL,
   Status NVARCHAR(25) NOT NULL,
   ReviewReason NVARCHAR(500) NOT NULL,
   ReceivedAt DATETIME2 NOT NULL CONSTRAINT DF_PBT_Received DEFAULT SYSUTCDATETIME(),
   CONSTRAINT FK_PBT_Order FOREIGN KEY(PaymentOrderId) REFERENCES dbo.PaymentOrders(Id)
 );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PBT_ProviderTransaction' AND object_id=OBJECT_ID('dbo.PaymentBankTransactions'))
 CREATE UNIQUE INDEX UX_PBT_ProviderTransaction ON dbo.PaymentBankTransactions(Provider,ProviderTransactionId);
GO
IF OBJECT_ID('dbo.PaymentWebhookEvents','U') IS NULL
BEGIN
 CREATE TABLE dbo.PaymentWebhookEvents (
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentWebhookEvents PRIMARY KEY,
   Provider NVARCHAR(20) NOT NULL,
   ProviderEventId NVARCHAR(100) NOT NULL,
   PayloadHash NVARCHAR(64) NOT NULL,
   Outcome NVARCHAR(100) NOT NULL,
   ReceivedAt DATETIME2 NOT NULL CONSTRAINT DF_PWE_Received DEFAULT SYSUTCDATETIME()
 );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PWE_ProviderEvent' AND object_id=OBJECT_ID('dbo.PaymentWebhookEvents'))
 CREATE UNIQUE INDEX UX_PWE_ProviderEvent ON dbo.PaymentWebhookEvents(Provider,ProviderEventId);
GO
IF OBJECT_ID('dbo.PaymentAuditLogs','U') IS NULL
BEGIN
 CREATE TABLE dbo.PaymentAuditLogs (
   Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PaymentAuditLogs PRIMARY KEY,
   PaymentOrderId BIGINT NULL,
   ActorUserId INT NULL,
   Action NVARCHAR(100) NOT NULL,
   Detail NVARCHAR(1000) NOT NULL,
   CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_PAL_Created DEFAULT SYSUTCDATETIME(),
   CONSTRAINT FK_PAL_Order FOREIGN KEY(PaymentOrderId) REFERENCES dbo.PaymentOrders(Id)
 );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_PAL_OrderDate' AND object_id=OBJECT_ID('dbo.PaymentAuditLogs'))
 CREATE INDEX IX_PAL_OrderDate ON dbo.PaymentAuditLogs(PaymentOrderId,CreatedAt);
GO
PRINT N'PaymentUpgrade xong: bảng cũ giữ nguyên, các bảng mới đã được kiểm tra.';
