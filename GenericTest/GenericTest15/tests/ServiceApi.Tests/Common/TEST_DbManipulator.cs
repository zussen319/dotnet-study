using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Data.Common;

namespace ServiceApi.Tests.Common;

public class TEST_DbManipulator(string connectionString) : IDisposable
{
    private OracleConnection Connection = new OracleConnection(connectionString);

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

        using OracleCommand cmd = this.Connection.CreateCommand();
        cmd.CommandText = sql;

        cmd.BindByName = true;
        bindAction(cmd.Parameters);

        using OracleDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            yield return mapFunc(reader);
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
        if (this.Connection.State is not ConnectionState.Open) { this.Connection.Open(); }

        using OracleCommand cmd = new(sql, this.Connection) { BindByName = true };
        bindAction(cmd.Parameters);

        DataTable dt = new();
        using (OracleDataAdapter adapter = new(cmd)) { adapter.Fill(dt); }
        return dt;
    }

    /// <summary>
    /// コネクションを解放する
    /// </summary>
    public void Dispose()
    {
        if (this.Connection?.State is ConnectionState.Open)
        {
            this.Connection.Close();
        }
        this.Connection?.Dispose();

        GC.SuppressFinalize(this);
    }
}
