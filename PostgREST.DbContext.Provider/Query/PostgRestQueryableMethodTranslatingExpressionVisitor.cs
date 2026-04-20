using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PosgREST.DbContext.Provider.Core.Query;

/// <summary>
/// Translates LINQ queryable method calls into PostgREST query parameters
/// captured in <see cref="PostgRestQueryExpression"/>.
/// </summary>
/// <remarks>
/// Creates a new instance of the translator.
/// </remarks>
public class PostgRestQueryableMethodTranslatingExpressionVisitor(
    QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
    QueryCompilationContext queryCompilationContext,
    bool subquery)
        : QueryableMethodTranslatingExpressionVisitor(
            dependencies,
            queryCompilationContext,
            subquery)
{

    /// <inheritdoc />
    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
        => new PostgRestQueryableMethodTranslatingExpressionVisitor(
            Dependencies, QueryCompilationContext, subquery: true);

    /// <inheritdoc />
    protected override ShapedQueryExpression CreateShapedQueryExpression(IEntityType entityType)
    {
        var queryExpression = new PostgRestQueryExpression(entityType);

        return new ShapedQueryExpression(
            queryExpression,
            new StructuralTypeShaperExpression(
                entityType,
                new ProjectionBindingExpression(
                    queryExpression, new ProjectionMember(), typeof(ValueBuffer)),
                nullable: false));
    }

    // ──────────────────────────────────────────────
    //  Supported translations
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateWhere(
        ShapedQueryExpression source,
        LambdaExpression predicate)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        if (TryExtractFilters(predicate.Body, predicate.Parameters[0], queryExpression))
            return source;

        AddTranslationErrorDetails("Could not translate predicate to PostgREST filter.");
        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateOrderBy(
        ShapedQueryExpression source,
        LambdaExpression keySelector,
        bool ascending)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        if (TryExtractPropertyName(keySelector.Body, keySelector.Parameters[0], out var propName) && queryExpression.EntityType.GetProperty(propName)?.ColumnName is { } column)
        {
            queryExpression.ClearOrderBy();
            queryExpression.AddOrderBy(new PostgRestOrderByClause(column, ascending));
            return source;
        }

        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateThenBy(
        ShapedQueryExpression source,
        LambdaExpression keySelector,
        bool ascending)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        if (TryExtractPropertyName(keySelector.Body, keySelector.Parameters[0], out var propName) && queryExpression.EntityType.GetProperty(propName)?.ColumnName is { } column)
        {
            queryExpression.AddOrderBy(new PostgRestOrderByClause(column, ascending));
            return source;
        }

        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSkip(
        ShapedQueryExpression source,
        Expression count)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        if (count is ConstantExpression { Value: int offset })
        {
            queryExpression.Offset = offset;
            return source;
        }

        if (count is QueryParameterExpression queryParam)
        {
            queryExpression.OffsetParameterName = queryParam.Name;
            return source;
        }

        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateTake(
        ShapedQueryExpression source,
        Expression count)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        if (count is ConstantExpression { Value: int limit })
        {
            queryExpression.Limit = limit;
            return source;
        }

        if (count is QueryParameterExpression queryParam)
        {
            queryExpression.LimitParameterName = queryParam.Name;
            return source;
        }

        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateFirstOrDefault(
        ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType,
        bool returnDefault)
    {
        if (predicate is not null)
        {
            var filtered = TranslateWhere(source, predicate);
            if (filtered is null)
                return null;
            source = filtered;
        }

        ((PostgRestQueryExpression)source.QueryExpression).Limit = 1;

        return source.UpdateResultCardinality(
            returnDefault ? ResultCardinality.SingleOrDefault : ResultCardinality.Single);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSingleOrDefault(
        ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType,
        bool returnDefault)
    {
        if (predicate is not null)
        {
            var filtered = TranslateWhere(source, predicate);
            if (filtered is null)
                return null;
            source = filtered;
        }

        // Take 2 so EF Core can detect more-than-one violations.
        ((PostgRestQueryExpression)source.QueryExpression).Limit = 2;

        return source.UpdateResultCardinality(
            returnDefault ? ResultCardinality.SingleOrDefault : ResultCardinality.Single);
    }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateLastOrDefault(
        ShapedQueryExpression source,
        LambdaExpression? predicate,
        Type returnType,
        bool returnDefault)
    {
        // PostgREST doesn't support LAST without an explicit ORDER BY reversal.
        return null;
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateCount(
        ShapedQueryExpression source,
        LambdaExpression? predicate)
        => null; // TODO: Implement via Prefer: count=exact header

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateLongCount(
        ShapedQueryExpression source,
        LambdaExpression? predicate)
        => null; // TODO: Implement via Prefer: count=exact header

    // ──────────────────────────────────────────────
    //  Unsupported — return null for client eval
    // ──────────────────────────────────────────────

    /// <summary>
    /// Translates <c>.Select(e =&gt; projection)</c> into PostgREST vertical filtering
    /// (<c>?select=col1,col2</c>) and builds a server-side shaper that directly
    /// constructs the projected type — anonymous types, value tuples, custom DTOs,
    /// scalar members, and arbitrarily nested combinations are all supported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strategy avoids a bespoke <c>ValueBuffer</c> layout for projected types.
    /// Instead the existing entity shaper (<see cref="StructuralTypeShaperExpression"/>)
    /// is inlined into the selector body via
    /// <see cref="ReplacingExpressionVisitor.Replace"/>:
    /// </para>
    /// <code>
    ///   original selector:  e =&gt; new { e.Id, e.Nombre }
    ///   after inlining:     vb =&gt; new { Id   = Materialize(vb).Id,
    ///                                    Nombre = Materialize(vb).Nombre }
    /// </code>
    /// <para>
    /// <c>InjectStructuralTypeMaterializers</c> (base class) then replaces each
    /// <see cref="StructuralTypeShaperExpression"/> occurrence with real materializer
    /// code operating on the <see cref="ValueBuffer"/>.
    /// Entity properties absent from <c>?select=…</c> resolve to <c>null</c>/default
    /// in the <c>ValueBuffer</c>; since they are never accessed by the projection
    /// this is safe.
    /// </para>
    /// </remarks>
    protected override ShapedQueryExpression TranslateSelect(
        ShapedQueryExpression source,
        LambdaExpression selector)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;
        var entityParam = selector.Parameters[0];

        // ── 1. Vertical filtering: collect every e.Prop access in the body ──────
        var referencedColumns = ExtractReferencedColumns(selector.Body, entityParam, queryExpression.EntityType);

        if (referencedColumns.Count > 0)
        {
            // Replace (don't append) — a fresh Select always redefines the projection.
            queryExpression.SelectColumns.Clear();
            foreach (var col in referencedColumns.Distinct())
                queryExpression.SelectColumns.Add(col);
        }

        // ── 2. Build new shaper by inlining the entity shaper into the selector ──
        //
        //   e.g.   e => new { e.Id, e.Nombre }
        //   =>     new { Id     = StructuralTypeShaperExpression(Categoria, vb).Id,
        //                 Nombre = StructuralTypeShaperExpression(Categoria, vb).Nombre }
        //
        // The base class VisitShapedQuery then:
        //   a) ProjectionBindingRemovingVisitor  — vb param replaces ProjectionBinding
        //   b) InjectStructuralTypeMaterializers — StructuralTypeShaperExpression
        //      is replaced by the actual CLR materializer lambda call
        //
        // Supported projection shapes (non-exhaustive):
        //   • anonymous type  : e => new { e.Id, e.Name }
        //   • ValueTuple      : e => (e.Id, e.Name)
        //   • named tuple     : e => (Id: e.Id, Name: e.Name)
        //   • custom DTO ctor : e => new PersonDto(e.Id, e.Name)
        //   • DTO member-init : e => new PersonDto { Id = e.Id, Name = e.Name }
        //   • scalar member   : e => e.Id
        //   • nested shapes   : e => new { e.Id, Sub = new { e.Name, e.Age } }
        var newShaperExpression = ReplacingExpressionVisitor.Replace(
            entityParam,
            source.ShaperExpression,
            selector.Body);

        return source.UpdateShaperExpression(newShaperExpression);
    }

    // ──────────────────────────────────────────────
    //  Column extraction helper
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns all lower-cased column names directly accessed via
    /// <c>entityParam.PropertyName</c> anywhere in <paramref name="body"/>.
    /// Handles arbitrary nesting of <see cref="NewExpression"/>,
    /// <see cref="MemberInitExpression"/>, conditional expressions, etc.
    /// </summary>
    private static IReadOnlyList<string> ExtractReferencedColumns(
        Expression body,
        ParameterExpression entityParam,
        IEntityType entityType)
    {
        var extractor = new ColumnReferenceExtractor(entityParam, entityType);
        extractor.Visit(body);
        return extractor.Columns;
    }

    /// <summary>
    /// Expression visitor that collects the names of entity properties
    /// accessed directly on <see cref="_entityParam"/> (i.e. <c>e.Prop</c>).
    /// Nested projections inside <see cref="NewExpression"/> or
    /// <see cref="MemberInitExpression"/> nodes are also traversed.
    /// Collection navigation properties projected via a nested
    /// <c>.Select(i =&gt; …)</c> are emitted as
    /// <c>relation(col1,col2,…)</c> to match the PostgREST embedded-resource
    /// syntax, supporting arbitrary nesting depth.
    /// </summary>
    private sealed class ColumnReferenceExtractor(ParameterExpression entityParam, IEntityType entityType) : ExpressionVisitor
    {
        private readonly ParameterExpression _entityParam = entityParam;

        /// <summary>Distinct ordered list of accessed column/relation names (lowercase).</summary>
        public List<string> Columns { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            // e.PropertyName → direct column reference
            if (node.Expression == _entityParam)
            {
                var colName = entityType.GetProperty(node.Member.Name).ColumnName;
                if (!Columns.Contains(colName))
                    Columns.Add(colName);

                // Do NOT recurse further — the target IS the entity param.
                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Handle e.Collection.Select(i => projection)
            // Supports both extension-method form (Enumerable.Select) and instance form.
            if (node.Method.Name == nameof(Enumerable.Select))
            {
                Expression? source = null;
                LambdaExpression? lambda = null;

                // Static extension form: Enumerable.Select(source, selector)
                if (node.Arguments.Count == 2)
                {
                    source = node.Arguments[0];

                    while (source is not EntityQueryRootExpression && source is MethodCallExpression { Arguments: [{ } _s, ..] })
                        source = _s;

                    lambda = node.Arguments[1] is UnaryExpression unary
                        ? unary.Operand as LambdaExpression
                        : node.Arguments[1] as LambdaExpression;

                    if (source is EntityQueryRootExpression { EntityType :{ TableName: string sourceName } entityType }
                        && lambda is not null)
                    {
                        var innerParam = lambda.Parameters[0];

                        // Recursively extract columns from the nested projection.
                        // Because ColumnReferenceExtractor handles Select itself,
                        // arbitrarily deep nesting is resolved automatically.
                        var innerExtractor = new ColumnReferenceExtractor(innerParam, entityType);

                        // Capture the visited body so deeper nesting is rewritten and the
                        // resulting lambda remains fully reducible (no dangling sub-trees).
                        var reducedBody = innerExtractor.Visit(lambda.Body);

                        var entry = innerExtractor.Columns.Count > 0
                            ? $"{sourceName}({string.Join(",", innerExtractor.Columns)})"
                            : sourceName;

                        if (!Columns.Contains(entry))
                            Columns.Add(entry);

                        // Rebuild the lambda with the visited (reducible) body so any
                        // further expression rewriting by EF Core can traverse the tree
                        // without encountering unprocessed sub-expressions.
                        var reducedLambda = Expression.Lambda(reducedBody, lambda.Parameters);
                        var newArgs = node.Arguments.ToArray();
                        newArgs[^1] = reducedLambda;
                        return node.Update(node.Object, newArgs);
                    }
                }
            }

            return base.VisitMethodCall(node);
        }
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateAll(
        ShapedQueryExpression source,
        LambdaExpression predicate) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateAny(
        ShapedQueryExpression source,
        LambdaExpression? predicate) => null; // Evaluated client-side via FirstOrDefault + null check

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateAverage(
        ShapedQueryExpression source,
        LambdaExpression? selector,
        Type resultType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateCast(
        ShapedQueryExpression source,
        Type castType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateConcat(
        ShapedQueryExpression source1,
        ShapedQueryExpression source2) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateContains(
        ShapedQueryExpression source, Expression item) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateDefaultIfEmpty(
        ShapedQueryExpression source,
        Expression? defaultValue) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateDistinct(
        ShapedQueryExpression source) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateElementAtOrDefault(
        ShapedQueryExpression source,
        Expression index,
        bool returnDefault) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateExcept(
        ShapedQueryExpression source1,
        ShapedQueryExpression source2) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateGroupBy(
        ShapedQueryExpression source, LambdaExpression keySelector,
        LambdaExpression? elementSelector, LambdaExpression? resultSelector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateGroupJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateIntersect(
        ShapedQueryExpression source1,
        ShapedQueryExpression source2) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateLeftJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateRightJoin(
        ShapedQueryExpression outer,
        ShapedQueryExpression inner,
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        LambdaExpression resultSelector) => null;

    /// <inheritdoc />
    protected override Expression? TranslateExecuteDelete(ShapedQueryExpression source)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        var filtersConstant = Expression.Constant(
            queryExpression.Filters.ToList(),
            typeof(IReadOnlyList<PostgRestFilter>));

        var entitTypeConstant = Expression.Constant(
            queryExpression.EntityType,
            typeof(IEntityType));

        var orFiltersConstant = Expression.Constant(
            queryExpression.OrFilters.ToList(),
            typeof(IReadOnlyList<PostgRestOrFilter>));

        if (QueryCompilationContext.IsAsync)
        {
            var executeAsyncMethod = typeof(PostgRestBulkDeleteExecutor)
                .GetMethod(nameof(PostgRestBulkDeleteExecutor.ExecuteAsync))!;

            return Expression.Call(
                executeAsyncMethod,
                QueryCompilationContext.QueryContextParameter,
                Expression.Constant(queryExpression.TableName),
                filtersConstant,
                orFiltersConstant,
                entitTypeConstant);
        }
        else
        {
            var executeMethod = typeof(PostgRestBulkDeleteExecutor)
                .GetMethod(nameof(PostgRestBulkDeleteExecutor.Execute))!;

            return Expression.Call(
                executeMethod,
                QueryCompilationContext.QueryContextParameter,
                Expression.Constant(queryExpression.TableName),
                filtersConstant,
                orFiltersConstant,
                entitTypeConstant);
        }
    }

    /// <inheritdoc />
    protected override Expression? TranslateExecuteUpdate(
        ShapedQueryExpression source,
        IReadOnlyList<ExecuteUpdateSetter> setters)
    {
        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;

        // ── Resolve each setter to (columnName, propertyName, value/paramName) ────
        var resolvedSetters =
            new List<(string Column, string PropertyName, object? Value, string? ParameterName, bool IsParameter)>(setters.Count);

        foreach (var setter in setters)
        {
            // PropertySelector is a lambda like: e => e.Name
            var selectorBody = setter.PropertySelector.Body;

            if (!TryExtractColumnNameFromType(
                    selectorBody,
                    setter.PropertySelector.Parameters[0],
                    queryExpression.EntityType,
                    out var columnName,
                    out var propertyName))
            {
                AddTranslationErrorDetails(
                    $"ExecuteUpdate: could not resolve property selector '{selectorBody}' to a column name.");
                return null;
            }

            // ValueExpression is the new value — constant or query parameter
            if (!TryExtractValue(
                    setter.ValueExpression,
                    out var value,
                    out var paramName,
                    out var isParam))
            {
                AddTranslationErrorDetails(
                    $"ExecuteUpdate: could not resolve value expression '{setter.ValueExpression}' for column '{columnName}'.");
                return null;
            }

            resolvedSetters.Add((columnName, propertyName, value, paramName, isParam));
        }

        // ── Build the expression tree that will be called at runtime ─────────
        //
        //   PostgRestBulkUpdateExecutor.Execute(
        //       queryContext,
        //       tableName,
        //       filters,
        //       orFilters,
        //       setters)
        //
        var entityTypeConstant = Expression.Constant(queryExpression.EntityType);

        var filtersConstant = Expression.Constant(
            queryExpression.Filters.ToList(),
            typeof(IReadOnlyList<PostgRestFilter>));

        var orFiltersConstant = Expression.Constant(
            queryExpression.OrFilters.ToList(),
            typeof(IReadOnlyList<PostgRestOrFilter>));

        var settersConstant = Expression.Constant(
            resolvedSetters,
            typeof(IReadOnlyList<(string, string, object?, string?, bool)>));

        // EF Core requires the returned expression type to match the execution path:
        //   • ExecuteUpdate      → Expression returning int
        //   • ExecuteUpdateAsync → Expression returning Task<int>
        if (QueryCompilationContext.IsAsync)
        {
            var executeAsyncMethod = typeof(PostgRestBulkUpdateExecutor)
                .GetMethod(nameof(PostgRestBulkUpdateExecutor.ExecuteAsync))!;

            return Expression.Call(
                executeAsyncMethod,
                QueryCompilationContext.QueryContextParameter,
                Expression.Constant(queryExpression.TableName),
                entityTypeConstant,
                filtersConstant,
                orFiltersConstant,
                settersConstant);
        }
        else
        {
            var executeMethod = typeof(PostgRestBulkUpdateExecutor)
                .GetMethod(nameof(PostgRestBulkUpdateExecutor.Execute))!;

            return Expression.Call(
                executeMethod,
                QueryCompilationContext.QueryContextParameter,
                Expression.Constant(queryExpression.TableName),
                entityTypeConstant,
                filtersConstant,
                orFiltersConstant,
                settersConstant);
        }
    }

    /// <summary>
    /// Extracts a PostgREST column name from a property-selector body (e.g. <c>e.Name</c>),
    /// using EF Core metadata to resolve the actual column name.
    /// </summary>
    private static bool TryExtractColumnNameFromType(
        Expression selectorBody,
        ParameterExpression entityParam,
        IEntityType entityType,
        out string columnName,
        out string propertyName)
    {
        columnName = null!;
        propertyName = null!;

        if (selectorBody is MemberExpression { Expression: { } memberTarget } memberExpr
            && memberTarget == entityParam)
        {
            if (entityType.FindProperty(memberExpr.Member.Name) is not { } property)
                return false;

            columnName = property.ColumnName;   // C# 14 extension from PostgRestNamesResolver
            propertyName = property.Name;        // CLR property name — no extension needed
            return true;
        }

        return false;
    }
    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateMax(
        ShapedQueryExpression source,
        LambdaExpression? selector,
        Type resultType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateMin(
        ShapedQueryExpression source,
        LambdaExpression? selector,
        Type resultType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateOfType(
        ShapedQueryExpression source,
        Type resultType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateReverse(
        ShapedQueryExpression source) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSelectMany(
        ShapedQueryExpression source,
        LambdaExpression collectionSelector,
        LambdaExpression resultSelector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSelectMany(
        ShapedQueryExpression source,
        LambdaExpression selector) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSkipWhile(
        ShapedQueryExpression source,
        LambdaExpression predicate) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSum(
        ShapedQueryExpression source,
        LambdaExpression? selector,
        Type resultType) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateTakeWhile(
        ShapedQueryExpression source,
        LambdaExpression predicate) => null;

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateUnion(
        ShapedQueryExpression source1,
        ShapedQueryExpression source2) => null;

    // ──────────────────────────────────────────────
    //  Predicate extraction helpers
    // ──────────────────────────────────────────────

    private static bool TryExtractFilters(
        Expression expression,
        ParameterExpression entityParam,
        PostgRestQueryExpression queryExpression)
    {
        // Handle AND: a && b
        if (expression is BinaryExpression { NodeType: ExpressionType.AndAlso } andExpr)
        {
            return TryExtractFilters(andExpr.Left, entityParam, queryExpression)
                && TryExtractFilters(andExpr.Right, entityParam, queryExpression);
        }

        // Handle OR: a || b → ?or=(cond1,cond2)
        if (expression is BinaryExpression { NodeType: ExpressionType.OrElse } orExpr)
        {
            var branches = new List<PostgRestFilter>();
            if (TryFlattenOrBranches(queryExpression.EntityType, orExpr, entityParam, branches))
            {
                queryExpression.AddOrFilter(new PostgRestOrFilter { Branches = branches });
                return true;
            }
            return false;
        }

        // Handle NOT: !(expr)
        if (expression is UnaryExpression { NodeType: ExpressionType.Not } notExpr)
        {
            return TryExtractNegatedFilter(notExpr.Operand, entityParam, queryExpression);
        }

        // Handle comparison: e.Column op value (including null checks)
        if (expression is BinaryExpression binary)
        {
            // Null equality: e.Col == null → is.null / e.Col != null → not.is.null
            if (TryExtractNullCheck(queryExpression.EntityType, binary, entityParam, out var nullFilter))
            {
                queryExpression.AddFilter(nullFilter);
                return true;
            }

            if (TryExtractBinaryFilter(queryExpression.EntityType, binary, entityParam, out var filter))
            {
                queryExpression.AddFilter(filter);
                return true;
            }
        }

        // Handle method calls: Contains, StartsWith, EndsWith, List.Contains
        if (expression is MethodCallExpression methodCall
            && TryExtractMethodCallFilter(queryExpression.EntityType, methodCall, entityParam, out var mcFilter))
        {
            queryExpression.AddFilter(mcFilter);
            return true;
        }

        // Handle bare boolean member: e.Done → ?done=is.true
        if (expression is MemberExpression member
            && TryExtractPropertyName(member, entityParam, out var propName) && queryExpression.EntityType.GetProperty(propName)?.ColumnName is { } boolCol
            && member.Type == typeof(bool))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
                PropertyName = propName,
                Column = boolCol,
                Operator = PostgRestFilterOperator.Is,
                Value = true
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles <c>!(expr)</c> by extracting the inner filter and negating it.
    /// </summary>
    private static bool TryExtractNegatedFilter(
        Expression operand,
        ParameterExpression entityParam,
        PostgRestQueryExpression queryExpression)
    {
        // !e.Done → ?done=is.false
        if (operand is MemberExpression member
            && TryExtractPropertyName(member, entityParam, out var propName) && queryExpression.EntityType.GetProperty(propName)?.ColumnName is { } column
            && member.Type == typeof(bool))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
                PropertyName = propName,
                Column = column,
                Operator = PostgRestFilterOperator.Is,
                Value = false
            });
            return true;
        }

        // !(e.Col == value) → not.eq.value
        if (operand is BinaryExpression binary
            && TryExtractBinaryFilter(queryExpression.EntityType, binary, entityParam, out var filter))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
                PropertyName = filter.PropertyName,
                Column = filter.Column,
                Operator = filter.Operator,
                Negate = true,
                Value = filter.Value,
                ParameterName = filter.ParameterName,
                IsParameter = filter.IsParameter
            });
            return true;
        }

        // !(e.Name.Contains("x")) → not.like.*x*
        if (operand is MethodCallExpression methodCall
            && TryExtractMethodCallFilter(queryExpression.EntityType, methodCall, entityParam, out var mcFilter))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
                PropertyName = mcFilter.PropertyName,
                Column = mcFilter.Column,
                Operator = mcFilter.Operator,
                Negate = true,
                Value = mcFilter.Value,
                ParameterName = mcFilter.ParameterName,
                IsParameter = mcFilter.IsParameter
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Flattens nested OR expressions into a list of filter branches.
    /// </summary>
    private static bool TryFlattenOrBranches(
        IEntityType entityType,
        Expression expression,
        ParameterExpression entityParam,
        List<PostgRestFilter> branches)
    {
        if (expression is BinaryExpression { NodeType: ExpressionType.OrElse } orExpr)
        {
            return TryFlattenOrBranches(entityType, orExpr.Left, entityParam, branches)
                && TryFlattenOrBranches(entityType, orExpr.Right, entityParam, branches);
        }

        if (expression is BinaryExpression binary
            && TryExtractBinaryFilter(entityType, binary, entityParam, out var filter))
        {
            branches.Add(filter);
            return true;
        }

        if (expression is MethodCallExpression methodCall
            && TryExtractMethodCallFilter(entityType, methodCall, entityParam, out var mcFilter))
        {
            branches.Add(mcFilter);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles <c>e.Col == null</c> → <c>is.null</c> and
    /// <c>e.Col != null</c> → <c>not.is.null</c>.
    /// </summary>
    private static bool TryExtractNullCheck(
        IEntityType entityType,
        BinaryExpression binary,
        ParameterExpression entityParam,
        out PostgRestFilter filter)
    {
        filter = null!;

        if (binary.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
            return false;

        var negate = binary.NodeType == ExpressionType.NotEqual;

        // column == null or column != null
        if (TryExtractPropertyName(binary.Left, entityParam, out var propName) && entityType.GetProperty(propName)?.ColumnName is { } column
            && IsNullConstant(binary.Right))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = column,
                Operator = PostgRestFilterOperator.Is,
                Negate = negate,
                Value = null
            };
            return true;
        }

        // null == column or null != column
        if (TryExtractPropertyName(binary.Right, entityParam, out propName) && (column = entityType.GetProperty(propName)?.ColumnName) is { } 
            && IsNullConstant(binary.Left))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = column,
                Operator = PostgRestFilterOperator.Is,
                Negate = negate,
                Value = null
            };
            return true;
        }

        return false;
    }

    private static bool IsNullConstant(Expression expression)
    {
        return expression is ConstantExpression { Value: null }
            || (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert
                && IsNullConstant(convert.Operand));
    }

    private static bool TryExtractBinaryFilter(
        IEntityType entityType,
        BinaryExpression binary,
        ParameterExpression entityParam,
        out PostgRestFilter filter)
    {
        filter = null!;

        var op = MapBinaryOperator(binary.NodeType);
        if (op is null)
            return false;

        // Try: column op value
        if (TryExtractPropertyName(binary.Left, entityParam, out var propName) && entityType.GetProperty(propName)?.ColumnName is { } column
            && TryExtractValue(binary.Right, out var value, out var paramName, out var isParam))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = column,
                Operator = op.Value,
                Value = value,
                ParameterName = paramName,
                IsParameter = isParam
            };
            return true;
        }

        // Try reversed: value op column → flip operator
        if (TryExtractPropertyName(binary.Right, entityParam, out propName) && (column = entityType.GetProperty(propName)?.ColumnName) is { }
            && TryExtractValue(binary.Left, out value, out paramName, out isParam))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = column,
                Operator = ReverseOperator(op.Value),
                Value = value,
                ParameterName = paramName,
                IsParameter = isParam
            };
            return true;
        }

        return false;
    }

    private static bool TryExtractMethodCallFilter(
        IEntityType entityType,
        MethodCallExpression call,
        ParameterExpression entityParam,
        out PostgRestFilter filter)
    {
        filter = null!;

        if (call.Method.DeclaringType == typeof(string))
        {
            // string.Contains(value) → like.*value*
            if (call.Method.Name == nameof(string.Contains)
                && call.Object is not null
                && TryExtractPropertyName(call.Object, entityParam, out var pName) && entityType.GetProperty(pName)?.ColumnName is { } column
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out var value, out var paramName, out var isParam))
            {
                var likeValue = isParam ? value : $"*{value}*";
                filter = new PostgRestFilter
                {
                    PropertyName = pName,
                    Column = column,
                    Operator = PostgRestFilterOperator.Like,
                    Value = likeValue,
                    ParameterName = paramName,
                    IsParameter = isParam
                };
                return true;
            }

            // string.StartsWith(value) → like.value*
            if (call.Method.Name == nameof(string.StartsWith)
                && call.Object is not null
                && TryExtractPropertyName(call.Object, entityParam, out pName) && (column = entityType.GetProperty(pName)?.ColumnName) is { }
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out value, out paramName, out isParam))
            {
                var likeValue = isParam ? value : $"{value}*";
                filter = new PostgRestFilter
                {
                    PropertyName = pName,
                    Column = column,
                    Operator = PostgRestFilterOperator.Like,
                    Value = likeValue,
                    ParameterName = paramName,
                    IsParameter = isParam
                };
                return true;
            }

            // string.EndsWith(value) → like.*value
            if (call.Method.Name == nameof(string.EndsWith)
                && call.Object is not null
                && TryExtractPropertyName(call.Object, entityParam, out pName) && (column = entityType.GetProperty(pName)?.ColumnName) is { }
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out value, out paramName, out isParam))
            {
                var likeValue = isParam ? value : $"*{value}";
                filter = new PostgRestFilter
                {
                    PropertyName = pName,
                    Column = column,
                    Operator = PostgRestFilterOperator.Like,
                    Value = likeValue,
                    ParameterName = paramName,
                    IsParameter = isParam
                };
                return true;
            }
        }

        // Enumerable.Contains(list, e.Column) — static extension method form
        // e.g. ids.Contains(e.Id) → ?id=in.(1,2,3)
        if (call.Method.Name == nameof(Enumerable.Contains)
            && call.Arguments.Count == 2
            && TryExtractPropertyName(call.Arguments[1], entityParam, out var propName) && entityType.GetProperty(propName)?.ColumnName is { } inColumn
            && TryExtractCollectionValue(call.Arguments[0], out var collectionValue, out var collectionParamName, out var collectionIsParam))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = inColumn,
                Operator = PostgRestFilterOperator.In,
                Value = collectionValue,
                ParameterName = collectionParamName,
                IsParameter = collectionIsParam
            };
            return true;
        }

        // List<T>.Contains(e.Column) — instance method form
        // e.g. list.Contains(e.Id) → ?id=in.(1,2,3)
        if (call.Method.Name == nameof(IList.Contains)
            && call.Arguments.Count == 1
            && call.Object is not null
            && TryExtractPropertyName(call.Arguments[0], entityParam, out propName) && (inColumn = entityType.GetProperty(propName)?.ColumnName) is { } 
            && TryExtractCollectionValue(call.Object, out collectionValue, out collectionParamName, out collectionIsParam))
        {
            filter = new PostgRestFilter
            {
                PropertyName = propName,
                Column = inColumn,
                Operator = PostgRestFilterOperator.In,
                Value = collectionValue,
                ParameterName = collectionParamName,
                IsParameter = collectionIsParam
            };
            return true;
        }

        return false;
    }

    private static bool TryExtractPropertyName(
        Expression expression,
        ParameterExpression entityParam,
        out string columnName)
    {
        columnName = null!;

        if (expression is MemberExpression { Expression: { } memberTarget } memberExpr
            && memberTarget == entityParam)
        {
            columnName = memberExpr.Member.Name;
            return true;
        }

        return false;
    }

    private static bool TryExtractValue(
        Expression expression,
        out object? value,
        out string? paramName,
        out bool isParam)
    {
        value = null;
        paramName = null;
        isParam = false;

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        // QueryParameterExpression (runtime parameter extracted by EF Core)
        if (expression is QueryParameterExpression queryParam)
        {
            paramName = queryParam.Name;
            isParam = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts a collection value for IN filters. Handles both constant
    /// collections and parameterized ones.
    /// </summary>
    private static bool TryExtractCollectionValue(
        Expression expression,
        out object? value,
        out string? paramName,
        out bool isParam)
    {
        value = null;
        paramName = null;
        isParam = false;

        if (expression is QueryParameterExpression queryParam)
        {
            paramName = queryParam.Name;
            isParam = true;
            return true;
        }

        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        return false;
    }

    private static PostgRestFilterOperator? MapBinaryOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => PostgRestFilterOperator.Equal,
        ExpressionType.NotEqual => PostgRestFilterOperator.NotEqual,
        ExpressionType.GreaterThan => PostgRestFilterOperator.GreaterThan,
        ExpressionType.GreaterThanOrEqual => PostgRestFilterOperator.GreaterThanOrEqual,
        ExpressionType.LessThan => PostgRestFilterOperator.LessThan,
        ExpressionType.LessThanOrEqual => PostgRestFilterOperator.LessThanOrEqual,
        _ => null
    };

    private static PostgRestFilterOperator ReverseOperator(PostgRestFilterOperator op) => op switch
    {
        PostgRestFilterOperator.GreaterThan => PostgRestFilterOperator.LessThan,
        PostgRestFilterOperator.GreaterThanOrEqual => PostgRestFilterOperator.LessThanOrEqual,
        PostgRestFilterOperator.LessThan => PostgRestFilterOperator.GreaterThan,
        PostgRestFilterOperator.LessThanOrEqual => PostgRestFilterOperator.GreaterThanOrEqual,
        _ => op
    };
}
