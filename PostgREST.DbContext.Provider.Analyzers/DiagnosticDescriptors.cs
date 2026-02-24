using Microsoft.CodeAnalysis;

namespace PostgREST.DbContext.Provider.Analyzers;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// PGREST001 – Reported on a constant URL argument in <c>UsePostgRest("…")</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor UsePostgRestConstantUrl = new(
        id: "PGREST001",
        title: "PostgREST entity classes can be generated",
        messageFormat: "PostgREST entity classes can be generated from '{0}'",
        category: "PostgREST.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "A constant URL was passed to UsePostgRest. " +
            "A code fix can fetch the PostgREST OpenAPI schema and generate C# entity classes.");

    /// <summary>
    /// PGREST002 – Reported on a <c>[SchemaDesign("…")]</c> attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor SchemaDesignAttributeUrl = new(
        id: "PGREST002",
        title: "PostgREST entity classes can be generated",
        messageFormat: "PostgREST entity classes can be generated from '{0}'",
        category: "PostgREST.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "A [SchemaDesign] attribute specifies a PostgREST URL. " +
            "A code fix can fetch the OpenAPI schema and generate C# entity classes.");
}
