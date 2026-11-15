namespace StringToHash.Generator;

internal sealed record HashField(string MemberName, string HashInput) : HashMember(MemberName);
