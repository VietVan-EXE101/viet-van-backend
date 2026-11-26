using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VietVan.API.Data;

/// <summary>Retry opening SQL connections only; never replay commands or user transactions.</summary>
public sealed class SqlConnectionOpeningRetryInterceptor : DbConnectionInterceptor
{
    public static SqlConnectionOpeningRetryInterceptor Instance { get; } = new();

    public static void Configure(SqlConnection connection) => connection.RetryLogicProvider =
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 3,
            DeltaTime = TimeSpan.FromSeconds(1),
            MaxTimeInterval = TimeSpan.FromSeconds(5)
        });

    public override DbConnection ConnectionCreated(ConnectionCreatedEventData eventData, DbConnection result)
    {
        if (result is SqlConnection sql) Configure(sql);
        return result;
    }
}
