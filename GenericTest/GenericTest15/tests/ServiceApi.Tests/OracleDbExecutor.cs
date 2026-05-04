using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace ServiceApi.Tests;

public class OracleDbExecutor(string connectionString) : IDisposable
{
    private OracleConnection Connection = new OracleConnection(connectionString);

    /// <summary>
    /// SQLを実行し、結果を1行ずつマッピングして返却する（SELECT専用）
    /// </summary>
    public async IAsyncEnumerable<T> ExecuteQueryAsync<T>(
        string sql,
        object? parameters,
        Func<IDataRecord, T> map)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return map(reader);
        }
    }

    /// <summary>
    /// SQLを実行し、結果をDataTableで返却する（SELECT専用）
    /// </summary>
    public DataTable ExecuteQuery(string sql) => ExecuteQuery(sql, _ => { });

    /// <summary>
    /// SQLを実行し、結果をDataTableで返却する（SELECT専用）
    /// </summary>
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

    /// <summary>
    /// コネクションを解放する
    /// </summary>
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