/* Chỉ đọc. Chạy trong SSMS trên cùng instance/database mà API sử dụng.
   Sao chép kết quả, không bao gồm token, mật khẩu hoặc nội dung dữ liệu. */
SET NOCOUNT ON;
IF DB_NAME() <> N'VietVan_New'
    THROW 51000, 'Select the imported VietVan_New database before running diagnostics.', 1;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
-- Current EF properties used by create/upload/detail (including retained legacy fields).
-- A missing column can fail SELECT even when its scene/asset table contains no rows.
WITH Expected(TableName,ColumnName) AS (
 SELECT * FROM (VALUES
 (N'DramaProductions',N'Id'),(N'DramaProductions',N'LiteraryWorkId'),(N'DramaProductions',N'Title'),
 (N'DramaProductions',N'Description'),(N'DramaProductions',N'IsPublished'),(N'DramaProductions',N'Status'),
 (N'DramaProductions',N'PublishedAt'),(N'DramaProductions',N'FreeVideoAssetId'),
 (N'DramaProductions',N'PreviewDurationMs'),(N'DramaProductions',N'AccessLevel'),
 (N'DramaProductions',N'CreatedAt'),(N'DramaProductions',N'UpdatedAt'),
 (N'DramaAssets',N'Id'),(N'DramaAssets',N'DramaProductionId'),(N'DramaAssets',N'Kind'),
 (N'DramaAssets',N'Title'),(N'DramaAssets',N'OriginalFileName'),(N'DramaAssets',N'RelativePath'),
 (N'DramaAssets',N'ContentType'),(N'DramaAssets',N'SizeBytes'),(N'DramaAssets',N'IsPreview'),
 (N'DramaAssets',N'NameVi'),(N'DramaAssets',N'NameEn'),(N'DramaAssets',N'MeaningVi'),
 (N'DramaAssets',N'MeaningEn'),(N'DramaAssets',N'DurationSeconds'),(N'DramaAssets',N'CreatedAt'),
 (N'DramaScenes',N'Id'),(N'DramaScenes',N'DramaProductionId'),(N'DramaScenes',N'SceneKey'),
 (N'DramaScenes',N'Title'),(N'DramaScenes',N'SortOrder'),(N'DramaScenes',N'StartMs'),
 (N'DramaScenes',N'EndMs'),(N'DramaScenes',N'AudioOffsetMs'),(N'DramaScenes',N'BackgroundAssetId'),
 (N'DramaScenes',N'AudioAssetId'),(N'DramaScenes',N'VideoAssetId'),
 (N'DramaScenes',N'PreviewAudioAssetId'),(N'DramaScenes',N'PreviewVideoAssetId'),
 (N'DramaHotspots',N'Id'),(N'DramaHotspots',N'DramaSceneId'),(N'DramaHotspots',N'VisualAssetId'),
 (N'DramaHotspots',N'Kind'),(N'DramaHotspots',N'NameVi'),(N'DramaHotspots',N'NameEn'),
 (N'DramaHotspots',N'MeaningVi'),(N'DramaHotspots',N'MeaningEn'),(N'DramaHotspots',N'X'),
 (N'DramaHotspots',N'Y'),(N'DramaHotspots',N'Width'),(N'DramaHotspots',N'Height'),
 (N'DramaHotspots',N'StartMs'),(N'DramaHotspots',N'EndMs'),(N'DramaHotspots',N'ZIndex'),
 (N'DramaHotspots',N'IsEnabled'),(N'DramaHotspots',N'ActionKey'),(N'DramaHotspots',N'TargetSegmentIdsJson'),
 (N'DramaManifests',N'DramaProductionId'),(N'DramaManifests',N'DraftJson'),
 (N'DramaManifests',N'PublishedJson'),(N'DramaManifests',N'PublishedLegacyJson'),
 (N'DramaManifests',N'UpdatedAt'),(N'DramaManifests',N'PublishedAt'),(N'DramaManifests',N'Revision'),
 (N'CulturalInsightViews',N'UserId'),(N'CulturalInsightViews',N'DramaProductionId'),
 (N'CulturalInsightViews',N'DramaHotspotId'),(N'CulturalInsightViews',N'ViewedAt')
 ) v(TableName,ColumnName)
)
SELECT e.TableName,e.ColumnName,
 CASE WHEN t.object_id IS NULL THEN N'MISSING_TABLE_OR_NOT_VISIBLE'
      WHEN c.column_id IS NULL THEN N'MISSING_COLUMN_OR_NOT_VISIBLE' ELSE N'PRESENT' END AS Status
FROM Expected e
LEFT JOIN sys.tables t ON t.schema_id=SCHEMA_ID(N'dbo') AND t.name=e.TableName
LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name=e.ColumnName
ORDER BY CASE WHEN c.column_id IS NULL THEN 0 ELSE 1 END,e.TableName,e.ColumnName;

SELECT s.name AS SchemaName, t.name AS TableName, c.column_id, c.name AS ColumnName,
       TYPE_NAME(c.user_type_id) AS DataType, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, dc.definition AS DefaultDefinition
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE t.name LIKE N'Drama%' OR t.name=N'CulturalInsightViews'
ORDER BY s.name,t.name,c.column_id;
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,
       SUM(CASE WHEN p.index_id IN (0,1) THEN CONVERT(bigint,p.rows) ELSE 0 END) AS ApproximateRows
FROM sys.tables t LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.name LIKE N'Drama%' OR t.name=N'CulturalInsightViews'
GROUP BY t.schema_id,t.name ORDER BY SchemaName,TableName;
SELECT fk.name AS ForeignKeyName, SCHEMA_NAME(pt.schema_id) AS ParentSchema,pt.name AS ParentTable,pc.name AS ParentColumn,
       SCHEMA_NAME(rt.schema_id) AS TargetSchema,rt.name AS TargetTable,rc.name AS TargetColumn,
       fk.delete_referential_action_desc,fk.is_disabled,fk.is_not_trusted
FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables pt ON pt.object_id=fkc.parent_object_id JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.parent_column_id
JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fkc.referenced_column_id
WHERE pt.name LIKE N'Drama%' OR rt.name LIKE N'Drama%'
   OR pt.name=N'CulturalInsightViews' OR rt.name=N'CulturalInsightViews'
ORDER BY ParentSchema,ParentTable,ForeignKeyName,fkc.constraint_column_id;

-- PK/unique/index cần cho mapping giữ ID và quan hệ của schema cũ.
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,
       i.name AS IndexName,i.is_primary_key AS IsPrimaryKey,i.is_unique AS IsUnique,
       i.filter_definition AS FilterDefinition,ic.key_ordinal AS KeyPosition,
       c.name AS ColumnName,ic.is_included_column AS IsIncluded
FROM sys.tables t
JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id>0
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE t.name LIKE N'Drama%' OR t.name=N'CulturalInsightViews'
ORDER BY SchemaName,TableName,IndexName,KeyPosition,c.column_id;

-- Giữ các ràng buộc nghiệp vụ hiện có, không tắt/drop để ép dữ liệu vào model mới.
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName,t.name AS TableName,
       cc.name AS ConstraintName,cc.definition AS CheckDefinition,
       cc.is_disabled AS IsDisabled,cc.is_not_trusted AS IsNotTrusted
FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id
WHERE t.name LIKE N'Drama%' OR t.name=N'CulturalInsightViews'
ORDER BY SchemaName,TableName,ConstraintName;
