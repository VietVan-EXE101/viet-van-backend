/*
 Dự thảo nâng cấp riêng cho VietVan_New. KHÔNG tự động chạy: đối chiếu schema thực,
 backup và kiểm thử trên bản sao trước. Không thay đổi bảng Payment.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;
    IF OBJECT_ID(N'dbo.Users',N'U') IS NULL THROW 51001, 'dbo.Users is missing.', 1;
    IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
                   WHERE c.object_id=OBJECT_ID(N'dbo.Users') AND c.name=N'Id' AND t.name=N'int' AND c.is_nullable=0)
        THROW 51002, 'dbo.Users.Id must be a non-null int.', 1;
    -- Không đoán cấu trúc bảng hoặc cột đã được tạo bởi hệ thống khác.
    IF COL_LENGTH(N'dbo.Users',N'EmailVerified') IS NOT NULL OR
       COL_LENGTH(N'dbo.Users',N'EmailVerifiedAt') IS NOT NULL OR
       COL_LENGTH(N'dbo.Users',N'AccountStatus') IS NOT NULL OR
       OBJECT_ID(N'dbo.UserExternalLogins') IS NOT NULL OR
       OBJECT_ID(N'dbo.EmailVerificationTokens') IS NOT NULL
        THROW 51003, 'Auth upgrade objects already exist; inspect and reconcile manually.', 1;
    BEGIN TRANSACTION;
    -- NULL: tài khoản cũ chưa có bằng chứng xác minh, không tự chuyển thành verified.
    ALTER TABLE dbo.Users ADD EmailVerified bit NULL, EmailVerifiedAt datetime2 NULL,
        AccountStatus nvarchar(20) NOT NULL CONSTRAINT DF_Users_AccountStatus DEFAULT N'Active';
    CREATE TABLE dbo.UserExternalLogins (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserExternalLogins PRIMARY KEY,
        UserId int NOT NULL, Provider nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderSubject nvarchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAt datetime2 NOT NULL, LastLoginAt datetime2 NULL,
        CONSTRAINT FK_UserExternalLogins_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id)
    );
    CREATE UNIQUE INDEX UX_UserExternalLogins_Provider_Subject ON dbo.UserExternalLogins(Provider,ProviderSubject);
    CREATE INDEX IX_UserExternalLogins_UserId ON dbo.UserExternalLogins(UserId);
    CREATE TABLE dbo.EmailVerificationTokens (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailVerificationTokens PRIMARY KEY,
        UserId int NOT NULL, EmailSnapshot nvarchar(150) NOT NULL, TokenHash binary(32) NOT NULL, CreatedAt datetime2 NOT NULL,
        ExpiresAt datetime2 NOT NULL, ConsumedAt datetime2 NULL,
        CONSTRAINT FK_EmailVerificationTokens_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id)
    );
    CREATE UNIQUE INDEX UX_EmailVerificationTokens_TokenHash ON dbo.EmailVerificationTokens(TokenHash);
    CREATE INDEX IX_EmailVerificationTokens_UserId_CreatedAt ON dbo.EmailVerificationTokens(UserId,CreatedAt);
    COMMIT TRANSACTION;
    SELECT N'Users, UserExternalLogins, EmailVerificationTokens' AS UpgradedObjects;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
