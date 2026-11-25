/*
  Chẩn đoán chỉ đọc. Chọn VietVan_New trong SSMS và chạy toàn bộ file.
  Cần quyền xem metadata đầy đủ và đọc bảng history; số hàng từ sys.partitions
  là thống kê, không thay thế kiểm tra dữ liệu thực tế.
  Không thay đổi database, không xóa bảng hoặc lịch sử migration.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'VietVan_New'
    THROW 51000, 'Select VietVan_New before running diagnostics.', 1;

SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       ORIGINAL_LOGIN() AS OriginalLogin, SUSER_SNAME() AS CurrentLogin,
       USER_NAME() AS DatabaseUser, SCHEMA_NAME() AS DefaultSchema,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS CanViewDatabaseDefinition,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') AS HasDatabaseControl;

-- Guard migration loại trừ duy nhất bảng history bootstrap trong schema dbo.
SELECT s.name AS SchemaName, t.name AS TableName, t.is_ms_shipped AS IsSystemObject,
       t.temporal_type_desc AS TemporalType,
       COALESCE(SUM(CASE WHEN p.index_id IN (0,1) THEN CONVERT(bigint,p.rows) ELSE 0 END),0) AS ApproximateRows,
       CASE WHEN s.name=N'dbo' AND t.name=N'__EFMigrationsHistory_Bootstrap'
            THEN N'Excluded by baseline guard' ELSE N'Blocks baseline guard' END AS GuardEffect
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
LEFT JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
GROUP BY s.name,t.name,t.is_ms_shipped,t.temporal_type_desc
ORDER BY CASE WHEN s.name=N'dbo' AND t.name=N'__EFMigrationsHistory_Bootstrap' THEN 1 ELSE 0 END,
         s.name,t.name;

-- Hiện cả các object khác; chúng không nhất thiết là bảng ứng dụng.
SELECT s.name AS SchemaName, o.name AS ObjectName, o.type_desc AS ObjectType
FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
WHERE o.is_ms_shipped=0 AND o.type IN ('V','P','FN','IF','TF','SN','SO')
ORDER BY s.name,o.name;

-- So sánh cột, kiểu dữ liệu và nullability của các bảng thấy được.
SELECT s.name AS SchemaName, t.name AS TableName, c.column_id AS ColumnOrder,
       c.name AS ColumnName, ty.name AS SqlType, c.max_length AS MaxLengthBytes,
       c.precision AS NumericPrecision, c.scale AS NumericScale,
       c.is_nullable AS IsNullable, c.is_identity AS IsIdentity
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
ORDER BY s.name,t.name,c.column_id;

SELECT s.name AS SchemaName, t.name AS TableName, i.name AS IndexName,
       i.is_primary_key AS IsPrimaryKey, i.is_unique AS IsUnique,
       i.filter_definition AS FilterDefinition, ic.key_ordinal AS KeyPosition,
       c.name AS ColumnName
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id > 0
JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
ORDER BY s.name,t.name,i.name,ic.key_ordinal,ic.index_column_id;

SELECT fk.name AS ForeignKeyName,
       SCHEMA_NAME(pt.schema_id) AS ParentSchema, pt.name AS ParentTable, pc.name AS ParentColumn,
       SCHEMA_NAME(rt.schema_id) AS ReferencedSchema, rt.name AS ReferencedTable, rc.name AS ReferencedColumn,
       fk.delete_referential_action_desc AS DeleteAction, fk.is_disabled AS IsDisabled,
       fk.is_not_trusted AS IsNotTrusted
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables pt ON pt.object_id=fkc.parent_object_id
JOIN sys.columns pc ON pc.object_id=pt.object_id AND pc.column_id=fkc.parent_column_id
JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id
JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fkc.referenced_column_id
ORDER BY ParentSchema,ParentTable,ForeignKeyName,fkc.constraint_column_id;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       c.name AS ColumnName, dc.name AS ConstraintName, dc.definition AS DefaultDefinition
FROM sys.default_constraints dc
JOIN sys.tables t ON t.object_id=dc.parent_object_id
JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=dc.parent_column_id
ORDER BY SchemaName,TableName,ColumnName;

SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       cc.name AS ConstraintName, cc.definition AS CheckDefinition,
       cc.is_disabled AS IsDisabled, cc.is_not_trusted AS IsNotTrusted
FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id
ORDER BY SchemaName,TableName,ConstraintName;

-- Dynamic SQL tránh lỗi compile khi bảng history chưa tồn tại.
IF OBJECT_ID(N'dbo.__EFMigrationsHistory_Bootstrap',N'U') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT N''Bootstrap'' AS HistoryType, MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory_Bootstrap ORDER BY MigrationId;';
ELSE
    SELECT N'dbo.__EFMigrationsHistory_Bootstrap not found or not visible' AS HistoryStatus;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT N''API'' AS HistoryType, MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;';
ELSE
    SELECT N'dbo.__EFMigrationsHistory not found or not visible' AS HistoryStatus;

-- History ở schema khác cũng chặn baseline; không tự suy đoán đây là bảng tạm.
SELECT s.name AS SchemaName,t.name AS HistoryTable
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.name IN (N'__EFMigrationsHistory',N'__EFMigrationsHistory_Bootstrap')
ORDER BY s.name,t.name;
