namespace StringToHash.Generator;

internal sealed record HashClass(string OriginalName, string MemberName) : HashMember(MemberName)
{
    public SortedDictionary<string, HashMember> Members { get; } = new(StringComparer.Ordinal);
}
