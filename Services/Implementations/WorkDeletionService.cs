using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietVan.API.Data;

namespace VietVan.API.Services.Implementations;

public enum WorkDeleteResult { Deleted, NotFound, HasDependencies }

public class WorkDeletionService(VietVanDbContext db)
{
    public async Task<WorkDeleteResult> DeleteAsync(int id, CancellationToken ct)
    {
        // Serializable range locks protect the dependency checks and delete as one operation.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
DECLARE @result int = 0, @found bit = 0, @sql nvarchar(max), @schema sysname, @table sysname, @column sysname;
IF NOT EXISTS (SELECT 1 FROM dbo.LiteraryWorks WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id)
    SELECT 1;
ELSE
BEGIN
    -- Check all declared foreign keys, including cascade relationships, and legacy work ID columns.
    DECLARE refs CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT s.name, t.name, c.name
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id
    WHERE t.object_id <> OBJECT_ID(N'dbo.LiteraryWorks')
      AND (EXISTS (
          SELECT 1 FROM sys.foreign_key_columns fk
          JOIN sys.columns target ON target.object_id = fk.referenced_object_id AND target.column_id = fk.referenced_column_id
          WHERE fk.parent_object_id = t.object_id AND fk.parent_column_id = c.column_id
            AND fk.referenced_object_id = OBJECT_ID(N'dbo.LiteraryWorks') AND target.name = N'Id'
      ) OR (c.name IN (N'WorkId', N'LiteraryWorkId') AND c.system_type_id = TYPE_ID(N'int')));
    OPEN refs;
    FETCH NEXT FROM refs INTO @schema, @table, @column;
    WHILE @@FETCH_STATUS = 0 AND @found = 0
    BEGIN
        SET @sql = N'IF EXISTS (SELECT 1 FROM ' + QUOTENAME(@schema) + N'.' + QUOTENAME(@table)
            + N' WITH (UPDLOCK, HOLDLOCK) WHERE ' + QUOTENAME(@column) + N' = @workId) SET @hasRows = 1;';
        EXEC sys.sp_executesql @sql, N'@workId int, @hasRows bit OUTPUT', @id, @found OUTPUT;
        FETCH NEXT FROM refs INTO @schema, @table, @column;
    END
    CLOSE refs;
    DEALLOCATE refs;
    IF @found = 1 SELECT 2;
    ELSE
    BEGIN
        DELETE FROM dbo.LiteraryWorks WHERE Id = @id;
        SELECT 0;
    END
END
""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.DbType = DbType.Int32;
        parameter.Value = id;
        command.Parameters.Add(parameter);
        try
        {
            var result = (WorkDeleteResult)Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            await transaction.RollbackAsync(ct);
            return WorkDeleteResult.HasDependencies;
        }
    }
}

