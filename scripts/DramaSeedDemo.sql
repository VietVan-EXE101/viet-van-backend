/*
   Chạy SAU DramaUpgrade.sql.
   Demo lấy 3 khung hình + âm thanh từ video kịch.mp4 do người dùng cung cấp.
   Đây là 3 cảnh mẫu minh họa, chưa phải kịch bản phân cảnh chính xác.
   Phải giữ nguyên các file wwwroot/drama-assets/demo/*.jpg, *.mp3.
*/
USE VietVanDb;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @d INT, @bg1 INT, @bg2 INT, @bg3 INT, @audio INT;
DECLARE @s1 INT, @s2 INT, @s3 INT;
SELECT @d = Id FROM dbo.DramaProductions WHERE Title = N'Câu chuyện bốn mùa (demo tương tác)';
IF @d IS NULL
BEGIN
    INSERT INTO dbo.DramaProductions(Title,Description,IsPublished)
        VALUES(N'Câu chuyện bốn mùa (demo tương tác)',
               N'Bản demo 3 cảnh, từ ảnh chụp và âm thanh video; có hotspot nhân vật và đồ vật.',1);
    SET @d = SCOPE_IDENTITY();
END;
IF NOT EXISTS(SELECT 1 FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-1.jpg')
    INSERT INTO dbo.DramaAssets(DramaProductionId,Kind,Title,OriginalFileName,RelativePath,ContentType,SizeBytes)
    VALUES(@d,'Background',N'Cảnh sân khấu 1','canh-1.jpg','drama-assets/demo/canh-1.jpg','image/jpeg',167286);
SELECT @bg1 = Id FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-1.jpg';
IF NOT EXISTS(SELECT 1 FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-2.jpg')
    INSERT INTO dbo.DramaAssets(DramaProductionId,Kind,Title,OriginalFileName,RelativePath,ContentType,SizeBytes)
    VALUES(@d,'Background',N'Cảnh sân khấu 2','canh-2.jpg','drama-assets/demo/canh-2.jpg','image/jpeg',195127);
SELECT @bg2 = Id FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-2.jpg';
IF NOT EXISTS(SELECT 1 FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-3.jpg')
    INSERT INTO dbo.DramaAssets(DramaProductionId,Kind,Title,OriginalFileName,RelativePath,ContentType,SizeBytes)
    VALUES(@d,'Background',N'Cảnh sân khấu 3','canh-3.jpg','drama-assets/demo/canh-3.jpg','image/jpeg',201649);
SELECT @bg3 = Id FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/canh-3.jpg';
IF NOT EXISTS(SELECT 1 FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/audio-bon-mua.mp3')
    INSERT INTO dbo.DramaAssets(DramaProductionId,Kind,Title,OriginalFileName,RelativePath,ContentType,SizeBytes)
    VALUES(@d,'Audio',N'Audio gốc từ video','audio-bon-mua.mp3','drama-assets/demo/audio-bon-mua.mp3','audio/mpeg',2421791);
SELECT @audio = Id FROM dbo.DramaAssets WHERE DramaProductionId=@d AND RelativePath='drama-assets/demo/audio-bon-mua.mp3';
IF NOT EXISTS(SELECT 1 FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-1')
    INSERT INTO dbo.DramaScenes(DramaProductionId,SceneKey,Title,SortOrder,StartMs,EndMs,AudioOffsetMs,BackgroundAssetId,AudioAssetId)
    VALUES(@d,'demo-1',N'Mở đầu',1,0,50000,0,@bg1,@audio);
IF NOT EXISTS(SELECT 1 FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-2')
    INSERT INTO dbo.DramaScenes(DramaProductionId,SceneKey,Title,SortOrder,StartMs,EndMs,AudioOffsetMs,BackgroundAssetId,AudioAssetId)
    VALUES(@d,'demo-2',N'Các nhân vật xuất hiện',2,50000,100000,50000,@bg2,@audio);
IF NOT EXISTS(SELECT 1 FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-3')
    INSERT INTO dbo.DramaScenes(DramaProductionId,SceneKey,Title,SortOrder,StartMs,EndMs,AudioOffsetMs,BackgroundAssetId,AudioAssetId)
    VALUES(@d,'demo-3',N'Khung cảnh hội tụ',3,100000,151291,100000,@bg3,@audio);
SELECT @s1 = Id FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-1';
SELECT @s2 = Id FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-2';
SELECT @s3 = Id FROM dbo.DramaScenes WHERE DramaProductionId=@d AND SceneKey='demo-3';
-- Mọi tọa độ là phần trăm so với khung 16:9; tên nhân vật tạm thời để không suy đoán cốt truyện.
IF NOT EXISTS(SELECT 1 FROM dbo.DramaHotspots WHERE DramaSceneId=@s1 AND NameVi=N'Nhân vật áo nâu')
    INSERT INTO dbo.DramaHotspots(DramaSceneId,Kind,NameVi,NameEn,MeaningVi,MeaningEn,X,Y,Width,Height,IsEnabled,TargetSegmentIdsJson)
    VALUES(@s1,'Character',N'Nhân vật áo nâu',N'Brown-clothed character',N'Nhân vật xuất hiện bên trái sân khấu. Có thể bổ sung tên và vai trò chính thức từ kịch bản.',N'A character standing at the left side of the stage. Replace this text with the character’s confirmed name and role.',20,42,15,47,1,N'[]');
IF NOT EXISTS(SELECT 1 FROM dbo.DramaHotspots WHERE DramaSceneId=@s1 AND NameVi=N'Đèn hoa sen')
    INSERT INTO dbo.DramaHotspots(DramaSceneId,Kind,NameVi,NameEn,MeaningVi,MeaningEn,X,Y,Width,Height,IsEnabled,TargetSegmentIdsJson)
    VALUES(@s1,'Object',N'Đèn hoa sen',N'Lotus lantern',N'Vật trang trí hình hoa sen treo trên sân khấu.',N'A lotus-shaped hanging ornament in the stage setting.',45,10,12,28,1,N'[]');
IF NOT EXISTS(SELECT 1 FROM dbo.DramaHotspots WHERE DramaSceneId=@s2 AND NameVi=N'Nhóm nhân vật')
    INSERT INTO dbo.DramaHotspots(DramaSceneId,Kind,NameVi,NameEn,MeaningVi,MeaningEn,X,Y,Width,Height,IsEnabled,TargetSegmentIdsJson)
    VALUES(@s2,'Character',N'Nhóm nhân vật',N'Group of characters',N'Nhiều nhân vật cùng xuất hiện trong một khung cảnh. Hãy chia hotspot riêng cho từng người khi có asset gốc.',N'Multiple characters appear together. Separate the hotspots once individual character assets are available.',19,41,44,48,1,N'[]');
IF NOT EXISTS(SELECT 1 FROM dbo.DramaHotspots WHERE DramaSceneId=@s3 AND NameVi=N'Nhân vật đội nón')
    INSERT INTO dbo.DramaHotspots(DramaSceneId,Kind,NameVi,NameEn,MeaningVi,MeaningEn,X,Y,Width,Height,IsEnabled,TargetSegmentIdsJson)
    VALUES(@s3,'Character',N'Nhân vật đội nón',N'Character wearing a conical hat',N'Nhân vật xuất hiện ở phía phải của nhóm người.',N'The character on the right side of the group.',70,41,16,48,1,N'[]');
COMMIT TRANSACTION;
PRINT N'Đã tạo dữ liệu demo Câu chuyện bốn mùa (chạy lại không trùng).';
GO
