using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PostgREST.DbContext.Provider.Analyzers.Schema;

/// <summary>
/// Fetches the PostgREST root (<c>GET /</c>) OpenAPI 2.0 specification and
/// parses the <c>definitions</c> section into <see cref="TableDefinition"/> objects.
/// </summary>
internal static class PostgRestSchemaFetcher
{
    /// <summary>
    /// Fetches the OpenAPI spec from <paramref name="baseUrl"/> and returns a
    /// list of table definitions.
    /// </summary>
    public static async Task<List<TableDefinition>> FetchSchemaAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var requestUrl = baseUrl.TrimEnd('/') + "/";

        // Use the overload that accepts a CancellationToken for the request itself.
        using var response = await client
            .GetAsync(requestUrl, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // netstandard2.0 does not have ReadAsStringAsync(CancellationToken).
#pragma warning disable CA2016
        var json = await response.Content
            .ReadAsStringAsync()
            .ConfigureAwait(false);
#pragma warning restore CA2016

        using var doc = JsonDocument.Parse(json);
        return ParseDefinitions(doc.RootElement);
    }

    // ─────────────────────────────────────────────────────────────────────

    private static List<TableDefinition> ParseDefinitions(JsonElement root)
    {
        var tables = new List<TableDefinition>();

        if (!root.TryGetProperty("definitions", out var definitions))
            return tables;

        foreach (var def in definitions.EnumerateObject())
        {
            var table = new TableDefinition { Name = def.Name };

            // Required columns
            if (def.Value.TryGetProperty("required", out var required))
            {
                foreach (var item in required.EnumerateArray())
                {
                    var name = item.GetString();
                    if (name != null)
                        table.RequiredColumns.Add(name);
                }
            }

            // Properties (columns)
            if (def.Value.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var column = ParseColumn(prop);
                    table.Columns.Add(column);
                }
            }

            tables.Add(table);
        }

        return tables;
    }

    private static ColumnDefinition ParseColumn(JsonProperty prop)
    {
        var column = new ColumnDefinition { Name = prop.Name };

        // Handle array wrapper: { "type": "array", "items": { "type": "...", "format": "..." } }
        if (prop.Value.TryGetProperty("type", out var typeEl))
        {
            var typeStr = typeEl.GetString() ?? "string";

            if (typeStr == "array")
            {
                column.IsArray = true;
                if (prop.Value.TryGetProperty("items", out var items))
                {
                    if (items.TryGetProperty("type", out var innerType))
                        column.JsonType = innerType.GetString() ?? "string";
                    if (items.TryGetProperty("format", out var innerFmt))
                        column.Format = innerFmt.GetString();
                }
            }
            else
            {
                column.JsonType = typeStr;
            }
        }

        if (!column.IsArray && prop.Value.TryGetProperty("format", out var format))
            column.Format = format.GetString();

        if (prop.Value.TryGetProperty("description", out var desc))
        {
            var descStr = desc.GetString() ?? string.Empty;
            column.Description = descStr;
            column.IsPrimaryKey = descStr.Contains("<pk/>");
        }

        if (prop.Value.TryGetProperty("default", out var defaultVal))
            column.Default = defaultVal.ToString();

        if (prop.Value.TryGetProperty("maxLength", out var maxLen)
            && maxLen.TryGetInt32(out var maxLenValue))
            column.MaxLength = maxLenValue;

        if (prop.Value.TryGetProperty("enum", out var enumEl))
        {
            var vals = new List<string>();
            foreach (var v in enumEl.EnumerateArray())
            {
                var s = v.GetString();
                if (s != null)
                    vals.Add(s);
            }
            if (vals.Count > 0)
                column.EnumValues = vals.ToArray();
        }

        return column;
    }
}
