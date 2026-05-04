using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace ServiceApi.Tests;

public class OracleDbExecutor(string connectionString) : IDisposable
{
    private OracleConnection Connection = new OracleConnection(connectionString);

#if false
    // コネクションを作成するヘルパー
    private async Task<OracleConnection> CreateConnectionAsync()
    {
        var conn = new OracleConnection(connectionString);
        await conn.OpenAsync();
        return conn;
    }
#endif

    /// <summary>
    /// SQLを実行し、結果を1行ずつマッピングして返却する（SELECT専用）
    /// </summary>
    public async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
        string sql,
        object? parameters,
        Func<IDataRecord, T> map)
    {
        //using var conn = await CreateConnectionAsync();
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return map(reader);
        }
    }

    public DataTable ExecuteQuery(string sql) => ExecuteQuery(sql, _ => { });

    public DataTable ExecuteQuery(
        string sql,
        Action<OracleParameterCollection> bindAction)
    {
        if (this.Connection.State != ConnectionState.Open)
        {
            this.Connection.Open();
        }

        using var cmd = new OracleCommand(sql, this.Connection);
        cmd.BindByName = true;
        bindAction(cmd.Parameters);

        DataTable dt = new();
        using (var adapter = new OracleDataAdapter(cmd))
        {
            adapter.Fill(dt);
        }
        return dt;
    }

    public void Dispose()
    {
        if (this.Connection?.State == ConnectionState.Open)
        {
            this.Connection.Close();
        }
        this.Connection?.Dispose();

        GC.SuppressFinalize(this);
    }
}