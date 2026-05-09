namespace ServiceApi.Responses.C2;

/// <summary>
/// 
/// </summary>
public class C2Response : ResponseBase
{
    // レベル１：Dept情報
    /// <summary></summary>
    public required decimal DEPTNO { get; init; }
    /// <summary></summary>
    public string DNAME { get; init; } = string.Empty;

    // レベル２：Member情報
    /// <summary></summary>
    public List<Member> Members { get; init; } = [];

    /// <summary></summary>
    public class Member
    {
        /// <summary></summary>
        public required decimal MEMBER_EMPNO { get; init; }
        /// <summary></summary>
        public string MEMBER_ENAME { get; init; } = string.Empty;

        // レベル３：Staff情報
        /// <summary></summary>
        public List<Staff> Staffs { get; init; } = [];
    }

    /// <summary></summary>
    public class Staff
    {
        /// <summary></summary>
        public required decimal STAFF_EMPNO { get; init; }
        /// <summary></summary>
        public string STAFF_ENAME { get; init; } = string.Empty;
    }
}