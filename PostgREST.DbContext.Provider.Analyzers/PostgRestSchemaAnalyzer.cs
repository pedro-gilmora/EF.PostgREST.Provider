using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PostgREST.DbContext.Provider.Analyzers;

/// <summary>
/// Reports informational diagnostics on:
/// <list type="bullet">
///   <item><c>UsePostgRest("constant-url")</c> invocations (<c>PGREST001</c>)</item>
///   <item><c>[SchemaDesign("url")]</c> attribute usages (<c>PGREST002</c>)</item>
/// </list>
/// These diagnostics are consumed by
/// <see cref="PostgRestSchemaCodeFixProvider"/> to offer entity code generation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PostgRestSchemaAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [DiagnosticDescriptors.UsePostgRestConstantUrl, DiagnosticDescriptors.SchemaDesignAttributeUrl];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    // ── UsePostgRest("…") ───────────────────────────────────────────────

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        string? methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            IdentifierNameSyntax identifier     => identifier.Identifier.Text,
            _                                   => null
        };

        if (methodName != "UsePostgRest")
            return;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
            return;

        // The first positional argument must be a string literal.
        var firstArg = arguments[0].Expression;
        if (firstArg is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var url = literal.Token.ValueText;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Verify via semantic model that this is the expected extension method.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (methodSymbol.ContainingType?.Name != "PostgRestDbContextOptionsBuilderExtensions")
            return;

        var props = ImmutableDictionary.CreateBuilder<string, string?>();
        props.Add("Url", url);

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.UsePostgRestConstantUrl,
            firstArg.GetLocation(),
            props.ToImmutable(),
            url));
    }

    // ── [SchemaDesign("…")] ─────────────────────────────────────────────

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        var name = attribute.Name switch
        {
            IdentifierNameSyntax id          => id.Identifier.Text,
            QualifiedNameSyntax qualified     => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias    => alias.Name.Identifier.Text,
            _                                 => null
        };

        if (name is not ("SchemaDesign" or "SchemaDesignAttribute"))
            return;

        var args = attribute.ArgumentList?.Arguments;
        if (args is null || args.Value.Count == 0)
            return;

        var firstArg = args.Value[0].Expression;
        if (firstArg is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var url = literal.Token.ValueText;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // Verify the attribute type via semantic model.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol ctorSymbol
            && ctorSymbol.ContainingType?.Name != "SchemaDesignAttribute")
            return;

        var props = ImmutableDictionary.CreateBuilder<string, string?>();
        props.Add("Url", url);

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.SchemaDesignAttributeUrl,
            attribute.GetLocation(),
            props.ToImmutable(),
            url));
    }
}
