[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StringToHashAttribute(params string[] names) : Attribute
{
    public string[] Names { get; } = names;
}
