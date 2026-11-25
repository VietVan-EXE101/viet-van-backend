/* Additive upgrade, 25/11/2026. Review schema + backup first. No package/price/user/history deletion.
   Requires the existing Payment, Auth and Drama upgrades. Creates missing review tables.
   Do NOT run historical
   API migrations on the separately bootstrapped database. This script has its own marker. */
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @MissingTables nvarchar(1800);
 SELECT @MissingTables=STRING_AGG(CONVERT(nvarchar(max), Required.TableName), N', ')
 FROM (VALUES (N'dbo.Users'), (N'dbo.UserAuthStates'), (N'dbo.LiteraryWorks'),
              (N'dbo.DramaProductions'), (N'dbo.DramaAssets'), (N'dbo.DramaScenes'),
              (N'dbo.DramaHotspots'), (N'dbo.PaymentOrders'), (N'dbo.UserSubscriptions')) Required(TableName)
 WHERE OBJECT_ID(Required.TableName, N'U') IS NULL;
 IF @MissingTables IS NOT NULL
 BEGIN
   DECLARE @MissingMessage nvarchar(2048)=N'Missing prerequisite schema: '+@MissingTables
     +N'. Inspect the database; do not run a fresh baseline over existing data.';
   THROW 51001, @MissingMessage, 1;
 END;

 -- Same schema as Upgrade_Reviews_Feedback.sql and the current EF model.
 -- These tables are absent in some older bootstrap databases. Existing tables/data stay intact.
 IF OBJECT_ID(N'dbo.Reviews', N'U') IS NULL
 BEGIN
   CREATE TABLE dbo.Reviews (
     Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reviews PRIMARY KEY,
     UserId int NOT NULL, LiteraryWorkId int NULL, DramaProductionId int NULL,
     Rating int NOT NULL, Comment nvarchar(2000) NOT NULL, Status nvarchar(16) NOT NULL,
     CreatedAt datetime2 NOT NULL, UpdatedAt datetime2 NULL,
     CONSTRAINT FK_Reviews_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
     CONSTRAINT FK_Reviews_LiteraryWorks FOREIGN KEY(LiteraryWorkId) REFERENCES dbo.LiteraryWorks(Id),
     CONSTRAINT FK_Reviews_DramaProductions FOREIGN KEY(DramaProductionId) REFERENCES dbo.DramaProductions(Id),
     CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
     CONSTRAINT CK_Reviews_Status CHECK (Status IN (N'Published',N'Hidden')),
     CONSTRAINT CK_Reviews_Target CHECK ((LiteraryWorkId IS NOT NULL AND DramaProductionId IS NULL) OR (LiteraryWorkId IS NULL AND DramaProductionId IS NOT NULL))
   );
   CREATE UNIQUE INDEX IX_Reviews_UserId_LiteraryWorkId ON dbo.Reviews(UserId,LiteraryWorkId) WHERE LiteraryWorkId IS NOT NULL;
   CREATE UNIQUE INDEX IX_Reviews_UserId_DramaProductionId ON dbo.Reviews(UserId,DramaProductionId) WHERE DramaProductionId IS NOT NULL;
   CREATE INDEX IX_Reviews_LiteraryWorkId_Status_CreatedAt ON dbo.Reviews(LiteraryWorkId,Status,CreatedAt);
   CREATE INDEX IX_Reviews_DramaProductionId_Status_CreatedAt ON dbo.Reviews(DramaProductionId,Status,CreatedAt);
 END;
 IF OBJECT_ID(N'dbo.ReviewModerationLogs', N'U') IS NULL
 BEGIN
   CREATE TABLE dbo.ReviewModerationLogs (
     Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReviewModerationLogs PRIMARY KEY,
     ReviewId bigint NOT NULL, AdminUserId int NOT NULL, OldStatus nvarchar(16) NOT NULL,
     NewStatus nvarchar(16) NOT NULL, CreatedAt datetime2 NOT NULL,
     CONSTRAINT FK_ReviewModerationLogs_Users FOREIGN KEY(AdminUserId) REFERENCES dbo.Users(Id)
   );
   CREATE INDEX IX_ReviewModerationLogs_ReviewId_CreatedAt ON dbo.ReviewModerationLogs(ReviewId,CreatedAt);
 END;

 IF COL_LENGTH('dbo.UserAuthStates','TokenVersion') IS NULL
   ALTER TABLE dbo.UserAuthStates ADD TokenVersion int NOT NULL CONSTRAINT DF_UAS_TokenVersion DEFAULT 0;
 IF COL_LENGTH('dbo.Users','FullName') IS NULL ALTER TABLE dbo.Users ADD FullName nvarchar(150) NULL;
 -- Tài khoản cũ chưa có bằng chứng xác minh phải xác minh email; giữ nguyên mọi tài khoản.
 INSERT dbo.UserAuthStates(UserId,EmailVerified,EmailVerifiedAt,IsLocked)
 SELECT u.Id,CASE WHEN u.Role=N'Admin' THEN 1 ELSE 0 END,NULL,0
 FROM dbo.Users u WHERE NOT EXISTS(SELECT 1 FROM dbo.UserAuthStates s WHERE s.UserId=u.Id);

 -- Older DramaUpgrade databases lack these columns; EF still maps them even though
 -- preview playback is retired. Preserve existing values, add defaults only when absent.
 IF COL_LENGTH(N'dbo.DramaProductions',N'AccessLevel') IS NULL
   ALTER TABLE dbo.DramaProductions ADD AccessLevel nvarchar(30) NOT NULL CONSTRAINT DF_DP_AccessLevel DEFAULT N'Public' WITH VALUES;
 IF COL_LENGTH(N'dbo.DramaProductions',N'PreviewDurationMs') IS NULL
   ALTER TABLE dbo.DramaProductions ADD PreviewDurationMs int NOT NULL CONSTRAINT DF_DramaProductions_PreviewDurationMs DEFAULT (120000) WITH VALUES;
 IF COL_LENGTH(N'dbo.DramaAssets',N'IsPreview') IS NULL
   ALTER TABLE dbo.DramaAssets ADD IsPreview bit NOT NULL CONSTRAINT DF_DramaAssets_IsPreview DEFAULT (0) WITH VALUES;
 IF COL_LENGTH(N'dbo.DramaScenes',N'PreviewAudioAssetId') IS NULL
   ALTER TABLE dbo.DramaScenes ADD PreviewAudioAssetId int NULL;
 IF COL_LENGTH(N'dbo.DramaScenes',N'PreviewVideoAssetId') IS NULL
   ALTER TABLE dbo.DramaScenes ADD PreviewVideoAssetId int NULL;

 IF EXISTS (
   SELECT 1 FROM (VALUES (N'DramaProductions',N'AccessLevel',N'nvarchar',0,60),
                        (N'DramaProductions',N'PreviewDurationMs',N'int',0,4),
                        (N'DramaAssets',N'IsPreview',N'bit',0,1),
                        (N'DramaScenes',N'PreviewAudioAssetId',N'int',1,4),
                        (N'DramaScenes',N'PreviewVideoAssetId',N'int',1,4)) v(TableName,ColumnName,TypeName,Nullable,MinBytes)
   LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+v.TableName) AND c.name=v.ColumnName
   WHERE c.column_id IS NULL OR TYPE_NAME(c.user_type_id)<>v.TypeName OR c.is_nullable<>v.Nullable
     OR (c.max_length<>-1 AND c.max_length<v.MinBytes)
 ) THROW 51002, 'An existing Drama compatibility column has an incompatible type, size or nullability. Run Diagnose_Drama_Schema.sql.', 1;

 -- Compile references only after missing columns have been added. WITH CHECK validates
 -- existing references and rolls back the upgrade if they contain orphan asset IDs.
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns f JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id
                WHERE f.parent_object_id=OBJECT_ID(N'dbo.DramaScenes') AND c.name=N'PreviewAudioAssetId' AND f.referenced_object_id=OBJECT_ID(N'dbo.DramaAssets'))
   EXEC(N'ALTER TABLE dbo.DramaScenes WITH CHECK ADD CONSTRAINT FK_DramaScenes_PreviewAudioAsset FOREIGN KEY (PreviewAudioAssetId) REFERENCES dbo.DramaAssets(Id);');
 IF NOT EXISTS (SELECT 1 FROM sys.foreign_key_columns f JOIN sys.columns c ON c.object_id=f.parent_object_id AND c.column_id=f.parent_column_id
                WHERE f.parent_object_id=OBJECT_ID(N'dbo.DramaScenes') AND c.name=N'PreviewVideoAssetId' AND f.referenced_object_id=OBJECT_ID(N'dbo.DramaAssets'))
   EXEC(N'ALTER TABLE dbo.DramaScenes WITH CHECK ADD CONSTRAINT FK_DramaScenes_PreviewVideoAsset FOREIGN KEY (PreviewVideoAssetId) REFERENCES dbo.DramaAssets(Id);');

 IF COL_LENGTH('dbo.DramaProductions','Status') IS NULL
 BEGIN
   ALTER TABLE dbo.DramaProductions ADD Status nvarchar(16) NOT NULL CONSTRAINT DF_DP_Status DEFAULT N'DRAFT';
   EXEC(N'UPDATE dbo.DramaProductions SET Status = CASE WHEN IsPublished=1 THEN N''PUBLISHED'' ELSE N''DRAFT'' END;');
 END;
 IF COL_LENGTH('dbo.DramaProductions','PublishedAt') IS NULL
 BEGIN
   ALTER TABLE dbo.DramaProductions ADD PublishedAt datetime2 NULL;
   -- Timestamp chuyển đổi, không giả định biết ngày xuất bản lịch sử.
   EXEC(N'UPDATE dbo.DramaProductions SET PublishedAt=SYSUTCDATETIME() WHERE IsPublished=1;');
 END;
 IF COL_LENGTH('dbo.DramaProductions','FreeVideoAssetId') IS NULL
   ALTER TABLE dbo.DramaProductions ADD FreeVideoAssetId int NULL;
 -- Không suy diễn video dùng trong cảnh PRO là video FREE; Admin phải chọn/upload rõ ràng.
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_DP_FreeVideo')
   EXEC(N'ALTER TABLE dbo.DramaProductions ADD CONSTRAINT FK_DP_FreeVideo FOREIGN KEY(FreeVideoAssetId) REFERENCES dbo.DramaAssets(Id);');
 IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_DP_Status')
   EXEC(N'ALTER TABLE dbo.DramaProductions ADD CONSTRAINT CK_DP_Status CHECK(Status IN (N''DRAFT'',N''PUBLISHED'',N''DISABLED'') AND ((Status=N''PUBLISHED'' AND IsPublished=1) OR (Status<>N''PUBLISHED'' AND IsPublished=0)));');
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.DramaProductions') AND name='IX_DP_Status')
   EXEC(N'CREATE INDEX IX_DP_Status ON dbo.DramaProductions(Status, IsPublished);');

 IF COL_LENGTH('dbo.DramaAssets','NameVi') IS NULL ALTER TABLE dbo.DramaAssets ADD NameVi nvarchar(200) NULL;
 IF COL_LENGTH('dbo.DramaAssets','NameEn') IS NULL ALTER TABLE dbo.DramaAssets ADD NameEn nvarchar(200) NULL;
 IF COL_LENGTH('dbo.DramaAssets','MeaningVi') IS NULL ALTER TABLE dbo.DramaAssets ADD MeaningVi nvarchar(max) NULL;
 IF COL_LENGTH('dbo.DramaAssets','MeaningEn') IS NULL ALTER TABLE dbo.DramaAssets ADD MeaningEn nvarchar(max) NULL;
 IF COL_LENGTH('dbo.DramaAssets','DurationSeconds') IS NULL ALTER TABLE dbo.DramaAssets ADD DurationSeconds float NULL;
 -- Mở rộng allowlist nếu schema cũ dùng check constraint. Chỉ thay constraint Kind đã biết;
 -- giữ nguyên mọi dữ liệu và không tự thay constraint không xác định.
 IF OBJECT_ID('dbo.CK_DramaAssets_Kind','C') IS NOT NULL
 BEGIN
   ALTER TABLE dbo.DramaAssets DROP CONSTRAINT CK_DramaAssets_Kind;
   ALTER TABLE dbo.DramaAssets ADD CONSTRAINT CK_DramaAssets_Kind CHECK(Kind IN('Background','Character','Object','Audio','Video','Music','Effect'));
 END;

 IF OBJECT_ID('dbo.DramaManifests','U') IS NULL
 CREATE TABLE dbo.DramaManifests(
   DramaProductionId int NOT NULL CONSTRAINT PK_DramaManifests PRIMARY KEY,
   DraftJson nvarchar(max) NULL, PublishedJson nvarchar(max) NULL, PublishedLegacyJson nvarchar(max) NULL,
   UpdatedAt datetime2 NOT NULL, PublishedAt datetime2 NULL, Revision int NOT NULL,
   CONSTRAINT FK_DM_Drama FOREIGN KEY(DramaProductionId) REFERENCES dbo.DramaProductions(Id),
   CONSTRAINT CK_DM_Json CHECK((DraftJson IS NULL OR ISJSON(DraftJson)=1) AND (PublishedJson IS NULL OR ISJSON(PublishedJson)=1) AND (PublishedLegacyJson IS NULL OR ISJSON(PublishedLegacyJson)=1))
 );
 IF OBJECT_ID('dbo.DramaViewings','U') IS NULL
 CREATE TABLE dbo.DramaViewings(
   UserId int NOT NULL, DramaProductionId int NOT NULL, AccessedAt datetime2 NOT NULL, VersionType nvarchar(20) NOT NULL,
   CONSTRAINT PK_DramaViewings PRIMARY KEY(UserId,DramaProductionId),
   CONSTRAINT FK_DV_User FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
   CONSTRAINT FK_DV_Drama FOREIGN KEY(DramaProductionId) REFERENCES dbo.DramaProductions(Id),
   CONSTRAINT CK_DV_Version CHECK(VersionType IN('FREE_VIDEO','PRO_INTERACTIVE'))
 );
 IF OBJECT_ID('dbo.PasswordResetTokens','U') IS NULL
 BEGIN
   CREATE TABLE dbo.PasswordResetTokens(
     Id bigint IDENTITY NOT NULL CONSTRAINT PK_PasswordResetTokens PRIMARY KEY,
     UserId int NOT NULL, TokenHash binary(32) NOT NULL, EmailSnapshot nvarchar(150) NOT NULL,
     CreatedAt datetime2 NOT NULL, ExpiresAt datetime2 NOT NULL, ConsumedAt datetime2 NULL,
     CONSTRAINT FK_PRT_User FOREIGN KEY(UserId) REFERENCES dbo.Users(Id)
   );
   CREATE UNIQUE INDEX UX_PRT_Hash ON dbo.PasswordResetTokens(TokenHash);
   CREATE INDEX IX_PRT_UserDate ON dbo.PasswordResetTokens(UserId,CreatedAt);
 END;
 IF OBJECT_ID('dbo.AdminAuditLogs','U') IS NULL
 BEGIN
   CREATE TABLE dbo.AdminAuditLogs(
     Id bigint IDENTITY NOT NULL CONSTRAINT PK_AdminAuditLogs PRIMARY KEY,
     AdminUserId int NOT NULL, TargetUserId int NOT NULL, Action nvarchar(80) NOT NULL, CreatedAt datetime2 NOT NULL,
     CONSTRAINT FK_AAL_Admin FOREIGN KEY(AdminUserId) REFERENCES dbo.Users(Id),
     CONSTRAINT FK_AAL_User FOREIGN KEY(TargetUserId) REFERENCES dbo.Users(Id)
   );
   CREATE INDEX IX_AAL_TargetDate ON dbo.AdminAuditLogs(TargetUserId,CreatedAt);
 END;
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.UserSubscriptions') AND name='IX_US_Expiry')
   CREATE INDEX IX_US_Expiry ON dbo.UserSubscriptions(UserId,IsActive,StartDate,EndDate);
 IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.PaymentOrders') AND name='IX_PO_PaidAt')
   CREATE INDEX IX_PO_PaidAt ON dbo.PaymentOrders(Status,PaidAt);
 IF OBJECT_ID('dbo.__VietVanUpgrades','U') IS NULL
   CREATE TABLE dbo.__VietVanUpgrades(Name nvarchar(150) NOT NULL PRIMARY KEY, AppliedAt datetime2 NOT NULL);
 IF NOT EXISTS(SELECT 1 FROM dbo.__VietVanUpgrades WHERE Name=N'20261125_DramaPlatformRefactor')
   INSERT dbo.__VietVanUpgrades(Name,AppliedAt) VALUES(N'20261125_DramaPlatformRefactor',SYSUTCDATETIME());
 COMMIT;
 PRINT N'Drama platform upgrade completed. Retry login and POST /api/admin/dramas.';
END TRY
BEGIN CATCH
 IF @@TRANCOUNT > 0 ROLLBACK;
 THROW;
END CATCH;
