using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StringToHash.Generator;

internal static class StringToHashDiagnostics
{
    private static readonly DiagnosticDescriptor InvalidTarget = CreateDescriptor(
        id: "STH0001",
        title: "Invalid hash class",
        message: "The class '{0}' must be declared static partial where StringToHash is applied"
    );

    private static readonly DiagnosticDescriptor InvalidContainingType = CreateDescriptor(
        id: "STH0002",
        title: "Invalid containing type",
        message: "The containing type '{0}' must be partial and accessible from another file"
    );

    private static readonly DiagnosticDescriptor MissingNames = CreateDescriptor(
        id: "STH0003",
        title: "Missing Animator names",
        message: "Supply at least one Animator name in a non-null array"
    );

    private static readonly DiagnosticDescriptor MissingName = CreateDescriptor(
        id: "STH0004",
        title: "Missing Animator name",
        message: "An Animator name cannot be null or empty"
    );

    private static readonly DiagnosticDescriptor EmptySegment = CreateDescriptor(
        id: "STH0005",
        title: "Empty name segment",
        message: "The Animator name '{0}' contains an empty path segment"
    );

    private static readonly DiagnosticDescriptor InvalidCharacters = CreateDescriptor(
        id: "STH0006",
        title: "Invalid characters in Animator name",
        message: "{0} cannot be used at position {1} in the Animator name {2}"
    );

    private static readonly DiagnosticDescriptor DuplicateName = CreateDescriptor(
        id: "STH0007",
        title: "Duplicate Animator name",
        message: "The Animator name '{0}' is specified more than once"
    );

    private static readonly DiagnosticDescriptor MemberConflict = CreateDescriptor(
        id: "STH0008",
        title: "Conflicting generated member",
        message: "The segment '{0}' in Animator name '{1}' conflicts with the member or type name '{2}'"
    );

    private static readonly DiagnosticDescriptor FileLocalTarget = CreateDescriptor(
        id: "STH0009",
        title: "File-local hash class",
        message: "The hash class '{0}' cannot be file-local"
    );

    public static Diagnostic CreateInvalidTarget(Location location, string className)
    {
        return Diagnostic.Create(InvalidTarget, location, className);
    }

    public static Diagnostic CreateInvalidContainingType(Location location, string typeName)
    {
        return Diagnostic.Create(InvalidContainingType, location, typeName);
    }

    public static Diagnostic CreateMissingNames(Location location)
    {
        return Diagnostic.Create(MissingNames, location);
    }

    public static Diagnostic CreateMissingName(Location location)
    {
        return Diagnostic.Create(MissingName, location);
    }

    public static Diagnostic CreateEmptySegment(Location location, string animatorName)
    {
        return Diagnostic.Create(EmptySegment, location, animatorName);
    }

    public static Diagnostic CreateInvalidCharacters(
        Location location,
        string invalidText,
        int offset,
        string animatorName
    )
    {
        return Diagnostic.Create(
            InvalidCharacters,
            location,
            SymbolDisplay.FormatLiteral(invalidText, quote: true),
            offset + 1,
            SymbolDisplay.FormatLiteral(animatorName, quote: true)
        );
    }

    public static Diagnostic CreateDuplicateName(Location location, string animatorName)
    {
        return Diagnostic.Create(DuplicateName, location, animatorName);
    }

    public static Diagnostic CreateMemberConflict(
        Location location,
        string segment,
        string animatorName,
        string memberName
    )
    {
        return Diagnostic.Create(MemberConflict, location, segment, animatorName, memberName);
    }

    public static Diagnostic CreateFileLocalTarget(Location location, string className)
    {
        return Diagnostic.Create(FileLocalTarget, location, className);
    }

    private static DiagnosticDescriptor CreateDescriptor(string id, string title, string message)
    {
        return new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: message,
            category: "StringToHash",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );
    }
}
