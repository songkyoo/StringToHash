using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace StringToHash.Generator;

internal readonly struct NameArgument(SyntaxNode argument)
{
    #region Static
    public static ExpressionSyntax[] GetElementExpressions(ExpressionSyntax[] arguments)
    {
        if (arguments.Length != 1)
        {
            return arguments;
        }

        return Unwrap(arguments[0]) switch
        {
            ArrayCreationExpressionSyntax array => array.Initializer?.Expressions.ToArray() ?? [],
            ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.ToArray(),
            _ => arguments,
        };
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        return expression;
    }

    private static TextSpan? GetQuotedSpan(SyntaxToken token, TextSpan valueSpan)
    {
        var text = token.Text;
        var isVerbatim = text.StartsWith("@\"", StringComparison.Ordinal);
        var valueIndex = 0;
        var sourceStart = -1;

        for (var i = isVerbatim ? 2 : 1; i < text.Length - 1;)
        {
            var sourceWidth = 1;
            var valueWidth = 1;

            if (isVerbatim && text[i] == '"')
            {
                sourceWidth = 2;
            }
            else if (!isVerbatim && text[i] == '\\')
            {
                sourceWidth = GetEscapeWidth(text, i);

                if (text[i + 1] == 'U' && IsSupplementaryEscape(text, i))
                {
                    valueWidth = 2;
                }
            }

            valueIndex += valueWidth;

            if (sourceStart == -1 && valueIndex > valueSpan.Start)
            {
                sourceStart = token.SpanStart + i;
            }

            i += sourceWidth;

            if (valueIndex >= valueSpan.End)
            {
                return TextSpan.FromBounds(sourceStart, end: token.SpanStart + i);
            }
        }

        return null;
    }

    private static bool IsSupplementaryEscape(string text, int index)
    {
        var digits = text.Substring(index + 2, 8);
        var codePoint = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return codePoint > 0xFFFF;
    }

    private static int GetEscapeWidth(string text, int index)
    {
        switch (text[index + 1])
        {
            case 'u':
            {
                return 6;
            }
            case 'U':
            {
                return 10;
            }
            case 'x':
            {
                var end = index + 2;

                while (end < Math.Min(index + 6, text.Length) && Uri.IsHexDigit(text[end]))
                {
                    end++;
                }

                return end - index;
            }
            default:
            {
                return 2;
            }
        }
    }

    private static TextSpan GetSingleLineRawSpan(SyntaxToken token, TextSpan valueSpan)
    {
        var delimiterLength = (token.Text.Length - token.ValueText.Length) / 2;
        return new TextSpan(token.SpanStart + delimiterLength + valueSpan.Start, valueSpan.Length);
    }

    private static TextSpan? GetMultiLineRawSpan(SyntaxToken token, TextSpan valueSpan)
    {
        var sourceLines = token.SyntaxTree!.GetText().Lines;
        var openingLine = sourceLines.GetLineFromPosition(token.SpanStart);
        var valueLines = SourceText.From(token.ValueText).Lines;
        var firstValueLine = valueLines.GetLineFromPosition(valueSpan.Start);
        var lastValueLine = valueLines.GetLineFromPosition(valueSpan.End - 1);

        // 여러 줄에 걸친 위치에는 원문 값에 포함되지 않은 들여쓰기가 끼어들 수 있다.
        if (firstValueLine.LineNumber != lastValueLine.LineNumber)
            return null;

        var sourceLine = sourceLines[openingLine.LineNumber + 1 + firstValueLine.LineNumber];
        var contentStart = sourceLine.End - firstValueLine.Span.Length;
        return new TextSpan(contentStart + valueSpan.Start - firstValueLine.Start, valueSpan.Length);
    }
    #endregion

    #region Methods
    public Location GetLocation()
    {
        return argument.GetLocation();
    }

    public Location GetLocation(TextSpan valueSpan)
    {
        if (argument is not ExpressionSyntax expression
            || Unwrap(expression) is not LiteralExpressionSyntax { ContainsDiagnostics: false } literal
        )
        {
            return GetLocation();
        }

        var token = literal.Token;
        var span = token.Kind() switch
        {
            SyntaxKind.StringLiteralToken => GetQuotedSpan(token, valueSpan),
            SyntaxKind.SingleLineRawStringLiteralToken => GetSingleLineRawSpan(token, valueSpan),
            SyntaxKind.MultiLineRawStringLiteralToken => GetMultiLineRawSpan(token, valueSpan),
            _ => null,
        };

        return span is { } sourceSpan
            ? Location.Create(argument.SyntaxTree, sourceSpan)
            : GetLocation();
    }
    #endregion
}
