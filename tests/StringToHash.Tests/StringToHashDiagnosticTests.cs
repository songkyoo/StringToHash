using Microsoft.CodeAnalysis.CSharp;

namespace StringToHash.Tests;

public class StringToHashDiagnosticTests
{
    [TestCase("""(" Open")""", 2, " ")]
    [TestCase("""("Open ")""", 6, " ")]
    [TestCase("""("Base Layer .Open")""", 12, " ")]
    [TestCase("""("Combat. Ready")""", 9, " ")]
    [TestCase("""("Idle-Run")""", 6, "-")]
    [TestCase("""("2D")""", 2, "2")]
    [TestCase("""("@class")""", 2, "@")]
    [TestCase("""("Open\tDoor")""", 6, """\t""")]
    [TestCase("""("Open\u00A0Door")""", 6, """\u00A0""")]
    [TestCase("""("Open\x09Zone")""", 6, """\x09""")]
    [TestCase("""("Open\U0001F600Door")""", 6, """\U0001F600""")]
    [TestCase("""(@"Idle""Run")""", 7, "\"\"")]
    [TestCase("""(names: new[] { "Idle-Run" })""", 21, "-")]
    public void InvalidCharactersHighlightTheUnusableSourceText(string arguments, int offset, string unusableText)
    {
        var source =
            $$"""
            [StringToHash{{arguments}}]
            public static partial class Hashes { }
            """;
        var (result, _) = GeneratorCompilation.Run(source);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("STH0006"));

        var span = diagnostic.Location.SourceSpan;

        Assert.That(span.Start, Is.EqualTo(source.IndexOf(arguments, StringComparison.Ordinal) + offset));
        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo(unusableText));
        Assert.That(result.GeneratedTrees, Is.Empty);
    }

    [Test]
    public void SingleLineRawStringMapsInvalidCharacterInsideItsDelimiter()
    {
        const string source =
            """"
            [StringToHash("""Idle-Run""")]
            public static partial class Hashes { }
            """";

        var (result, _) = GeneratorCompilation.Run(source, LanguageVersion.Preview);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("STH0006"));

        var span = diagnostic.Location.SourceSpan;

        Assert.That(span.Start, Is.EqualTo(source.IndexOf('-')));
        Assert.That(span.Length, Is.EqualTo(1));
    }

    [Test]
    public void MultiLineRawStringMapsInvalidCharacterPastIndentation()
    {
        const string source =
            """"
            [StringToHash(
                """
                Idle-Run
                """
            )]
            public static partial class Hashes { }
            """";
        var (result, _) = GeneratorCompilation.Run(source, LanguageVersion.Preview);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("STH0006"));

        var span = diagnostic.Location.SourceSpan;

        Assert.That(span.Start, Is.EqualTo(source.IndexOf('-')));
        Assert.That(span.Length, Is.EqualTo(1));
    }

    [Test]
    public void ConstantReferenceReportsTheArgumentWhenTheLiteralIsElsewhere()
    {
        const string source =
            """
            [StringToHash(Hashes.InvalidName)]
            public static partial class Hashes
            {
                internal const string InvalidName = "2D";
            }
            """;
        var (result, _) = GeneratorCompilation.Run(source);
        var diagnostic = result.Diagnostics.Single();
        var span = diagnostic.Location.SourceSpan;

        Assert.That(diagnostic.Id, Is.EqualTo("STH0006"));
        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo("Hashes.InvalidName"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("position 1"));
    }

    [TestCase("()", "STH0003")]
    [TestCase("(names: null)", "STH0003")]
    [TestCase("(new string[] { })", "STH0003")]
    [TestCase("""(null, "Open")""", "STH0004")]
    [TestCase("""("")""", "STH0004")]
    [TestCase("""(".")""", "STH0005")]
    [TestCase("""(".Open")""", "STH0005")]
    [TestCase("""("Combat..Open")""", "STH0005")]
    [TestCase("""("Open.")""", "STH0005")]
    [TestCase("""("Open", "O\u0070en")""", "STH0007")]
    public void MissingAndDuplicateNamesReportAnErrorAndSkipSource(string arguments, string diagnosticId)
    {
        var source =
            $$"""
            [StringToHash{{arguments}}]
            public static partial class Hashes { }
            """;
        var (result, _) = GeneratorCompilation.Run(source);

        Assert.That(result.Diagnostics.Single().Id, Is.EqualTo(diagnosticId));
        Assert.That(result.GeneratedTrees, Is.Empty);
    }

    [TestCase("""("Base Layer.Open", "Base_Layer.Close")""")]
    [TestCase("""("Base Layer", "Base_Layer")""")]
    [TestCase("""("Clo\u200Cse", "Close")""")]
    [TestCase("""("Combat", "Combat.Open")""")]
    [TestCase("""("Combat.Open", "Combat")""")]
    [TestCase("""("Combat.Combat")""")]
    [TestCase("""("Combat.Combat", "Combat")""")]
    [TestCase("""("Hashes.Open")""")]
    [TestCase("""("Existing.More")""")]
    [TestCase("""("T")""")]
    public void GeneratedMembersDiagnoseConflictsWithTheirScope(string arguments)
    {
        var source =
            $$"""
            [StringToHash{{arguments}}]
            public static partial class Hashes<T> { }

            public static partial class Hashes<T>
            {
                private const int Existing = 0;
            }
            """;
        var (result, _) = GeneratorCompilation.Run(source);

        Assert.That(result.Diagnostics.Single().Id, Is.EqualTo("STH0008"));
        Assert.That(result.GeneratedTrees, Is.Empty);
    }

    [TestCase("static class")]
    [TestCase("partial class")]
    public void AttributeRequiresBothModifiersOnItsDeclaration(string declaration)
    {
        var source =
            $$"""
            [StringToHash("Open")]
            public {{declaration}} Hashes { }
            """;
        var (result, _) = GeneratorCompilation.Run(source);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("STH0001"));

        var span = diagnostic.Location.SourceSpan;

        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo("Hashes"));
        Assert.That(result.GeneratedTrees, Is.Empty);
    }

    [Test]
    public void NonPartialContainingTypeReportsItsDeclaration()
    {
        const string source =
            """
            public class Controller
            {
                [StringToHash("Open")]
                private static partial class Hashes { }
            }
            """;
        var (result, _) = GeneratorCompilation.Run(source);
        var diagnostic = result.Diagnostics.Single();

        Assert.That(diagnostic.Id, Is.EqualTo("STH0002"));

        var span = diagnostic.Location.SourceSpan;

        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo("Controller"));
        Assert.That(result.GeneratedTrees, Is.Empty);
    }

    [Test]
    public void InvalidTargetReportsEachInvalidRunAndDoesNotBlockOtherTargets()
    {
        const string source =
            """
            [StringToHash("Idle-Run?", " Open ", "Valid")]
            public static partial class InvalidHashes { }

            [StringToHash("Open")]
            public static partial class ValidHashes { }
            """;
        var (result, _) = GeneratorCompilation.Run(source);
        var diagnosticText = result.Diagnostics.Select(diagnostic =>
        {
            var span = diagnostic.Location.SourceSpan;

            return source.Substring(span.Start, span.Length);
        });

        Assert.That(diagnosticText, Is.EqualTo(new[] { "-", "?", " ", " " }));

        var generatedSource = result.GeneratedTrees.Single().GetText().ToString();

        Assert.That(generatedSource, Does.Contain("partial class ValidHashes"));
    }
}
