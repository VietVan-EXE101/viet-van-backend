USE [VietVanDb];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Run manually to provision this account. This is not an application startup seed.
-- Existing account IDs, subscriptions and other users are preserved.
BEGIN TRY
    BEGIN TRANSACTION;

    IF (SELECT COUNT(*) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
        WHERE LOWER(Email) = N'admin@gmail.com') > 1
        THROW 50001, 'Multiple matching emails found; no account was changed.', 1;

    UPDATE dbo.Users
    SET PasswordHash = N'AQAAAAIAAYagAAAAEH07S7I+c33lEMskNJ2zl1MI+AVckau5yktj6JxHljI1E9dOkzB4Hs2KWEfTKccrCw==', Role = N'Admin'
    WHERE LOWER(Email) = N'admin@gmail.com';

    IF @@ROWCOUNT = 0
    BEGIN
        INSERT INTO dbo.Users (Username, Email, PasswordHash, Role, CreatedAt)
        VALUES (N'admin@gmail.com', N'admin@gmail.com', N'AQAAAAIAAYagAAAAEH07S7I+c33lEMskNJ2zl1MI+AVckau5yktj6JxHljI1E9dOkzB4Hs2KWEfTKccrCw==', N'Admin', SYSUTCDATETIME());
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT Id, Username, Email, Role
FROM dbo.Users
WHERE LOWER(Email) = N'admin@gmail.com';