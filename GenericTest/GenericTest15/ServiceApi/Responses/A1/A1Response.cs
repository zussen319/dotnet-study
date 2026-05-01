using System.Data;
namespace ServiceApi.Responses.A1;

public class A1Response : ResponseBase
{
    public required int Id { get; init; }
    public required string DataName { get; init; }
#if false
    // ★追加: 引数なしコンストラクタ
    // SetsRequiredMemberをつけることで、new() 制約を通過できるようにします
    /*
     * A1Response クラスに required プロパティが存在する一方で、
     * 引数なしのコンストラクタでそれらを初期化していないことにあります。
     * C#の new() 制約（where T : new()）は、「引数なしのコンストラクタで
     * 正常にインスタンス化できること」を保証する必要がありますが、
     * required があると「ただ new() しただけでは不完全なオブジェクトになる」ため
     * コンパイラが安全性のためにブロックしている状況です。
     * これを解決するには、A1Response に「中身は空だけど、とりあえずインスタンス化は許す」
     * ためのコンストラクタを追加します。
     */
    [SetsRequiredMembers]
    public A1Response() { Id = 0; DataName = string.Empty; } // 警告を消すために初期値を代入

    internal override ResponseBase MapFromReader(IDataRecord reader)
    {
        // 既存の自分を更新するのではなく、新しい自分を作って返す
        return new A1Response
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            DataName = reader.GetString(reader.GetOrdinal("DATANAME"))
        };
    }
#endif
}