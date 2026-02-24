using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PostgREST.DbContext.Provider.Analyzers.Schema;

namespace PostgREST.DbContext.Provider.Analyzers;

/// <summary>
/// Offers a code fix that fetches the PostgREST OpenAPI schema from the URL
/// found in <c>PGREST001</c> / <c>PGREST002</c> diagnostics and lets the user
/// pick which tables to generate via nested code actions.  Each selected table
/// produces a <c>{ClassName}.g.cs</c> entity file (placed next to the DbContext
/// source) and a <c>DbSet&lt;T&gt;</c> property on the DbContext class.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PostgRestSchemaCodeFixProvider))]
[Shared]
public sealed class PostgRestSchemaCodeFixProvider : CodeFixProvider
{
    private const string GeneratedFileSuffix = ".g.cs";

    // Lightweight in-memory cache so repeated light-bulb activations avoid
    // re-fetching the schema every time.
    private static readonly ConcurrentDictionary<string, (DateTime FetchedAt, List<TableDefinition> Tables)>
        SchemaCache = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds
        => [DiagnosticDescriptors.UsePostgRestConstantUrl.Id, DiagnosticDescriptors.SchemaDesignAttributeUrl.Id];

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        if (!diagnostic.Properties.TryGetValue("Url", out var url)
            || string.IsNullOrWhiteSpace(url))
            return;

        // Fetch (or use cached) table definitions so we can present one
        // code-action per table inside a nested menu.
        List<TableDefinition> tables;
        try
        {
            tables = await GetOrFetchSchemaAsync(url!, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or JsonException)
        {
            return; // server unreachable — nothing to offer
        }

        if (tables.Count == 0)
            return;

        // ── Build nested code actions ───────────────────────────────────

        var nestedBuilder = ImmutableArray.CreateBuilder<CodeAction>(tables.Count + 1);

        // "Generate all"
        nestedBuilder.Add(CodeAction.Create(
            title: $"Generate all tables ({tables.Count} tables)",
            createChangedSolution: ct =>
                GenerateSelectedEntitiesAsync(context.Document, diagnostic, url!, tables, ct),
            equivalenceKey: "GeneratePostgRest_All"));

        // One action per table, sorted alphabetically.
        foreach (var table in tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var captured = table;
            nestedBuilder.Add(CodeAction.Create(
                title: EntityCodeGenerator.GetClassName(captured),
                createChangedSolution: ct =>
                    GenerateSelectedEntitiesAsync(
                        context.Document, diagnostic, url!,
                        new List<TableDefinition> { captured }, ct),
                equivalenceKey: $"GeneratePostgRest_{captured.Name}"));
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Generate PostgREST entities from '{url}'",
                nestedActions: nestedBuilder.ToImmutable(),
                isInlinable: false),
            diagnostic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Schema cache
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<List<TableDefinition>> GetOrFetchSchemaAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (SchemaCache.TryGetValue(url, out var entry)
            && DateTime.UtcNow - entry.FetchedAt < CacheTtl)
        {
            return entry.Tables;
        }

        var tables = await PostgRestSchemaFetcher
            .FetchSchemaAsync(url, cancellationToken)
            .ConfigureAwait(false);

        SchemaCache[url] = (DateTime.UtcNow, tables);
        return tables;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Entity file generation + DbSet<T> injection
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<Solution> GenerateSelectedEntitiesAsync(
        Document document,
        Diagnostic diagnostic,
        string url,
        List<TableDefinition> selectedTables,
        CancellationToken cancellationToken)
    {
        try
        {
            var project       = document.Project;
            var rootNamespace = project.DefaultNamespace
                                ?? project.AssemblyName
                                ?? "PostgREST.Generated";

            // Place generated files in the same folder as the DbContext source.
            // Use the DbContext file name as prefix so VS nests them as children.
            var folders  = document.Folders.ToList();
            var solution = project.Solution;
            var contextName = GetFileNameWithoutExtension(document.Name);

            // 1. Generate / update entity .g.cs files ────────────────────

            foreach (var table in selectedTables)
            {
                var className  = EntityCodeGenerator.GetClassName(table);
                var fileName   = contextName + "." + className + GeneratedFileSuffix;
                var code       = EntityCodeGenerator.GenerateSingle(table, rootNamespace);
                var sourceText = SourceText.From(code, Encoding.UTF8);

                // Check for existing doc with new naming convention,
                // then fall back to legacy flat name for migration.
                var legacyName  = className + GeneratedFileSuffix;
                var existingDoc = project.Documents
                    .FirstOrDefault(d => d.Name == fileName
                        && d.Folders.SequenceEqual(folders))
                    ?? project.Documents
                        .FirstOrDefault(d => d.Name == legacyName
                            && d.Folders.SequenceEqual(folders));

                if (existingDoc != null)
                {
                    if (existingDoc.Name != fileName)
                    {
                        // Migrate: remove old flat file and add with new name.
                        solution = solution.RemoveDocument(existingDoc.Id);
                        project  = solution.GetProject(project.Id)!;
                        var docId = DocumentId.CreateNewId(project.Id);
                        solution = solution.AddDocument(docId, fileName, sourceText, folders: folders);
                    }
                    else
                    {
                        solution = solution.WithDocumentText(existingDoc.Id, sourceText);
                    }
                }
                else
                {
                    var docId = DocumentId.CreateNewId(project.Id);
                    solution = solution.AddDocument(docId, fileName, sourceText, folders: folders);
                }

                project = solution.GetProject(project.Id)!;
            }

            // 2. Add DbSet<T> properties to the DbContext class ─────────

            solution = await AddDbSetPropertiesAsync(
                    solution, document.Id, diagnostic,
                    selectedTables, rootNamespace, cancellationToken)
                .ConfigureAwait(false);

            return solution;
        }
        catch (Exception ex) when (
            ex is HttpRequestException
            or TaskCanceledException
            or JsonException)
        {
            return document.Project.Solution;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DbSet<T> property injection
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<Solution> AddDbSetPropertiesAsync(
        Solution solution,
        DocumentId documentId,
        Diagnostic diagnostic,
        List<TableDefinition> tables,
        string rootNamespace,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null)
            return solution;

        var root = await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root == null)
            return solution;

        // Locate the DbContext class from the diagnostic span.
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var classDecl = node.AncestorsAndSelf()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault();
        if (classDecl == null)
            return solution;

        // Collect existing DbSet<T> type-argument names to avoid duplicates.
        var existingDbSets = new HashSet<string>(
            classDecl.Members
                .OfType<PropertyDeclarationSyntax>()
                .Select(p => p.Type)
                .OfType<GenericNameSyntax>()
                .Where(g => g.Identifier.Text == "DbSet"
                            && g.TypeArgumentList.Arguments.Count == 1)
                .Select(g => g.TypeArgumentList.Arguments[0].ToString()));

        // Build the new DbSet<T> properties.
        var newProperties = new List<MemberDeclarationSyntax>();
        foreach (var table in tables)
        {
            var className = EntityCodeGenerator.GetClassName(table);
            if (existingDbSets.Contains(className))
                continue;

            var prop = CreateDbSetProperty(className);
            if (prop != null)
                newProperties.Add(prop);
        }

        if (newProperties.Count == 0)
            return solution;

        // Insert the properties into the class.
        var updatedClass = classDecl.AddMembers(newProperties.ToArray());
        var updatedRoot  = root.ReplaceNode(classDecl, updatedClass);

        // Ensure a using directive for the models namespace is present.
        var modelsNamespace = rootNamespace + ".Models";
        var compilationUnit = (CompilationUnitSyntax)updatedRoot;
        if (!compilationUnit.Usings.Any(u => u.Name?.ToString() == modelsNamespace))
        {
            compilationUnit = compilationUnit.AddUsings(
                SyntaxFactory.UsingDirective(
                    SyntaxFactory.ParseName(modelsNamespace)));
        }

        return solution.WithDocumentSyntaxRoot(documentId, compilationUnit);
    }

    /// <summary>
    /// Creates <c>public DbSet&lt;T&gt; T { get; set; } = null!;</c>
    /// for the given entity class name.
    /// </summary>
    private static MemberDeclarationSyntax? CreateDbSetProperty(string entityClassName)
    {
        var code = $"public DbSet<{entityClassName}> {entityClassName} {{ get; set; }} = null!;";
        return SyntaxFactory.ParseMemberDeclaration(code)?
            .WithLeadingTrivia(
                SyntaxFactory.CarriageReturnLineFeed,
                SyntaxFactory.Whitespace("    "));
    }

    /// <summary>
    /// Returns the file name without its extension (e.g., "AppDbContext" from "AppDbContext.cs").
    /// </summary>
    private static string GetFileNameWithoutExtension(string fileName)
    {
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex > 0 ? fileName.Substring(0, dotIndex) : fileName;
    }
}
