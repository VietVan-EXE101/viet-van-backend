/* Chỉ đọc trên database Somee đã import. Không sửa schema, publish hoặc dữ liệu.
   Đổi @DramaId nếu cần. Không đọc nội dung manifest, thông tin đăng nhập hoặc secret.
   RelativePath là khóa file, KHÔNG chứng minh file đã được chuyển lên Render.
   Đối chiếu thêm --check-db-readonly để kiểm tra đủ cột theo model EF hiện tại. */
SET NOCOUNT ON;
DECLARE @DramaId int = 22;
IF DB_NAME() <> N'VietVan_New'
    THROW 51000, 'Select the imported VietVan_New database on Somee first.', 1;

SELECT DB_NAME() AS DatabaseName, @DramaId AS DramaId;

-- Tổng số hàng xấp xỉ từ metadata, kể cả các bảng chưa có đủ cột.
SELECT t.name AS TableName,
       SUM(CONVERT(bigint, p.rows)) AS ApproximateRows
FROM sys.tables t
LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name IN
    (N'DramaProductions', N'DramaAssets', N'DramaScenes', N'DramaHotspots',
     N'DramaManifests', N'DramaViewings', N'CulturalInsightViews', N'Reviews')
GROUP BY t.name ORDER BY t.name;

-- Dynamic batches: thiếu bảng/cột thì không làm các mục khác lỗi lúc compile.
IF COL_LENGTH(N'dbo.DramaProductions', N'Id') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaProductions', N'IsPublished') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaProductions', N'Status') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaProductions', N'FreeVideoAssetId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT Id, IsPublished, Status, FreeVideoAssetId,
          CASE WHEN IsPublished = 1 AND Status = N''PUBLISHED'' THEN 1 ELSE 0 END AS VisibleInPublicList
        FROM dbo.DramaProductions WHERE Id = @Id;', N'@Id int', @Id = @DramaId;
ELSE PRINT 'SKIP drama status: required table/columns missing or not visible.';

IF COL_LENGTH(N'dbo.DramaAssets', N'Id') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaAssets', N'DramaProductionId') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaAssets', N'RelativePath') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaAssets', N'ContentType') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaAssets', N'SizeBytes') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT Id, DramaProductionId, RelativePath, ContentType, SizeBytes
        FROM dbo.DramaAssets WHERE DramaProductionId = @Id ORDER BY Id;', N'@Id int', @Id = @DramaId;
ELSE PRINT 'SKIP asset inventory: required table/columns missing or not visible.';

IF COL_LENGTH(N'dbo.DramaManifests', N'DramaProductionId') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaManifests', N'DraftJson') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaManifests', N'PublishedJson') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaManifests', N'PublishedLegacyJson') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT DramaProductionId,
          CASE WHEN DraftJson IS NOT NULL THEN 1 ELSE 0 END AS HasDraft,
          CASE WHEN PublishedJson IS NOT NULL THEN 1 ELSE 0 END AS HasPublished,
          CASE WHEN PublishedLegacyJson IS NOT NULL THEN 1 ELSE 0 END AS HasPublishedLegacy,
          ISJSON(DraftJson) AS DraftIsJson, ISJSON(PublishedJson) AS PublishedIsJson,
          ISJSON(PublishedLegacyJson) AS PublishedLegacyIsJson
        FROM dbo.DramaManifests WHERE DramaProductionId = @Id;', N'@Id int', @Id = @DramaId;
ELSE PRINT 'SKIP manifest flags: required table/columns missing or not visible.';

IF COL_LENGTH(N'dbo.DramaScenes', N'DramaProductionId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS ScenesForDrama FROM dbo.DramaScenes WHERE DramaProductionId = @Id;',
        N'@Id int', @Id = @DramaId;
ELSE PRINT 'SKIP scene count: required table/columns missing or not visible.';

IF COL_LENGTH(N'dbo.DramaScenes', N'Id') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaScenes', N'DramaProductionId') IS NOT NULL
   AND COL_LENGTH(N'dbo.DramaHotspots', N'DramaSceneId') IS NOT NULL
    EXEC sys.sp_executesql N'
        SELECT COUNT_BIG(*) AS HotspotsForDrama
        FROM dbo.DramaHotspots h JOIN dbo.DramaScenes s ON s.Id = h.DramaSceneId
        WHERE s.DramaProductionId = @Id;', N'@Id int', @Id = @DramaId;
ELSE PRINT 'SKIP hotspot count: required table/columns missing or not visible.';

PRINT 'SELECT only. Empty legacy scene/manifest data can be valid for video-only dramas; compare with the source data.';
