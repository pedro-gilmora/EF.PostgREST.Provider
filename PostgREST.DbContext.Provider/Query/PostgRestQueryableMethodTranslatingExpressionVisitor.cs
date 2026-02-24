using System.Collections;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

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

        if (TryExtractColumnName(keySelector.Body, keySelector.Parameters[0], out var column))
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

        if (TryExtractColumnName(keySelector.Body, keySelector.Parameters[0], out var column))
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
        var queryExpression  = (PostgRestQueryExpression)source.QueryExpression;
        var entityParam      = selector.Parameters[0];

        // ── 1. Vertical filtering: collect every e.Prop access in the body ──────
        var referencedColumns = ExtractReferencedColumns(selector.Body, entityParam);

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
        ParameterExpression entityParam)
    {
        var extractor = new ColumnReferenceExtractor(entityParam);
        extractor.Visit(body);
        return extractor.Columns;
    }

    /// <summary>
    /// Expression visitor that collects the names of entity properties
    /// accessed directly on <see cref="_entityParam"/> (i.e. <c>e.Prop</c>).
    /// Nested projections inside <see cref="NewExpression"/> or
    /// <see cref="MemberInitExpression"/> nodes are also traversed.
    /// </summary>
    private sealed class ColumnReferenceExtractor(ParameterExpression entityParam) : ExpressionVisitor
    {
        private readonly ParameterExpression _entityParam = entityParam;

        /// <summary>Distinct ordered list of accessed column names (lowercase).</summary>
        public List<string> Columns { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            // e.PropertyName → direct column reference
            if (node.Expression == _entityParam)
            {
                var colName = node.Member.Name.ToLowerInvariant();
                if (!Columns.Contains(colName))
                    Columns.Add(colName);

                // Do NOT recurse further — the target IS the entity param.
                return node;
            }

            return base.VisitMember(node);
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
            if (TryFlattenOrBranches(orExpr, entityParam, branches))
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
            if (TryExtractNullCheck(binary, entityParam, out var nullFilter))
            {
                queryExpression.AddFilter(nullFilter);
                return true;
            }

            if (TryExtractBinaryFilter(binary, entityParam, out var filter))
            {
                queryExpression.AddFilter(filter);
                return true;
            }
        }

        // Handle method calls: Contains, StartsWith, EndsWith, List.Contains
        if (expression is MethodCallExpression methodCall
            && TryExtractMethodCallFilter(methodCall, entityParam, out var mcFilter))
        {
            queryExpression.AddFilter(mcFilter);
            return true;
        }

        // Handle bare boolean member: e.Done → ?done=is.true
        if (expression is MemberExpression member
            && TryExtractColumnName(member, entityParam, out var boolCol)
            && member.Type == typeof(bool))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
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
            && TryExtractColumnName(member, entityParam, out var boolCol)
            && member.Type == typeof(bool))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
                Column = boolCol,
                Operator = PostgRestFilterOperator.Is,
                Value = false
            });
            return true;
        }

        // !(e.Col == value) → not.eq.value
        if (operand is BinaryExpression binary
            && TryExtractBinaryFilter(binary, entityParam, out var filter))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
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
            && TryExtractMethodCallFilter(methodCall, entityParam, out var mcFilter))
        {
            queryExpression.AddFilter(new PostgRestFilter
            {
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
        Expression expression,
        ParameterExpression entityParam,
        List<PostgRestFilter> branches)
    {
        if (expression is BinaryExpression { NodeType: ExpressionType.OrElse } orExpr)
        {
            return TryFlattenOrBranches(orExpr.Left, entityParam, branches)
                && TryFlattenOrBranches(orExpr.Right, entityParam, branches);
        }

        if (expression is BinaryExpression binary
            && TryExtractBinaryFilter(binary, entityParam, out var filter))
        {
            branches.Add(filter);
            return true;
        }

        if (expression is MethodCallExpression methodCall
            && TryExtractMethodCallFilter(methodCall, entityParam, out var mcFilter))
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
        BinaryExpression binary,
        ParameterExpression entityParam,
        out PostgRestFilter filter)
    {
        filter = null!;

        if (binary.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
            return false;

        var negate = binary.NodeType == ExpressionType.NotEqual;

        // column == null or column != null
        if (TryExtractColumnName(binary.Left, entityParam, out var column)
            && IsNullConstant(binary.Right))
        {
            filter = new PostgRestFilter
            {
                Column = column,
                Operator = PostgRestFilterOperator.Is,
                Negate = negate,
                Value = null
            };
            return true;
        }

        // null == column or null != column
        if (TryExtractColumnName(binary.Right, entityParam, out column)
            && IsNullConstant(binary.Left))
        {
            filter = new PostgRestFilter
            {
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
        BinaryExpression binary,
        ParameterExpression entityParam,
        out PostgRestFilter filter)
    {
        filter = null!;

        var op = MapBinaryOperator(binary.NodeType);
        if (op is null)
            return false;

        // Try: column op value
        if (TryExtractColumnName(binary.Left, entityParam, out var column)
            && TryExtractValue(binary.Right, out var value, out var paramName, out var isParam))
        {
            filter = new PostgRestFilter
            {
                Column = column,
                Operator = op.Value,
                Value = value,
                ParameterName = paramName,
                IsParameter = isParam
            };
            return true;
        }

        // Try reversed: value op column → flip operator
        if (TryExtractColumnName(binary.Right, entityParam, out column)
            && TryExtractValue(binary.Left, out value, out paramName, out isParam))
        {
            filter = new PostgRestFilter
            {
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
                && TryExtractColumnName(call.Object, entityParam, out var column)
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out var value, out var paramName, out var isParam))
            {
                var likeValue = isParam ? value : $"*{value}*";
                filter = new PostgRestFilter
                {
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
                && TryExtractColumnName(call.Object, entityParam, out column)
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out value, out paramName, out isParam))
            {
                var likeValue = isParam ? value : $"{value}*";
                filter = new PostgRestFilter
                {
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
                && TryExtractColumnName(call.Object, entityParam, out column)
                && call.Arguments.Count == 1
                && TryExtractValue(call.Arguments[0], out value, out paramName, out isParam))
            {
                var likeValue = isParam ? value : $"*{value}";
                filter = new PostgRestFilter
                {
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
            && TryExtractColumnName(call.Arguments[1], entityParam, out var inColumn)
            && TryExtractCollectionValue(call.Arguments[0], out var collectionValue, out var collectionParamName, out var collectionIsParam))
        {
            filter = new PostgRestFilter
            {
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
            && TryExtractColumnName(call.Arguments[0], entityParam, out inColumn)
            && TryExtractCollectionValue(call.Object, out collectionValue, out collectionParamName, out collectionIsParam))
        {
            filter = new PostgRestFilter
            {
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

    private static bool TryExtractColumnName(
        Expression expression,
        ParameterExpression entityParam,
        out string columnName)
    {
        columnName = null!;

        if (expression is MemberExpression { Expression: { } memberTarget } memberExpr
            && memberTarget == entityParam)
        {
            columnName = memberExpr.Member.Name.ToLowerInvariant();
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
