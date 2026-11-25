-- Chỉ đọc metadata. Chọn VietVan_New, chạy toàn bộ và gửi kết quả để đối chiếu.
-- Cần quyền VIEW DEFINITION trong database để tránh kết quả thiếu bảng/cột.
SET NOCOUNT ON;
IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New first.', 1;
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS CanViewDefinition,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') AS HasDatabaseControl;

WITH Expected(TableName,ColumnName) AS (
 SELECT * FROM (VALUES
 (N'Users',N'Id'),(N'Users',N'Username'),(N'Users',N'Email'),(N'Users',N'PasswordHash'),
 (N'Users',N'Role'),(N'Users',N'CreatedAt'),(N'Users',N'VipExpiresAt'),(N'Users',N'FullName'),
 (N'SubscriptionPackages',N'Id'),(N'SubscriptionPackages',N'Name'),(N'SubscriptionPackages',N'Price'),
 (N'SubscriptionPackages',N'DurationInDays'),(N'SubscriptionPackages',N'TierLevel'),
 (N'SubscriptionPackages',N'Code'),(N'SubscriptionPackages',N'BillingPeriod'),
 (N'SubscriptionPackages',N'Description'),(N'SubscriptionPackages',N'Benefits'),
 (N'SubscriptionPackages',N'IsActive'),(N'SubscriptionPackages',N'CreatedAt'),(N'SubscriptionPackages',N'UpdatedAt'),
 (N'UserSubscriptions',N'Id'),(N'UserSubscriptions',N'UserId'),(N'UserSubscriptions',N'PackageId'),
 (N'UserSubscriptions',N'StartDate'),(N'UserSubscriptions',N'EndDate'),(N'UserSubscriptions',N'IsActive'),
 (N'UserSubscriptions',N'GrantedTierLevel'),(N'UserSubscriptions',N'ActivationPaymentOrderId'),
 (N'UserAuthStates',N'UserId'),(N'UserAuthStates',N'EmailVerified'),(N'UserAuthStates',N'EmailVerifiedAt'),(N'UserAuthStates',N'IsLocked'),(N'UserAuthStates',N'TokenVersion'),
 (N'EmailVerificationTokens',N'Id'),(N'EmailVerificationTokens',N'UserId'),(N'EmailVerificationTokens',N'EmailSnapshot'),
 (N'EmailVerificationTokens',N'TokenHash'),(N'EmailVerificationTokens',N'CreatedAt'),
 (N'EmailVerificationTokens',N'ExpiresAt'),(N'EmailVerificationTokens',N'ConsumedAt')
 ) v(TableName,ColumnName)
)
SELECT e.TableName,e.ColumnName,
 CASE WHEN t.object_id IS NULL THEN N'TABLE NOT FOUND OR NOT VISIBLE'
      WHEN c.column_id IS NULL THEN N'COLUMN NOT FOUND OR NOT VISIBLE' ELSE N'PRESENT' END AS Status,
 ty.name AS SqlType,c.max_length AS MaxLengthBytes,c.is_nullable AS IsNullable
FROM Expected e
LEFT JOIN sys.schemas s ON s.name=N'dbo'
LEFT JOIN sys.tables t ON t.schema_id=s.schema_id AND t.name=e.TableName
LEFT JOIN sys.columns c ON c.object_id=t.object_id AND c.name=e.ColumnName
LEFT JOIN sys.types ty ON ty.user_type_id=c.user_type_id
ORDER BY e.TableName,e.ColumnName;

-- Xem cột thực tế, kể cả cột Benefit/Benefits, mà không in dữ liệu người dùng.
SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,ty.name AS SqlType,
       c.max_length AS MaxLengthBytes,c.is_nullable AS IsNullable,dc.definition AS DefaultDefinition
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
WHERE t.name IN (N'Users',N'SubscriptionPackages',N'UserSubscriptions',N'UserAuthStates',N'EmailVerificationTokens')
ORDER BY s.name,t.name,c.column_id;
