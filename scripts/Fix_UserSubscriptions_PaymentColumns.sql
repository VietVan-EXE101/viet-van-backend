/*
  Bổ sung hai cột subscription cần cho đăng ký và đối chiếu thanh toán.
  Chọn VietVan_New trong SSMS. Không xóa dữ liệu hoặc chỉnh migration history.
  Dừng và kiểm tra thủ công nếu cột, khóa hoặc dữ liệu hiện có không tương thích.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
    IF DB_NAME() <> N'VietVan_New' THROW 51000, 'Select VietVan_New first.', 1;
    IF OBJECT_ID(N'dbo.UserSubscriptions',N'U') IS NULL THROW 51001, 'dbo.UserSubscriptions missing or not visible.', 1;
    IF OBJECT_ID(N'dbo.PaymentOrders',N'U') IS NULL THROW 51002, 'dbo.PaymentOrders missing or not visible.', 1;
    IF COALESCE(HAS_PERMS_BY_NAME(N'dbo.UserSubscriptions',N'OBJECT',N'VIEW DEFINITION'),0) <> 1 OR
       COALESCE(HAS_PERMS_BY_NAME(N'dbo.PaymentOrders',N'OBJECT',N'VIEW DEFINITION'),0) <> 1
       THROW 51003, 'VIEW DEFINITION is required on both tables.', 1;
    IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
        WHERE c.object_id=OBJECT_ID(N'dbo.PaymentOrders') AND c.name=N'Id' AND t.name=N'bigint' AND c.is_nullable=0)
       THROW 51004, 'PaymentOrders.Id is not a non-null bigint; inspect schema before proceeding.', 1;
    IF COL_LENGTH(N'dbo.UserSubscriptions',N'ActivationPaymentOrderId') IS NOT NULL AND NOT EXISTS
      (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
       WHERE c.object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND c.name=N'ActivationPaymentOrderId' AND t.name=N'bigint' AND c.is_nullable=1)
       THROW 51005, 'ActivationPaymentOrderId exists with an incompatible type or nullability.', 1;
    IF COL_LENGTH(N'dbo.UserSubscriptions',N'GrantedTierLevel') IS NOT NULL AND NOT EXISTS
      (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id
       WHERE c.object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND c.name=N'GrantedTierLevel' AND t.name=N'int' AND c.is_nullable=1)
       THROW 51006, 'GrantedTierLevel exists with an incompatible type or nullability.', 1;

    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.UserSubscriptions',N'ActivationPaymentOrderId') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.UserSubscriptions ADD ActivationPaymentOrderId bigint NULL;';
    IF COL_LENGTH(N'dbo.UserSubscriptions',N'GrantedTierLevel') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.UserSubscriptions ADD GrantedTierLevel int NULL;';

    -- Không cho một đơn kích hoạt nhiều subscription; dữ liệu trùng sẽ làm rollback.
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND name=N'UX_US_ActivationPaymentOrder')
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND i.name=N'UX_US_ActivationPaymentOrder'
              AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=1 AND ic.key_ordinal=1 AND c.name=N'ActivationPaymentOrderId'
              AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal>0)=1
              AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(i.filter_definition),N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N'')=N'ACTIVATIONPAYMENTORDERIDISNOTNULL')
            THROW 51007, 'Existing activation index differs from the required unique filtered index.', 1;
    END
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE i.object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=1
          AND ic.key_ordinal=1 AND c.name=N'ActivationPaymentOrderId'
          AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal>0)=1
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(UPPER(i.filter_definition),N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N'')=N'ACTIVATIONPAYMENTORDERIDISNOTNULL')
        EXEC sys.sp_executesql N'CREATE UNIQUE INDEX UX_US_ActivationPaymentOrder ON dbo.UserSubscriptions(ActivationPaymentOrderId) WHERE ActivationPaymentOrderId IS NOT NULL;';

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_US_ActivationPaymentOrder')
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
            JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
            WHERE f.name=N'FK_US_ActivationPaymentOrder' AND f.parent_object_id=OBJECT_ID(N'dbo.UserSubscriptions')
              AND f.referenced_object_id=OBJECT_ID(N'dbo.PaymentOrders') AND f.is_disabled=0 AND f.is_not_trusted=0
              AND pc.name=N'ActivationPaymentOrderId' AND rc.name=N'Id'
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
            THROW 51008, 'Existing activation foreign key differs or is disabled/untrusted.', 1;
    END
    ELSE IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
        JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
        JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
        WHERE f.parent_object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND f.referenced_object_id=OBJECT_ID(N'dbo.PaymentOrders')
          AND f.is_disabled=0 AND f.is_not_trusted=0 AND pc.name=N'ActivationPaymentOrderId' AND rc.name=N'Id'
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
        EXEC sys.sp_executesql N'ALTER TABLE dbo.UserSubscriptions WITH CHECK ADD CONSTRAINT FK_US_ActivationPaymentOrder FOREIGN KEY(ActivationPaymentOrderId) REFERENCES dbo.PaymentOrders(Id);';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT DB_NAME() AS DatabaseName, c.name AS ColumnName, t.name AS SqlType, c.is_nullable AS IsNullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE c.object_id=OBJECT_ID(N'dbo.UserSubscriptions') AND c.name IN (N'ActivationPaymentOrderId',N'GrantedTierLevel')
ORDER BY c.name;
