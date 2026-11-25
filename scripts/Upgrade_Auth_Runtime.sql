-- Chỉ áp dụng sau khi kiểm tra và sao lưu VietVan_New. Không sửa dữ liệu tài khoản cũ.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New.', 1;
    IF OBJECT_ID(N'dbo.Users',N'U') IS NULL THROW 51001, 'dbo.Users is missing.', 1;
    IF OBJECT_ID(N'dbo.UserAuthStates') IS NOT NULL OR OBJECT_ID(N'dbo.UserExternalLogins') IS NOT NULL OR OBJECT_ID(N'dbo.EmailVerificationTokens') IS NOT NULL
        THROW 51002, 'Auth tables already exist; stop and reconcile schema before applying.', 1;
    BEGIN TRANSACTION;
    CREATE TABLE dbo.UserAuthStates (
        UserId int NOT NULL CONSTRAINT PK_UserAuthStates PRIMARY KEY,
        EmailVerified bit NOT NULL, EmailVerifiedAt datetime2 NULL, IsLocked bit NOT NULL,
        CONSTRAINT FK_UserAuthStates_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
    CREATE TABLE dbo.UserExternalLogins (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserExternalLogins PRIMARY KEY,
        UserId int NOT NULL, Provider nvarchar(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderSubject nvarchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CreatedAt datetime2 NOT NULL, LastLoginAt datetime2 NULL,
        CONSTRAINT FK_UserExternalLogins_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_UserExternalLogins_Provider_Subject ON dbo.UserExternalLogins(Provider,ProviderSubject);
    CREATE INDEX IX_UserExternalLogins_UserId ON dbo.UserExternalLogins(UserId);
    CREATE TABLE dbo.EmailVerificationTokens (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailVerificationTokens PRIMARY KEY,
        UserId int NOT NULL, EmailSnapshot nvarchar(150) NOT NULL, TokenHash binary(32) NOT NULL,
        CreatedAt datetime2 NOT NULL, ExpiresAt datetime2 NOT NULL, ConsumedAt datetime2 NULL,
        CONSTRAINT FK_EmailVerificationTokens_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_EmailVerificationTokens_TokenHash ON dbo.EmailVerificationTokens(TokenHash);
    CREATE INDEX IX_EmailVerificationTokens_UserId_CreatedAt ON dbo.EmailVerificationTokens(UserId,CreatedAt);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
