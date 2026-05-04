using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.Common;

namespace ServiceApi.Tests.Common;

public class TEST_DbManipulator(string connectionString) : IDisposable
{
    private OracleConnection Connection = new OracleConnection(connectionString);

#if true
    /// <summary>
    /// SQLを実行し、結果を1行ずつマッピングして返却する（SELECT専用）
    /// </summary>
    public async IAsyncEnumerable<TResponse> ExecuteQueryAsync<TResponse>(
        string sql,
        Action<OracleParameterCollection> bindAction,
        Func<DbDataReader, TResponse> mapFunc)
    {
        if (this.Connection.State != ConnectionState.Open)
        {
            await this.Connection.OpenAsync();
        }

        using var cmd = this.Connection.CreateCommand();
        cmd.CommandText = sql;

        cmd.BindByName = true;
        bindAction(cmd.Parameters);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return mapFunc(reader);
        }
    }
#else
    /// <summary>
    /// SQLを実行し、結果を1行ずつマッピングして返却する（SELECT専用）
    /// </summary>
    public async IAsyncEnumerable<TResponse> ExecuteQueryAsync<TResponse>(
        string sql,
        object? parameters,
        Func<IDataRecord, TResponse> map)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return map(reader);
        }
    }
#endif

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