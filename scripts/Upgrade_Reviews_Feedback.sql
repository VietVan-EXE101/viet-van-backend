/* Back up VietVan_New first. In SSMS select VietVan_New, review this script,
   then execute it with a principal allowed to create tables and constraints.
   Existing Reviews/ReviewModerationLogs are never modified. If either already
   exists, inspect its schema before applying this upgrade. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New first.', 1;
IF OBJECT_ID(N'dbo.Users',N'U') IS NULL OR OBJECT_ID(N'dbo.LiteraryWorks',N'U') IS NULL OR OBJECT_ID(N'dbo.DramaProductions',N'U') IS NULL
    THROW 51001, 'Required target tables are missing or not visible.', 1;
IF OBJECT_ID(N'dbo.Reviews',N'U') IS NOT NULL OR OBJECT_ID(N'dbo.ReviewModerationLogs',N'U') IS NOT NULL
    THROW 51002, 'A review table already exists. Inspect compatibility before proceeding.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
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
    CREATE TABLE dbo.ReviewModerationLogs (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReviewModerationLogs PRIMARY KEY,
        ReviewId bigint NOT NULL, AdminUserId int NOT NULL, OldStatus nvarchar(16) NOT NULL,
        NewStatus nvarchar(16) NOT NULL, CreatedAt datetime2 NOT NULL,
        CONSTRAINT FK_ReviewModerationLogs_Users FOREIGN KEY(AdminUserId) REFERENCES dbo.Users(Id)
    );
    CREATE INDEX IX_ReviewModerationLogs_ReviewId_CreatedAt ON dbo.ReviewModerationLogs(ReviewId,CreatedAt);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT t.name AS TableName, i.name AS IndexName, i.is_unique, i.filter_definition
FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo') AND t.name IN (N'Reviews',N'ReviewModerationLogs') AND i.index_id>0;
