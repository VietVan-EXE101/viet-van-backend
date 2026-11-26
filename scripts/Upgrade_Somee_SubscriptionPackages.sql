/*
  VietVan_New / Somee: bổ sung 6 cột SubscriptionPackages thiếu trong chẩn đoán.
  Chọn kết nối Somee và database VietVan_New trong SSMS trước khi chạy.
  Mặc định CHỈ XEM TRƯỚC. Sau khi sao lưu database và đọc kết quả, đổi
  @ApplyChanges từ 0 thành 1 rồi chạy lại TOÀN BỘ script khi Backend đã dừng.

  Không bootstrap, seed, DROP, DELETE hoặc UPDATE giá trị cột hiện có.
  Các cột mới: Benefits/Description = chuỗi rỗng, Code/BillingPeriod/UpdatedAt
  = NULL; CreatedAt = thời điểm nâng cấp UTC (không phải ngày tạo lịch sử).
  Nếu có cột Benefit cũ, giữ nguyên và không tự sao chép/đoán nội dung.
  Không được gọi tự động khi Backend khởi động.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ApplyChanges bit = 0;

IF DB_NAME() <> N'VietVan_New'
    THROW 51000, 'Select the imported VietVan_New database on Somee first.', 1;
IF @@TRANCOUNT <> 0
    THROW 51001, 'Run this script outside an existing transaction.', 1;
IF OBJECT_ID(N'dbo.SubscriptionPackages', N'U') IS NULL
    THROW 51002, 'dbo.SubscriptionPackages is missing or not visible. Do not run a baseline.', 1;

-- Độ dài nvarchar trong sys.columns tính bằng byte, không phải số ký tự.
DECLARE @Expected TABLE (
    ColumnName sysname NOT NULL,
    TypeName sysname NOT NULL,
    MaxLength smallint NOT NULL,
    IsNullable bit NOT NULL,
    Scale tinyint NOT NULL
);
INSERT INTO @Expected (ColumnName, TypeName, MaxLength, IsNullable, Scale) VALUES
    (N'Benefits', N'nvarchar', 6000, 0, 0),
    (N'BillingPeriod', N'nvarchar', 32, 1, 0),
    (N'Code', N'nvarchar', 64, 1, 0),
    (N'CreatedAt', N'datetime2', 8, 0, 7),
    (N'Description', N'nvarchar', 2000, 0, 0),
    (N'UpdatedAt', N'datetime2', 8, 1, 7);

SELECT DB_NAME() AS DatabaseName, @ApplyChanges AS ApplyChanges;
SELECT e.ColumnName, e.TypeName AS ExpectedType, e.MaxLength AS ExpectedLengthBytes,
       e.IsNullable AS ExpectedNullable,
       CASE WHEN c.column_id IS NULL THEN N'WILL ADD'
            WHEN t.name = e.TypeName AND c.max_length = e.MaxLength
                 AND c.is_nullable = e.IsNullable AND c.scale = e.Scale
                 AND c.is_computed = 0 THEN N'ALREADY MATCHES'
            ELSE N'INCOMPATIBLE - STOP' END AS Status
FROM @Expected e
LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.SubscriptionPackages') AND c.name = e.ColumnName
LEFT JOIN sys.types t ON t.user_type_id = c.user_type_id
ORDER BY e.ColumnName;

IF EXISTS (
    SELECT 1 FROM @Expected e
    JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.SubscriptionPackages') AND c.name = e.ColumnName
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE t.name <> e.TypeName OR c.max_length <> e.MaxLength
       OR c.is_nullable <> e.IsNullable OR c.scale <> e.Scale OR c.is_computed <> 0
)
    THROW 51003, 'Existing target column is incompatible. Review metadata; no existing column will be altered.', 1;

IF @ApplyChanges = 0
BEGIN
    PRINT 'PREVIEW ONLY: no schema/data changes. Back up first; set @ApplyChanges = 1 to apply.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @RowsBefore bigint = (SELECT COUNT_BIG(*) FROM dbo.SubscriptionPackages);

    -- Dynamic batches avoid compiling references to newly added columns early.
    -- Unnamed defaults avoid collisions with constraint names in imported schemas.
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'Benefits') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD Benefits nvarchar(3000) NOT NULL DEFAULT (N'''') WITH VALUES;');
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'BillingPeriod') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD BillingPeriod nvarchar(16) NULL;');
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'Code') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD Code nvarchar(32) NULL;');
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'CreatedAt') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD CreatedAt datetime2(7) NOT NULL DEFAULT (SYSUTCDATETIME()) WITH VALUES;');
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'Description') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD Description nvarchar(1000) NOT NULL DEFAULT (N'''') WITH VALUES;');
    IF COL_LENGTH(N'dbo.SubscriptionPackages', N'UpdatedAt') IS NULL
        EXEC(N'ALTER TABLE dbo.SubscriptionPackages ADD UpdatedAt datetime2(7) NULL;');

    IF EXISTS (
        SELECT 1 FROM @Expected e
        LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID(N'dbo.SubscriptionPackages') AND c.name = e.ColumnName
        WHERE c.column_id IS NULL
    )
        THROW 51004, 'A required column is still missing; rolling back.', 1;
    IF @RowsBefore <> (SELECT COUNT_BIG(*) FROM dbo.SubscriptionPackages)
        THROW 51005, 'Package row count changed; rolling back.', 1;

    COMMIT TRANSACTION;
    SELECT N'APPLIED' AS Result, @RowsBefore AS PackageRowsPreserved;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
