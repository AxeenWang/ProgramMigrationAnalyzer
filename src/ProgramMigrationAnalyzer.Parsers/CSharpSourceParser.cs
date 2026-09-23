using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ProgramMigrationAnalyzer.Core;

namespace ProgramMigrationAnalyzer.Parsers;

public sealed class CSharpSourceParser : ISourceParser
{
    public bool CanParse(SourceDocument source) => source.Language switch
    {
        SourceLanguage.CSharp => true,
        SourceLanguage.Informix4Gl => false,
        _ => Path.GetExtension(source.FileName).Equals(".cs", StringComparison.OrdinalIgnoreCase)
    };

    public Task<AnalysisResult> ParseAsync(
        SourceDocument source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tree = CSharpSyntaxTree.ParseText(source.Content, cancellationToken: cancellationToken);
        var root = tree.GetCompilationUnitRoot(cancellationToken);
        var result = new AnalysisResult
        {
            Source = source,
            Language = SourceLanguage.CSharp
        };

        AddProgramUnits(root, tree, result);
        AddFunctions(root, tree, result);
        AddSqlStatements(root, tree, result);
        AddLanguageFacts(root, result);

        var diagnostics = tree.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .ToList();

        foreach (var diagnostic in diagnostics)
        {
            var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
            result.MigrationIssues.Add(new MigrationIssue
            {
                Code = "CS-PARSE",
                Title = "C# syntax requires review",
                Description = diagnostic.GetMessage(),
                Severity = IssueSeverity.Medium,
                SuggestedAction = "Review the malformed or unsupported source syntax before migration.",
                Location = $"Line {line}"
            });
        }

        result.Summary = $"Detected {result.ProgramUnits.Count} type(s), " +
                         $"{result.Functions.Count} function(s), and " +
                         $"{result.SqlStatements.Count} SQL statement(s).";

        return Task.FromResult(result);
    }

    private static void AddProgramUnits(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        AnalysisResult result)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var span = tree.GetLineSpan(declaration.Span);
            var baseTypes = declaration is TypeDeclarationSyntax typeDeclaration
                ? typeDeclaration.BaseList?.Types.Select(item => item.Type.ToString()).ToList() ?? []
                : [];
            var baseType = declaration is ClassDeclarationSyntax or RecordDeclarationSyntax
                ? baseTypes.FirstOrDefault(item => !LooksLikeInterface(item))
                : null;
            var unit = new ProgramUnit
            {
                Name = declaration.Identifier.Text,
                Kind = declaration switch
                {
                    ClassDeclarationSyntax => "Class",
                    InterfaceDeclarationSyntax => "Interface",
                    StructDeclarationSyntax => "Struct",
                    RecordDeclarationSyntax => "Record",
                    EnumDeclarationSyntax => "Enum",
                    _ => "Type"
                },
                Namespace = declaration.Ancestors()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .FirstOrDefault()?.Name.ToString(),
                BaseType = baseType,
                StartLine = span.StartLinePosition.Line + 1,
                EndLine = span.EndLinePosition.Line + 1
            };

            if (declaration is TypeDeclarationSyntax typedDeclaration)
            {
                foreach (var interfaceName in baseTypes.Where(item => item != baseType))
                {
                    unit.ImplementedInterfaces.Add(interfaceName);
                }

                foreach (var member in typedDeclaration.Members)
                {
                    unit.Members.Add(member switch
                    {
                        FieldDeclarationSyntax field => $"Field: {string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text))}",
                        PropertyDeclarationSyntax property => $"Property: {property.Identifier.Text}",
                        ConstructorDeclarationSyntax constructor => $"Constructor: {constructor.Identifier.Text}",
                        MethodDeclarationSyntax method => $"Method: {method.Identifier.Text}",
                        _ => member.Kind().ToString()
                    });
                }
            }

            result.ProgramUnits.Add(unit);
        }
    }

    private static void AddLanguageFacts(CompilationUnitSyntax root, AnalysisResult result)
    {
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString();
            if (name is not null)
            {
                SqlParsing.AddUnique(result.UsingDirectives, name);
            }
        }

        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            SqlParsing.AddUnique(result.ObjectCreations, creation.Type.ToString());
        }
    }

    private static bool LooksLikeInterface(string typeName)
    {
        var shortName = typeName.Split('.').LastOrDefault() ?? typeName;
        return shortName.Length > 1 && shortName[0] == 'I' && char.IsUpper(shortName[1]);
    }

    private static void AddFunctions(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        AnalysisResult result)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var span = tree.GetLineSpan(method.Span);
            var function = new FunctionInfo
            {
                Name = method.Identifier.Text,
                Kind = "Method",
                ReturnType = method.ReturnType.ToString(),
                Summary = $"Method declared on {method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "unknown type"}.",
                StartLine = span.StartLinePosition.Line + 1,
                EndLine = span.EndLinePosition.Line + 1
            };

            AddParameters(function, method.ParameterList.Parameters);
            AddCalls(function, method.DescendantNodes().OfType<InvocationExpressionSyntax>());
            AddReferencedTables(function, method.DescendantNodes().OfType<LiteralExpressionSyntax>());
            result.Functions.Add(function);
        }

        foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            var span = tree.GetLineSpan(constructor.Span);
            var function = new FunctionInfo
            {
                Name = constructor.Identifier.Text,
                Kind = "Constructor",
                ReturnType = constructor.Identifier.Text,
                Summary = "Type constructor.",
                StartLine = span.StartLinePosition.Line + 1,
                EndLine = span.EndLinePosition.Line + 1
            };

            AddParameters(function, constructor.ParameterList.Parameters);
            AddCalls(function, constructor.DescendantNodes().OfType<InvocationExpressionSyntax>());
            result.Functions.Add(function);
        }
    }

    private static void AddParameters(FunctionInfo function, SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        foreach (var parameter in parameters)
        {
            function.Parameters.Add(new ParameterInfo
            {
                Name = parameter.Identifier.Text,
                Type = parameter.Type?.ToString() ?? "object?"
            });
        }
    }

    private static void AddCalls(FunctionInfo function, IEnumerable<InvocationExpressionSyntax> calls)
    {
        foreach (var call in calls)
        {
            var name = call.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => call.Expression.ToString()
            };

            SqlParsing.AddUnique(function.CalledFunctions, name);
        }
    }

    private static void AddReferencedTables(
        FunctionInfo function,
        IEnumerable<LiteralExpressionSyntax> literals)
    {
        foreach (var literal in literals)
        {
            var text = literal.Token.ValueText;
            if (!SqlParsing.LooksLikeSql(text))
            {
                continue;
            }

            var sql = SqlParsing.Create(text, 0);
            foreach (var table in sql.Tables)
            {
                SqlParsing.AddUnique(function.ReferencedTables, table);
            }
        }
    }

    private static void AddSqlStatements(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        AnalysisResult result)
    {
        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            var text = literal.Token.ValueText;
            if (!SqlParsing.LooksLikeSql(text))
            {
                continue;
            }

            var line = tree.GetLineSpan(literal.Span).StartLinePosition.Line + 1;
            result.SqlStatements.Add(SqlParsing.Create(text, line));
        }
    }
}
