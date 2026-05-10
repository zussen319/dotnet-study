namespace ServiceApi.Common;

/*
 * コンスタント定義クラス
 */
public static class ApiConstants
{
    // SQL用の日付フォーマット
    /*
     * DBのDATE型はAPIでは文字列で保持することとし、以下の形式で取得する
     * [SQL]
     *   SELECT TO_CHAR(SYSDATE, :SQL_DATE_FORMAT) FROM DUAL
     * [パラメータバインド]
     *   OracleParameter("SQL_DATE_FORMAT", ApiConstants.SqlDateFormat);
     */
    public const string SqlDateFormat = "yyyy/mm/dd";

    // DB取得時のフェッチ行数
    public const int DefaultFetchRows = 100;
}
