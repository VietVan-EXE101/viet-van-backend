/*
    Chạy một lần sau VietVanDb.sql trong SQL Server Management Studio.
    Không xoá hay thay đổi Users, LiteraryWorks, hoặc dữ liệu cũ.
    Kịch lưu riêng background/audio/video/nhân vật/đồ vật + hotspot tương tác.
*/
USE VietVanDb;
GO
IF OBJECT_ID(N'dbo.DramaProductions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DramaProductions (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        LiteraryWorkId INT NULL,
        Title NVARCHAR(250) NOT NULL,
        Description NVARCHAR(3000) NULL,
        IsPublished BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NULL,
        CONSTRAINT FK_DramaProductions_LiteraryWorks FOREIGN KEY(LiteraryWorkId)
            REFERENCES dbo.LiteraryWorks(Id) ON DELETE NO ACTION
    );
END;
GO
IF OBJECT_ID(N'dbo.DramaAssets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DramaAssets (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DramaProductionId INT NOT NULL,
        Kind NVARCHAR(30) NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        OriginalFileName NVARCHAR(260) NOT NULL,
        RelativePath NVARCHAR(600) NOT NULL,
        ContentType NVARCHAR(100) NOT NULL,
        SizeBytes BIGINT NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_DramaAssets_Dramas FOREIGN KEY(DramaProductionId)
            REFERENCES dbo.DramaProductions(Id) ON DELETE CASCADE,
        CONSTRAINT CK_DramaAssets_Kind CHECK (Kind IN (N'Background',N'Audio',N'Character',N'Object',N'Video'))
    );
    CREATE INDEX IX_DramaAssets_DramaProductionId_Kind ON dbo.DramaAssets(DramaProductionId,Kind);
END;
GO
IF OBJECT_ID(N'dbo.DramaScenes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DramaScenes (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DramaProductionId INT NOT NULL,
        SceneKey NVARCHAR(80) NOT NULL,
        Title NVARCHAR(250) NOT NULL,
        SortOrder INT NOT NULL,
        StartMs INT NOT NULL,
        EndMs INT NOT NULL,
        AudioOffsetMs INT NOT NULL DEFAULT 0,
        BackgroundAssetId INT NULL,
        AudioAssetId INT NULL,
        VideoAssetId INT NULL,
        CONSTRAINT FK_DramaScenes_Dramas FOREIGN KEY(DramaProductionId)
            REFERENCES dbo.DramaProductions(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DramaScenes_Background FOREIGN KEY(BackgroundAssetId)
            REFERENCES dbo.DramaAssets(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_DramaScenes_Audio FOREIGN KEY(AudioAssetId)
            REFERENCES dbo.DramaAssets(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_DramaScenes_Video FOREIGN KEY(VideoAssetId)
            REFERENCES dbo.DramaAssets(Id) ON DELETE NO ACTION,
        CONSTRAINT CK_DramaScenes_Time CHECK (StartMs >= 0 AND EndMs > StartMs AND AudioOffsetMs >= 0)
    );
    CREATE UNIQUE INDEX IX_DramaScenes_DramaProductionId_SceneKey
        ON dbo.DramaScenes(DramaProductionId, SceneKey);
    CREATE INDEX IX_DramaScenes_DramaProductionId_SortOrder
        ON dbo.DramaScenes(DramaProductionId, SortOrder);
END;
GO
IF OBJECT_ID(N'dbo.DramaHotspots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DramaHotspots (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        DramaSceneId INT NOT NULL,
        VisualAssetId INT NULL,
        Kind NVARCHAR(30) NOT NULL,
        NameVi NVARCHAR(200) NOT NULL,
        NameEn NVARCHAR(200) NULL,
        MeaningVi NVARCHAR(MAX) NOT NULL,
        MeaningEn NVARCHAR(MAX) NULL,
        X FLOAT NOT NULL,
        Y FLOAT NOT NULL,
        Width FLOAT NOT NULL,
        Height FLOAT NOT NULL,
        StartMs INT NULL,
        EndMs INT NULL,
        ZIndex INT NOT NULL DEFAULT 0,
        IsEnabled BIT NOT NULL DEFAULT 1,
        ActionKey NVARCHAR(120) NULL,
        TargetSegmentIdsJson NVARCHAR(MAX) NOT NULL DEFAULT N'[]',
        CONSTRAINT FK_DramaHotspots_Scenes FOREIGN KEY(DramaSceneId)
            REFERENCES dbo.DramaScenes(Id) ON DELETE CASCADE,
        CONSTRAINT FK_DramaHotspots_VisualAsset FOREIGN KEY(VisualAssetId)
            REFERENCES dbo.DramaAssets(Id) ON DELETE NO ACTION,
        CONSTRAINT CK_DramaHotspots_Kind CHECK(Kind IN(N'Character',N'Object')),
        CONSTRAINT CK_DramaHotspots_Position CHECK(X >= 0 AND Y >= 0 AND Width > 0 AND Height > 0 AND X + Width <= 100 AND Y + Height <= 100),
        CONSTRAINT CK_DramaHotspots_TargetsJson CHECK(ISJSON(TargetSegmentIdsJson) = 1)
    );
    CREATE INDEX IX_DramaHotspots_DramaSceneId_ZIndex
        ON dbo.DramaHotspots(DramaSceneId,ZIndex);
END;
GO
PRINT N'Đã kiểm tra/tạo các bảng Drama (không ảnh hưởng dữ liệu hiện có).';
