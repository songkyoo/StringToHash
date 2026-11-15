using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static StringToHash.Generator.StringToHashDiagnostics;

namespace StringToHash.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class StringToHashGenerator : IIncrementalGenerator
{
    #region Constants
    private const string AttributeMetadataName = "StringToHashAttribute";
    #endregion

    #region Types
    private sealed record GenerationResult(ImmutableArray<Diagnostic> Diagnostics, GeneratedSource? Source);

    private sealed record GeneratedSource(string HintName, string Text);
    #endregion

    #region Static
    private static GenerationResult? AnalyzeTarget(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        var targetType = (INamedTypeSymbol)context.TargetSymbol;
        var targetDeclaration = (TypeDeclarationSyntax)context.TargetNode;
        var attribute = context.Attributes[0];

        // 중복 어트리뷰트는 컴파일러가 진단하고 한 타입에 생성 파일을 여러 번 추가하지 않는다.
        var attributeCount = targetType.GetAttributes().Count(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute.AttributeClass)
        );
        if (attributeCount != 1
            || attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax
        )
        {
            return null;
        }

        var declarations = targetDeclaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().ToArray();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        ValidateDeclarations(declarations, targetDeclaration, diagnostics);

        var names = AnalyzeNames(attribute, syntax, targetType, diagnostics, cancellationToken);

        if (diagnostics.Count > 0)
        {
            return new GenerationResult(diagnostics.ToImmutable(), Source: null);
        }

        if (names is null)
        {
            return null;
        }

        var source = new GeneratedSource(
            HashSource.GetHintName(targetType),
            HashSource.Write(targetType.ContainingNamespace, declarations, names.Members)
        );

        return new GenerationResult(ImmutableArray<Diagnostic>.Empty, source);
    }

    private static void ValidateDeclarations(
        TypeDeclarationSyntax[] declarations,
        TypeDeclarationSyntax target,
        ImmutableArray<Diagnostic>.Builder diagnostics
    )
    {
        var location = target.Identifier.GetLocation();
        var name = target.Identifier.ValueText;

        if (target is not ClassDeclarationSyntax
            || !target.Modifiers.Any(SyntaxKind.StaticKeyword)
            || !target.Modifiers.Any(SyntaxKind.PartialKeyword)
        )
        {
            diagnostics.Add(CreateInvalidTarget(location, name));
        }

        if (IsFileLocal(target))
        {
            diagnostics.Add(CreateFileLocalTarget(location, name));
        }

        for (var i = 0; i < declarations.Length - 1; i++)
        {
            var declaration = declarations[i];

            if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || IsFileLocal(declaration))
            {
                diagnostics.Add(CreateInvalidContainingType(
                    location: declaration.Identifier.GetLocation(),
                    typeName: declaration.Identifier.ValueText
                ));
            }
        }
    }

    private static bool IsFileLocal(TypeDeclarationSyntax declaration)
    {
        // Roslyn 4.3에는 file 한정자의 SyntaxKind가 없지만 새 컴파일러에서는 토큰을 확인할 수 있다.
        return declaration.Modifiers.Any(modifier => modifier.Text == "file");
    }

    private static HashNames? AnalyzeNames(
        AttributeData attribute,
        AttributeSyntax syntax,
        INamedTypeSymbol targetType,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (attribute.ConstructorArguments.Length != 1
            || attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array
        )
        {
            return null;
        }

        var names = attribute.ConstructorArguments[0];
        var arguments = syntax.ArgumentList?.Arguments
            .Where(argument => argument.NameEquals is null)
            .Select(argument => argument.Expression)
            .ToArray() ?? [];

        if (names.IsNull || names.Values.IsEmpty)
        {
            var location = arguments.Length == 1 ? arguments[0].GetLocation() : syntax.GetLocation();

            diagnostics.Add(CreateMissingNames(location));

            return null;
        }

        var elements = NameArgument.GetElementExpressions(arguments);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hashNames = new HashNames(targetType);
        var hasBindingError = false;

        for (var i = 0; i < names.Values.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var constant = names.Values[i];

            if (constant.Kind == TypedConstantKind.Error)
            {
                hasBindingError = true;

                continue;
            }

            var argument = new NameArgument(i < elements.Length ? elements[i] : syntax);

            if (constant.Value is not string { Length: > 0 } name)
            {
                diagnostics.Add(CreateMissingName(argument.GetLocation()));

                continue;
            }

            if (!seen.Add(name))
            {
                diagnostics.Add(CreateDuplicateName(argument.GetLocation(), name));

                continue;
            }

            hashNames.Add(argument, name, diagnostics.Add);
        }

        return hasBindingError ? null : hashNames;
    }
    #endregion

    #region IIncrementalGenerator Interface
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: AnalyzeTarget
        );

        context.RegisterSourceOutput(targets, static (production, result) =>
        {
            if (result is null)
            {
                return;
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                production.ReportDiagnostic(diagnostic);
            }

            if (result.Source is { } source)
            {
                production.AddSource(source.HintName, SourceText.From(source.Text, Encoding.UTF8));
            }
        });
    }
    #endregion
}
