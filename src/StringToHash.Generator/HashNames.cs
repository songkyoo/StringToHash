using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using static StringToHash.Generator.StringToHashDiagnostics;

namespace StringToHash.Generator;

internal sealed class HashNames(INamedTypeSymbol targetType)
{
    #region Types
    private readonly record struct NameSegment(string OriginalName, string MemberName, TextSpan Span);
    #endregion

    #region Static
    private static List<NameSegment>? ParseSegments(NameArgument argument, string name, Action<Diagnostic> report)
    {
        var segments = new List<NameSegment>();
        var isValid = true;
        var offset = 0;
        var lastEmptySeparator = -1;

        foreach (var segment in name.Split('.'))
        {
            if (segment.Length == 0)
            {
                var separator = Math.Min(offset, name.Length - 1);

                isValid = false;

                if (separator != lastEmptySeparator)
                {
                    var location = argument.GetLocation(new TextSpan(separator, 1));

                    report(CreateEmptySegment(location, name));
                    lastEmptySeparator = separator;
                }
            }
            else
            {
                var segmentIsValid = true;

                foreach (var invalidSpan in FindInvalidCharacters(segment))
                {
                    var span = new TextSpan(offset + invalidSpan.Start, invalidSpan.Length);
                    var invalidText = name.Substring(span.Start, span.Length);

                    segmentIsValid = false;
                    report(CreateInvalidCharacters(
                        argument.GetLocation(span),
                        invalidText: invalidText,
                        offset: span.Start,
                        animatorName: name
                    ));
                }

                if (segmentIsValid)
                {
                    // C#이 이름 비교에 사용하는 값으로 기존 멤버 및 다른 입력과의 충돌을 검사한다.
                    var memberName = SyntaxFactory.ParseToken(segment.Replace(' ', '_')).ValueText;
                    var span = new TextSpan(offset, segment.Length);

                    segments.Add(new NameSegment(segment, memberName, span));
                }

                isValid &= segmentIsValid;
            }

            offset += segment.Length + 1;
        }

        return isValid ? segments : null;
    }

    private static IEnumerable<TextSpan> FindInvalidCharacters(string segment)
    {
        var firstNonSpace = 0;

        while (firstNonSpace < segment.Length && segment[firstNonSpace] == ' ')
        {
            firstNonSpace++;
        }

        var afterLastNonSpace = segment.Length;

        while (afterLastNonSpace > firstNonSpace && segment[afterLastNonSpace - 1] == ' ')
        {
            afterLastNonSpace--;
        }

        var invalidStart = -1;

        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            var isValid = character == ' '
                ? index >= firstNonSpace && index < afterLastNonSpace
                : IsIdentifierCharacter(character, isStart: index == 0);

            if (!isValid)
            {
                if (invalidStart == -1)
                {
                    invalidStart = index;
                }

                continue;
            }

            if (invalidStart != -1)
            {
                yield return TextSpan.FromBounds(invalidStart, index);

                invalidStart = -1;
            }
        }

        if (invalidStart != -1)
        {
            yield return TextSpan.FromBounds(invalidStart, segment.Length);
        }

        #region Local Functions
        static bool IsIdentifierCharacter(char character, bool isStart)
        {
            return isStart
                ? SyntaxFacts.IsIdentifierStartCharacter(character)
                : SyntaxFacts.IsIdentifierPartCharacter(character);
        }
        #endregion
    }

    private static void ReportConflict(
        NameArgument argument,
        string name,
        NameSegment segment,
        Action<Diagnostic> report
    )
    {
        report(CreateMemberConflict(
            argument.GetLocation(segment.Span),
            segment: segment.OriginalName,
            animatorName: name,
            memberName: segment.MemberName
        ));
    }
    #endregion

    #region Fields
    private readonly SortedDictionary<string, HashMember> _members = new(StringComparer.Ordinal);
    #endregion

    #region Properties
    public IEnumerable<HashMember> Members => _members.Values;
    #endregion

    #region Methods
    public void Add(NameArgument argument, string name, Action<Diagnostic> report)
    {
        var segments = ParseSegments(argument, name, report);

        if (segments is null || HasDeclarationConflicts(argument, name, segments, report))
        {
            return;
        }

        var members = _members;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var isField = i == segments.Count - 1;

            if (members.TryGetValue(segment.MemberName, out var existing))
            {
                if (isField || existing is not HashClass child || child.OriginalName != segment.OriginalName)
                {
                    ReportConflict(argument, name, segment, report);

                    return;
                }

                members = child.Members;

                continue;
            }

            if (isField)
            {
                var field = new HashField(segment.MemberName, name);

                members.Add(field.MemberName, field);

                return;
            }

            var nestedClass = new HashClass(segment.OriginalName, segment.MemberName);

            members.Add(nestedClass.MemberName, nestedClass);
            members = nestedClass.Members;
        }
    }

    private bool HasDeclarationConflicts(
        NameArgument argument,
        string name,
        List<NameSegment> segments,
        Action<Diagnostic> report
    )
    {
        var parentName = targetType.Name;
        var hasConflict = false;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            if (segment.MemberName == parentName || (i == 0 && IsDeclaredInTarget(segment.MemberName)))
            {
                ReportConflict(argument, name, segment, report);

                hasConflict = true;
            }

            parentName = segment.MemberName;
        }

        return hasConflict;
    }

    private bool IsDeclaredInTarget(string name)
    {
        return targetType.GetMembers(name).Length != 0
            || targetType.TypeParameters.Any(parameter => parameter.Name == name);
    }
    #endregion
}
