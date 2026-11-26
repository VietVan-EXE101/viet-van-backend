/*
  VietVan_New / Somee: 14 missing Payment columns confirmed on 2026-11-26.
  Default PREVIEW only. Select VietVan_New in SSMS; run the WHOLE script.
  Back up first and stop payment writers before setting @ApplyChanges = 1.

  Adds missing columns and makes four confirmed obsolete PaymentOrders columns
  nullable ONLY when PaymentOrders is empty: DurationInDays, PackageName, Price,
  TierLevel. Keeps their names, types, lengths, collation and existing constraints.
  Preserves all existing columns/rows/IDs/statuses; no dummy defaults.
  Does NOT copy current package prices into historical orders, invent money,
  durations, currency, webhook hashes/outcomes or audit history.
  Stops before DDL if a missing NOT NULL column has historical rows, an existing
  target column conflicts, or unmapped required legacy columns block EF INSERT.
  Such cases need an explicit mapping based on the actual legacy schema/data.
  No baseline, seed, migration-history changes, DROP, DELETE or UPDATE.
  Not called by application startup. Requires SQL Server 2019 or later.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;
DECLARE @ApplyChanges bit = 0;

IF DB_NAME() <> N'VietVan_New'
    THROW 51000, 'Select the imported VietVan_New database on Somee first.', 1;
IF @@TRANCOUNT <> 0
    THROW 51001, 'Run the whole script outside an existing transaction.', 1;

DECLARE @Tables TABLE (Seq int PRIMARY KEY, TableName sysname, RowsBefore bigint NULL);
INSERT INTO @Tables (Seq, TableName) VALUES
    (1,N'PaymentOrders'), (2,N'PaymentAuditLogs'),
    (3,N'PaymentBankTransactions'), (4,N'PaymentWebhookEvents');
IF EXISTS (SELECT 1 FROM @Tables WHERE OBJECT_ID(N'dbo.' + TableName,N'U') IS NULL)
    THROW 51002, 'A required Payment table is missing or not visible. Do not run a baseline.', 1;

DECLARE @Expected TABLE (
    TableName sysname, ColumnName sysname, Definition nvarchar(100),
    TypeName sysname, MaxLength smallint, PrecisionValue tinyint, ScaleValue tinyint, IsNullable bit,
    PRIMARY KEY (TableName,ColumnName)
);
INSERT INTO @Expected VALUES
    (N'PaymentOrders',N'Amount',N'decimal(18,2) NOT NULL',N'decimal',9,18,2,0),
    (N'PaymentOrders',N'BillingPeriodSnapshot',N'nvarchar(16) NULL',N'nvarchar',32,0,0,1),
    (N'PaymentOrders',N'Currency',N'nvarchar(3) NOT NULL',N'nvarchar',6,0,0,0),
    (N'PaymentOrders',N'DurationDaysSnapshot',N'int NOT NULL',N'int',4,10,0,0),
    (N'PaymentOrders',N'PackageNameSnapshot',N'nvarchar(100) NOT NULL',N'nvarchar',200,0,0,0),
    (N'PaymentOrders',N'ReviewReason',N'nvarchar(500) NULL',N'nvarchar',1000,0,0,1),
    (N'PaymentOrders',N'TierLevelSnapshot',N'int NOT NULL',N'int',4,10,0,0),
    (N'PaymentAuditLogs',N'Detail',N'nvarchar(1000) NOT NULL',N'nvarchar',2000,0,0,0),
    (N'PaymentBankTransactions',N'AccountNumberMasked',N'nvarchar(30) NULL',N'nvarchar',60,0,0,1),
    (N'PaymentBankTransactions',N'ClaimedOrderCode',N'nvarchar(30) NULL',N'nvarchar',60,0,0,1),
    (N'PaymentBankTransactions',N'Currency',N'nvarchar(3) NOT NULL',N'nvarchar',6,0,0,0),
    (N'PaymentBankTransactions',N'ReviewReason',N'nvarchar(500) NOT NULL',N'nvarchar',1000,0,0,0),
    (N'PaymentWebhookEvents',N'Outcome',N'nvarchar(100) NOT NULL',N'nvarchar',200,0,0,0),
    (N'PaymentWebhookEvents',N'PayloadHash',N'nvarchar(64) NOT NULL',N'nvarchar',128,0,0,0);

-- Complete scalar properties inserted by the current EF model, not just missing columns.
DECLARE @Mapped TABLE (TableName sysname, ColumnName sysname, PRIMARY KEY(TableName,ColumnName));
INSERT INTO @Mapped VALUES
    (N'PaymentOrders',N'Id'), (N'PaymentOrders',N'OrderCode'), (N'PaymentOrders',N'UserId'),
    (N'PaymentOrders',N'PackageId'), (N'PaymentOrders',N'PackageNameSnapshot'), (N'PaymentOrders',N'Amount'),
    (N'PaymentOrders',N'DurationDaysSnapshot'), (N'PaymentOrders',N'TierLevelSnapshot'),
    (N'PaymentOrders',N'BillingPeriodSnapshot'), (N'PaymentOrders',N'Currency'), (N'PaymentOrders',N'Provider'),
    (N'PaymentOrders',N'Status'), (N'PaymentOrders',N'IdempotencyKey'), (N'PaymentOrders',N'CreatedAt'),
    (N'PaymentOrders',N'ExpiresAt'), (N'PaymentOrders',N'PaidAt'), (N'PaymentOrders',N'ReviewReason'),
    (N'PaymentAuditLogs',N'Id'), (N'PaymentAuditLogs',N'PaymentOrderId'), (N'PaymentAuditLogs',N'ActorUserId'),
    (N'PaymentAuditLogs',N'Action'), (N'PaymentAuditLogs',N'Detail'), (N'PaymentAuditLogs',N'CreatedAt'),
    (N'PaymentBankTransactions',N'Id'), (N'PaymentBankTransactions',N'Provider'),
    (N'PaymentBankTransactions',N'ProviderTransactionId'), (N'PaymentBankTransactions',N'PaymentOrderId'),
    (N'PaymentBankTransactions',N'Amount'), (N'PaymentBankTransactions',N'Currency'),
    (N'PaymentBankTransactions',N'AccountNumberMasked'), (N'PaymentBankTransactions',N'ClaimedOrderCode'),
    (N'PaymentBankTransactions',N'Status'), (N'PaymentBankTransactions',N'ReviewReason'),
    (N'PaymentBankTransactions',N'ReceivedAt'), (N'PaymentWebhookEvents',N'Id'),
    (N'PaymentWebhookEvents',N'Provider'), (N'PaymentWebhookEvents',N'ProviderEventId'),
    (N'PaymentWebhookEvents',N'PayloadHash'), (N'PaymentWebhookEvents',N'Outcome'),
    (N'PaymentWebhookEvents',N'ReceivedAt');

BEGIN TRY
    IF @ApplyChanges = 1 BEGIN TRANSACTION;
    DECLARE @Seq int = 1, @Table sysname, @Rows bigint, @Sql nvarchar(max);
    WHILE @Seq <= 4
    BEGIN
        SELECT @Table=TableName FROM @Tables WHERE Seq=@Seq;
        -- Apply locks each whole table until COMMIT so emptiness checks cannot race INSERTs.
        SET @Sql=N'SELECT @n=COUNT_BIG(*) FROM dbo.' + QUOTENAME(@Table)
            + CASE WHEN @ApplyChanges=1 THEN N' WITH (TABLOCKX,HOLDLOCK);' ELSE N';' END;
        EXEC sys.sp_executesql @Sql,N'@n bigint OUTPUT',@n=@Rows OUTPUT;
        UPDATE @Tables SET RowsBefore=@Rows WHERE Seq=@Seq;
        SET @Seq+=1;
    END;

    DECLARE @Conflicts TABLE (TableName sysname, ColumnName sysname);
    INSERT INTO @Conflicts
    SELECT e.TableName,e.ColumnName FROM @Expected e
    JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE t.name<>e.TypeName OR c.max_length<>e.MaxLength OR c.precision<>e.PrecisionValue
       OR c.scale<>e.ScaleValue OR c.is_nullable<>e.IsNullable OR c.is_computed<>0 OR c.is_identity<>0;

    DECLARE @HistoryBlockers TABLE (TableName sysname, ColumnName sysname, RowsExisting bigint);
    INSERT INTO @HistoryBlockers
    SELECT e.TableName,e.ColumnName,t.RowsBefore FROM @Expected e JOIN @Tables t ON t.TableName=e.TableName
    WHERE e.IsNullable=0 AND t.RowsBefore>0
      AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName);

    DECLARE @InsertBlockers TABLE (TableName sysname, ColumnName sysname);
    INSERT INTO @InsertBlockers
    SELECT t.TableName,c.name FROM @Tables t JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+t.TableName)
    WHERE c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0 AND c.default_object_id=0
      AND c.generated_always_type=0 AND c.system_type_id<>189
      AND NOT EXISTS (SELECT 1 FROM @Mapped m WHERE m.TableName=t.TableName AND m.ColumnName=c.name);

    -- Confirmed by the operator's Somee result sets on 2026-11-26. These four
    -- obsolete columns are NOT EF properties. Do not rename/drop them or invent
    -- defaults. Preserve the exact built-in type and character collation.
    DECLARE @LegacyChanges TABLE (
        Seq int IDENTITY(1,1), ColumnName sysname, Definition nvarchar(300)
    );
    INSERT INTO @LegacyChanges(ColumnName,Definition)
    SELECT c.name,
        CASE WHEN ty.name IN (N'nvarchar',N'varchar') THEN ty.name + N'('
                  + CASE WHEN c.max_length=-1 THEN N'max'
                         ELSE CONVERT(nvarchar(10),c.max_length / CASE WHEN ty.name=N'nvarchar' THEN 2 ELSE 1 END) END
                  -- COLLATE requires an unquoted collation name, unlike column identifiers.
                  + N') COLLATE ' + c.collation_name
             WHEN ty.name IN (N'decimal',N'numeric') THEN ty.name + N'('
                  + CONVERT(nvarchar(10),c.precision)+N','+CONVERT(nvarchar(10),c.scale)+N')'
             ELSE ty.name END + N' NULL'
    FROM @InsertBlockers b
    JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.PaymentOrders') AND c.name=b.ColumnName
    JOIN sys.types ty ON ty.user_type_id=c.user_type_id
    WHERE b.TableName=N'PaymentOrders' AND ty.is_user_defined=0
      AND (ty.name NOT IN (N'nvarchar',N'varchar')
        OR (c.collation_name COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9_]%'
            AND EXISTS(SELECT 1 FROM sys.fn_helpcollations() hc WHERE hc.name=c.collation_name)))
      AND ((c.name IN (N'DurationInDays',N'TierLevel') AND ty.name IN (N'int',N'smallint',N'tinyint',N'bigint'))
        OR (c.name=N'Price' AND ty.name IN (N'decimal',N'numeric',N'money',N'smallmoney'))
        OR (c.name=N'PackageName' AND ty.name IN (N'nvarchar',N'varchar')));

    SELECT DB_NAME() AS DatabaseName,@ApplyChanges AS ApplyChanges;
    SELECT TableName,RowsBefore FROM @Tables ORDER BY Seq;
    SELECT e.TableName,e.ColumnName,e.Definition AS ExpectedDefinition,
        CASE WHEN EXISTS(SELECT 1 FROM @Conflicts x WHERE x.TableName=e.TableName AND x.ColumnName=e.ColumnName)
                  THEN N'INCOMPATIBLE - STOP'
             WHEN c.column_id IS NOT NULL THEN N'ALREADY MATCHES'
             WHEN EXISTS(SELECT 1 FROM @HistoryBlockers h WHERE h.TableName=e.TableName AND h.ColumnName=e.ColumnName)
                  THEN N'HISTORY MAPPING REQUIRED - STOP'
             ELSE N'WILL ADD' END AS Status
    FROM @Expected e LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName
    ORDER BY e.TableName,e.ColumnName;
    -- Metadata only: no amounts, order content, webhook payloads or account numbers are printed.
    SELECT t.TableName,c.name AS ColumnName,ty.name AS TypeName,c.max_length AS LengthBytes,
        c.precision,c.scale,c.is_nullable,c.is_identity,c.is_computed,
        CASE WHEN c.default_object_id=0 THEN 0 ELSE 1 END AS HasDefault
    FROM @Tables t JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+t.TableName)
    JOIN sys.types ty ON ty.user_type_id=c.user_type_id ORDER BY t.Seq,c.column_id;
    SELECT N'LEGACY INSERT BLOCKER - explicit mapping/default review required' AS Reason,TableName,ColumnName
    FROM @InsertBlockers b
    WHERE NOT (b.TableName=N'PaymentOrders'
        AND EXISTS(SELECT 1 FROM @LegacyChanges l WHERE l.ColumnName=b.ColumnName)
        AND (SELECT RowsBefore FROM @Tables WHERE TableName=N'PaymentOrders')=0)
    ORDER BY TableName,ColumnName;
    SELECT N'PaymentOrders' AS TableName,l.ColumnName,l.Definition AS ProposedDefinition,
        CASE WHEN t.RowsBefore=0 THEN N'WILL MAKE OBSOLETE COLUMN NULLABLE'
             ELSE N'HISTORICAL ORDERS - STOP' END AS Status
    FROM @LegacyChanges l JOIN @Tables t ON t.TableName=N'PaymentOrders' ORDER BY l.Seq;

    IF @ApplyChanges=0
    BEGIN
        PRINT 'PREVIEW ONLY: no persistent schema/data changes. Review all result sets before apply.';
        PRINT 'History/legacy INSERT blockers require a specific mapping; do not invent historical values.';
        RETURN;
    END;
    IF EXISTS(SELECT 1 FROM @Conflicts)
        THROW 51003,'An existing target column has incompatible metadata. No DDL applied.',1;
    IF EXISTS(SELECT 1 FROM @HistoryBlockers)
        THROW 51004,'Historical rows need an explicit mapping for missing NOT NULL fields. No DDL applied.',1;
    IF EXISTS(SELECT 1 FROM @LegacyChanges)
        AND (SELECT RowsBefore FROM @Tables WHERE TableName=N'PaymentOrders')<>0
        THROW 51008,'PaymentOrders must be empty before changing the four confirmed obsolete columns. No DDL applied.',1;
    IF EXISTS(SELECT 1 FROM @InsertBlockers b WHERE NOT (b.TableName=N'PaymentOrders'
        AND EXISTS(SELECT 1 FROM @LegacyChanges l WHERE l.ColumnName=b.ColumnName)))
        THROW 51005,'Unmapped required legacy columns would reject EF INSERTs. Review mapping before DDL.',1;

    DECLARE @LegacyCount int=(SELECT COUNT(*) FROM @LegacyChanges),@LegacyColumn sysname,@LegacyDefinition nvarchar(300);
    SET @Seq=1;
    WHILE @Seq<=@LegacyCount
    BEGIN
        SELECT @LegacyColumn=ColumnName,@LegacyDefinition=Definition FROM @LegacyChanges WHERE Seq=@Seq;
        SET @Sql=N'ALTER TABLE dbo.[PaymentOrders] ALTER COLUMN '+QUOTENAME(@LegacyColumn)+N' '+@LegacyDefinition+N';';
        EXEC sys.sp_executesql @Sql;
        SET @Seq+=1;
    END;

    DECLARE @Changes TABLE (Seq int IDENTITY(1,1),TableName sysname,ColumnName sysname,Definition nvarchar(100));
    INSERT INTO @Changes(TableName,ColumnName,Definition)
    SELECT e.TableName,e.ColumnName,e.Definition FROM @Expected e
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName);
    DECLARE @ChangeCount int=(SELECT COUNT(*) FROM @Changes),@Column sysname,@Definition nvarchar(100);
    SET @Seq=1;
    WHILE @Seq<=@ChangeCount
    BEGIN
        SELECT @Table=TableName,@Column=ColumnName,@Definition=Definition FROM @Changes WHERE Seq=@Seq;
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Table)+N' ADD '+QUOTENAME(@Column)+N' '+@Definition+N';';
        EXEC sys.sp_executesql @Sql;
        SET @Seq+=1;
    END;

    IF EXISTS(SELECT 1 FROM @Expected e LEFT JOIN sys.columns c
        ON c.object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName WHERE c.column_id IS NULL)
        THROW 51006,'A required column is still absent. Rolling back.',1;
    IF EXISTS(SELECT 1 FROM @LegacyChanges l JOIN sys.columns c
        ON c.object_id=OBJECT_ID(N'dbo.PaymentOrders') AND c.name=l.ColumnName WHERE c.is_nullable=0)
        THROW 51009,'An obsolete column still rejects NULL. Rolling back.',1;
    SET @Seq=1;
    WHILE @Seq<=4
    BEGIN
        SELECT @Table=TableName FROM @Tables WHERE Seq=@Seq;
        SET @Sql=N'SELECT @n=COUNT_BIG(*) FROM dbo.'+QUOTENAME(@Table)+N';';
        EXEC sys.sp_executesql @Sql,N'@n bigint OUTPUT',@n=@Rows OUTPUT;
        IF @Rows<>(SELECT RowsBefore FROM @Tables WHERE Seq=@Seq)
            THROW 51007,'A table row count changed. Rolling back.',1;
        SET @Seq+=1;
    END;
    COMMIT TRANSACTION;
    SELECT N'APPLIED' AS Result,@ChangeCount AS ColumnsAdded,@LegacyCount AS LegacyColumnsMadeNullable;
    SELECT TableName,RowsBefore AS RowsPreserved FROM @Tables ORDER BY Seq;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
