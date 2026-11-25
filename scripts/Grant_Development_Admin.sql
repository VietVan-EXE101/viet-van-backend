-- Chỉ chạy thủ công trên VietVan_New sau khi xác nhận đúng chủ tài khoản.
-- Không đổi mật khẩu, trạng thái khóa, email hoặc quyền của tài khoản khác.
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New before running this script.', 1;
    BEGIN TRANSACTION;
    IF (SELECT COUNT(*) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
        WHERE Username = N'admin' AND Email = N'admin@gmail.com') <> 1
        THROW 51001, 'Expected exactly one matching username and email; no changes made.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'admin' AND Email = N'admin@gmail.com'
               AND (Role IS NULL OR Role COLLATE Latin1_General_100_BIN2 NOT IN (N'Customer', N'Admin')))
        THROW 51002, 'Unexpected current role; inspect the account before changing it.', 1;
    UPDATE dbo.Users SET Role = N'Admin'
    WHERE Username = N'admin' AND Email = N'admin@gmail.com' AND Role COLLATE Latin1_General_100_BIN2 = N'Customer';
    SELECT u.Id, u.Username, u.Email, u.Role, s.EmailVerified, s.IsLocked
    FROM dbo.Users u LEFT JOIN dbo.UserAuthStates s ON s.UserId = u.Id
    WHERE u.Username = N'admin' AND u.Email = N'admin@gmail.com';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
