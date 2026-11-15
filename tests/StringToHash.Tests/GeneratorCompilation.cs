using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StringToHash.Generator;

namespace StringToHash.Tests;

internal static class GeneratorCompilation
{
    private const string AnimatorStub =
        """
        namespace UnityEngine
        {
            public class Animator
            {
                public static int StringToHash(string name) => name.Length;
            }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        MetadataReference.CreateFromFile(typeof(StringToHashAttribute).Assembly.Location),
    ];

    public static (GeneratorDriverRunResult Result, Compilation Output) Run(
        string source,
        LanguageVersion languageVersion = LanguageVersion.CSharp9
    )
    {
        var compilation = CreateCompilation(source, languageVersion);
        var driver = CreateDriver(languageVersion).RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out _
        );

        return (driver.GetRunResult(), output);
    }

    public static CSharpCompilation CreateCompilation(string source, LanguageVersion languageVersion)
    {
        var options = new CSharpParseOptions(languageVersion);

        return CSharpCompilation.Create(
            assemblyName: "HashExamples",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(source, options, path: "Input.cs"),
                CSharpSyntaxTree.ParseText(AnimatorStub, options, path: "Animator.cs")
            ],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    public static GeneratorDriver CreateDriver(LanguageVersion languageVersion)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new StringToHashGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(languageVersion)
        );
    }
}
