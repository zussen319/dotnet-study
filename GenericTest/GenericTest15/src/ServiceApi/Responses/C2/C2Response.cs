namespace ServiceApi.Responses.C2;

#if true
public record C2Response : ResponseBase
{
    // レベル１：Dept情報
    public required decimal DEPTNO { get; init; }
    public string DNAME { get; init; } = string.Empty;

    // レベル２：Member情報
    public List<Member> Members { get; init; } = [];

    public record Member
    {
        public required decimal MEMBER_EMPNO { get; init; }
        public string MEMBER_ENAME { get; init; } = string.Empty;

        // レベル３：Staff情報
        public List<Staff> Staffs { get; init; } = [];
    }

    public record Staff
    {
        public required decimal STAFF_EMPNO { get; init; }
        public string STAFF_ENAME { get; init; } = string.Empty;
    }
}
#else
public class C2Response : ResponseBase
{
    // レベル１：Dept情報
    public required decimal DEPTNO { get; init; }
    public string DNAME { get; init; } = string.Empty;

    // レベル２：Member情報
    public List<Member> Members { get; init; } = [];

    public class Member
    {
        public required decimal MEMBER_EMPNO { get; init; }
        public string MEMBER_ENAME { get; init; } = string.Empty;

        // レベル３：Staff情報
        public List<Staff> Staffs { get; init; } = [];
    }

    public class Staff
    {
        public required decimal STAFF_EMPNO { get; init; }
        public string STAFF_ENAME { get; init; } = string.Empty;
    }
}
#endif
