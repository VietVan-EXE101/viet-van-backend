/* Metadata avatar mới, không thêm cột vào Users hoặc sửa dữ liệu đăng nhập.
   Sao lưu và chọn Somee/VietVan_New. Mặc định preview, đổi @ApplyChanges = 1 để áp dụng.
   Không bắt buộc cho chức năng kịch; thiếu bảng thì chỉ các API avatar báo schema 503. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ApplyChanges bit = 0;
IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New first.', 1;
IF @@TRANCOUNT <> 0 THROW 51001, 'Run outside an existing transaction.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL THROW 51002, 'Users table is missing or not visible.', 1;
IF OBJECT_ID(N'dbo.UserAvatars') IS NOT NULL
BEGIN
    SELECT N'EXISTS - inspect; no changes performed' AS Status;
    SELECT c.name, TYPE_NAME(c.user_type_id) AS SqlType,c.max_length,c.is_nullable
    FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.UserAvatars');
    RETURN;
END;
IF @ApplyChanges=0
BEGIN
    SELECT N'PREVIEW ONLY - will create dbo.UserAvatars, no user rows will change' AS Status;
    RETURN;
END;
BEGIN TRY
    BEGIN TRANSACTION;
    CREATE TABLE dbo.UserAvatars (
        UserId int NOT NULL CONSTRAINT PK_UserAvatars PRIMARY KEY,
        ObjectKey nvarchar(600) NOT NULL,FileName nvarchar(260) NOT NULL,
        ContentType nvarchar(100) NOT NULL,FileSize bigint NOT NULL,UpdatedAt datetime2(7) NOT NULL,
        CONSTRAINT FK_UserAvatars_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id)
    );
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
