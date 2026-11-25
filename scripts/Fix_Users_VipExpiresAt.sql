/*
  Bổ sung cột tương thích cho Users khi schema thiếu VipExpiresAt.
  Chọn VietVan_New trong SSMS và chạy toàn bộ file sau khi kiểm tra schema.
  Không tạo tài khoản, không cấp VIP, không đổi migration history.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;
    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL THROW 51001, 'dbo.Users was not found or is not visible.', 1;
    IF HAS_PERMS_BY_NAME(N'dbo.Users', N'OBJECT', N'VIEW DEFINITION') <> 1
        THROW 51002, 'VIEW DEFINITION permission is required for dbo.Users.', 1;
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.Users', N'VipExpiresAt') IS NULL
    BEGIN
        -- Cột nullable không gán thêm quyền VIP cho dữ liệu đã có.
        EXEC sys.sp_executesql N'ALTER TABLE dbo.Users ADD VipExpiresAt datetime2 NULL;';
        PRINT N'Đã thêm dbo.Users.VipExpiresAt (datetime2 NULL).';
    END
    ELSE IF NOT EXISTS (
        SELECT 1 FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.Users') AND c.name=N'VipExpiresAt'
          AND ty.name=N'datetime2' AND c.is_nullable=1
    )
        THROW 51003, 'VipExpiresAt exists with an incompatible type or nullability; inspect manually.', 1;
    ELSE
        PRINT N'dbo.Users.VipExpiresAt đã tồn tại và có kiểu tương thích.';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT DB_NAME() AS DatabaseName, c.name AS ColumnName, ty.name AS SqlType,
       c.scale AS DateTimeScale, c.is_nullable AS IsNullable
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.Users') AND c.name=N'VipExpiresAt';
