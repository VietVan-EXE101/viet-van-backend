/*
  Sao lưu VietVan_New trước khi chạy. Trong SSMS, chọn VietVan_New và chạy toàn bộ file.
  Bổ sung các cột preview còn thiếu cho schema Drama hiện có; không tạo bảng,
  không xóa dữ liệu, không sửa migration history. Nếu cột đã tồn tại nhưng sai
  kiểu hoặc dữ liệu FK không hợp lệ, script dừng và rollback để kiểm tra thủ công.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;
IF OBJECT_ID(N'dbo.DramaProductions',N'U') IS NULL OR OBJECT_ID(N'dbo.DramaAssets',N'U') IS NULL OR OBJECT_ID(N'dbo.DramaScenes',N'U') IS NULL
    THROW 51001, 'Required Drama tables are missing or not visible. Inspect the database first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.DramaProductions',N'PreviewDurationMs') IS NULL
        ALTER TABLE dbo.DramaProductions ADD PreviewDurationMs int NOT NULL CONSTRAINT DF_DramaProductions_PreviewDurationMs DEFAULT (120000) WITH VALUES;
    IF COL_LENGTH(N'dbo.DramaAssets',N'IsPreview') IS NULL
        ALTER TABLE dbo.DramaAssets ADD IsPreview bit NOT NULL CONSTRAINT DF_DramaAssets_IsPreview DEFAULT (0) WITH VALUES;
    IF COL_LENGTH(N'dbo.DramaScenes',N'PreviewAudioAssetId') IS NULL
        ALTER TABLE dbo.DramaScenes ADD PreviewAudioAssetId int NULL;
    IF COL_LENGTH(N'dbo.DramaScenes',N'PreviewVideoAssetId') IS NULL
        ALTER TABLE dbo.DramaScenes ADD PreviewVideoAssetId int NULL;

    IF EXISTS (
        SELECT 1 FROM (VALUES (N'DramaProductions',N'PreviewDurationMs',N'int',0),
                              (N'DramaAssets',N'IsPreview',N'bit',0),
                              (N'DramaScenes',N'PreviewAudioAssetId',N'int',1),
                              (N'DramaScenes',N'PreviewVideoAssetId',N'int',1)) v(TableName,ColumnName,TypeName,Nullable)
        LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+v.TableName) AND c.name=v.ColumnName
        WHERE c.column_id IS NULL OR TYPE_NAME(c.user_type_id)<>v.TypeName OR c.is_nullable<>v.Nullable
    ) THROW 51002, 'An existing preview column has an incompatible type or nullability.', 1;

    -- Dynamic SQL avoids compilation against columns that did not exist before this batch.
    EXEC sys.sp_executesql N'
      IF EXISTS (SELECT 1 FROM dbo.DramaScenes s LEFT JOIN dbo.DramaAssets a ON a.Id=s.PreviewAudioAssetId WHERE s.PreviewAudioAssetId IS NOT NULL AND a.Id IS NULL)
       OR EXISTS (SELECT 1 FROM dbo.DramaScenes s LEFT JOIN dbo.DramaAssets a ON a.Id=s.PreviewVideoAssetId WHERE s.PreviewVideoAssetId IS NOT NULL AND a.Id IS NULL)
        THROW 51003, ''Invalid preview asset references; inspect existing data.'', 1;';
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns f JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id
                   WHERE f.parent_object_id=OBJECT_ID(N'dbo.DramaScenes') AND c.name=N'PreviewAudioAssetId' AND f.referenced_object_id=OBJECT_ID(N'dbo.DramaAssets'))
        EXEC(N'ALTER TABLE dbo.DramaScenes WITH CHECK ADD CONSTRAINT FK_DramaScenes_PreviewAudioAsset FOREIGN KEY (PreviewAudioAssetId) REFERENCES dbo.DramaAssets(Id);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns f JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id
                   WHERE f.parent_object_id=OBJECT_ID(N'dbo.DramaScenes') AND c.name=N'PreviewVideoAssetId' AND f.referenced_object_id=OBJECT_ID(N'dbo.DramaAssets'))
        EXEC(N'ALTER TABLE dbo.DramaScenes WITH CHECK ADD CONSTRAINT FK_DramaScenes_PreviewVideoAsset FOREIGN KEY (PreviewVideoAssetId) REFERENCES dbo.DramaAssets(Id);');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT t.name AS TableName, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType, c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND ((t.name=N'DramaProductions' AND c.name=N'PreviewDurationMs')
 OR (t.name=N'DramaAssets' AND c.name=N'IsPreview')
 OR (t.name=N'DramaScenes' AND c.name IN (N'PreviewAudioAssetId',N'PreviewVideoAssetId')))
ORDER BY t.name,c.name;
