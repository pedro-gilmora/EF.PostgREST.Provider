using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.FileIO;

using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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
                type: entityType,
                valueBufferExpression: new ProjectionBindingExpression(
                    queryExpression,
                    new ProjectionMember(),
                    typeof(ValueBuffer)),
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

        return source.UpdateResultCardinality(returnDefault ? ResultCardinality.SingleOrDefault : ResultCardinality.Single);
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

    record StackState(
        ColumnsTree Columns,
        ParameterExpression Parameter,
        IEntityType CurrentTargetEntitytType);

    readonly static MethodInfo
        _select = typeof(Enumerable).GetMethods().FirstOrDefault(m => m.Name == "Select" && m.GetParameters().Length == 2)!;

    StackState? stackState;

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
        var oldStackState = stackState;

        var queryExpression = (PostgRestQueryExpression)source.QueryExpression;
        stackState = new([],
                         selector.Parameters[0],
                         queryExpression.EntityType);

        // ── 1. Handle IncludeExpression ──────────────────────────────────────
        //
        // Each .Include(e => e.Nav) call arrives as a separate TranslateSelect
        // invocation whose selector.Body is a single IncludeExpression:
        //
        //   IncludeExpression(Ventas)          ← selector.Body
        //     └─ EntityExpression: p           ← the entity parameter
        //
        // Multiple .Include() calls therefore produce multiple TranslateSelect
        // calls in sequence — they are NOT nested.  We must NOT clear
        // SelectColumns here; we only add the new navigation node so that
        // columns accumulated by earlier Include calls are preserved.
        //
        // ThenInclude IS nested: Include(Include(p, Nav1), Nav2).
        // The while-loop handles that by walking inward until the bare entity
        // parameter is reached.
        //Expression newShaperExpression;
        Expression bodyToVisit = selector.Body;

        if (bodyToVisit is IncludeExpression include)
        {
            // ── Include / ThenInclude path ───────────────────────────────────
            // Walk the (potentially nested ThenInclude) chain.

            TryRegisterInclude(include, stackState.Columns);

        }
        else
        {
            var parameter = selector.Parameters[0];
            queryExpression.Projector = (LambdaExpression)new SelectMappingsCollector(parameter, source, stackState.CurrentTargetEntitytType, stackState.Columns, 0).Visit(selector);
            queryExpression._type = selector.ReturnType;

            queryExpression.SelectColumns.Clear();

        }

        foreach (var item in stackState.Columns) queryExpression.SelectColumns.Add(item);

        stackState = oldStackState;

        return source/*.UpdateShaperExpression(newShaperExpression)*/;

        static void CheckInnerInclude(Expression navExpression, ColumnsTree parentColumn)
        {
            if (navExpression is MaterializeCollectionNavigationExpression {
                    Subquery: MethodCallExpression {
                        Arguments: [_, UnaryExpression {
                            Operand: LambdaExpression {
                                Body: IncludeExpression include
                            }
                        }]
                    }
                })
            {
                TryRegisterInclude(include, parentColumn);
            }
        }

        static void TryRegisterInclude(IncludeExpression include, ColumnsTree parentColumn)
        {
            if (include is
                {
                    EntityExpression: var expression,
                    Navigation: { TargetEntityType: { TableName: string navName } entityType, IsCollection: var isCollection, Name: var memberName } outerNav,
                    NavigationExpression: var navExpression
                })
            {
                if (expression is IncludeExpression prevInclude)
                    TryRegisterInclude(prevInclude, parentColumn);

                var targetMember = outerNav.GetMemberInfo(true, true);

                var (clrType, getValue, setValue) = targetMember switch
                {
                    PropertyInfo { CanWrite: true } p =>
                        (p.PropertyType,
                         p.GetValue,
                         p.CanWrite ? p.SetValue : null),

                    FieldInfo f when !(f.IsStatic || f.IsLiteral || f.IsInitOnly) =>
                        (f.FieldType,
                         (Func<object, object?>?)f.GetValue,
                         (Action<object, object?>?)f.SetValue),

                    _ => throw new MissingMemberException($"Member for {outerNav} not found")
                };

                ColumnsTree col = new(navName, isRelation: true)
                {
                    GetValue = getValue,
                    SetValue = setValue,
                    ClrType = entityType.ClrType,
                    CollectionType = clrType,
                    TargetEntity = entityType,
                    IsCollection = isCollection
                };

                parentColumn.Add(col);

                CheckInnerInclude(navExpression, col);
            }
        }
    }

    class SelectMappingsCollector(ParameterExpression parameter, ShapedQueryExpression expression, IEntityType entityType, ColumnsTree selectColumns, int deepCount) : ExpressionVisitor
    {
        //IPropertyBase? currentSourceMember;

        //internal ParameterExpression ParamSource = Expression.Parameter(entityType.ClrType, "valueBuffer" + (deepCount == 0 ? "" : deepCount));

        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            if (node is { NewExpression: var newExpr, Bindings: { Count: > 0 } bindings })
            {
                List<MemberBinding> newBindings = new(bindings.Count);
                foreach (var item in bindings)
                {
                    if (item is MemberAssignment assignment)
                    {
                        var right = Visit(assignment.Expression);
                        newBindings.Add(Expression.Bind(item.Member, right));
                    }
                }
                return Expression.MemberInit(newExpr, newBindings);
            }
            return base.VisitMemberInit(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == parameter)
            {
                if (entityType.FindMember(node.Member.Name) is { IsCollection: var isCollection, ClrType: Type originalClrType } prop)
                {
                    var targetMember = prop.GetMemberInfo(true, true);

                    var (clrType, getValue, setValue) = targetMember switch
                    {
                        PropertyInfo p => (p.PropertyType, p.GetValue, p.CanWrite ? p.SetValue : null),
                        FieldInfo f when !(f.IsStatic || f.IsLiteral || f.IsInitOnly) => (f.FieldType, (Func<object, object?>?)f.GetValue, (Action<object, object?>?)f.SetValue),
                        _ => throw new MissingMemberException($"Member for {prop} not found")
                    };

                    selectColumns.Add(new ColumnsTree(prop.ColumnName)
                    {
                        GetValue = getValue,
                        SetValue = setValue,
                        ClrType = clrType,
                        TargetEntity = entityType,
                        IsCollection = isCollection
                    });
                }
            }

            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "Select"
                && node.Arguments is [MethodCallExpression { Arguments: [EntityQueryRootExpression { EntityType: var type } entity, _], } e, UnaryExpression { Operand: LambdaExpression { Parameters: [{ } _param], Body: var body } lambda }]
                && entityType.GetNavigations().FirstOrDefault(n => n.IsCollection && n.TargetEntityType == type) is { } prop)
            {
                var propMemberInfo = prop.GetMemberInfo(false, true);

                var (clrType, getValue, setValue) = propMemberInfo switch
                {
                    PropertyInfo { CanWrite: true } p => (p.PropertyType, p.GetValue, p.CanWrite ? p.SetValue : null),
                    FieldInfo f when !(f.IsStatic || f.IsLiteral || f.IsInitOnly) => (f.FieldType, (Func<object, object?>?)f.GetValue, (Action<object, object?>?)f.SetValue),
                    _ => throw new MissingMemberException($"Proper assignable member for {prop} not found")
                };

                ColumnsTree column = new(prop.ColumnName, isRelation: true)
                {
                    GetValue = getValue,
                    SetValue = setValue,
                    CollectionType = clrType,
                    ClrType = type.ClrType,
                    TargetEntity = prop.DeclaringEntityType,
                    IsCollection = prop.IsCollection
                };

                selectColumns.Add(column);

                var method = _select.MakeGenericMethod(type.ClrType, node.Method.GetGenericArguments()[1]);

                SelectMappingsCollector innerSelectCollector = new(_param, expression, type, column, deepCount + 1);

                var newBody = innerSelectCollector.Visit(body);

                var navAccess = Expression.MakeMemberAccess(parameter, propMemberInfo);
                var newLambda = Expression.Lambda(newBody, _param);
                return Expression.Call(null, method, navAccess, newLambda);
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
            new List<SetterInfo>(setters.Count);

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
            typeof(IReadOnlyList<SetterInfo>));

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
    /// collections and parameterized ones, as well as closure-captured local
    /// variables that EF Core exposes as <see cref="MemberExpression"/> nodes.
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

        // Closure-captured local variable: evaluate the expression at translation time.
        if (expression is MemberExpression or UnaryExpression { NodeType: ExpressionType.Convert })
        {
            try
            {
                value = Expression.Lambda(expression).Compile().DynamicInvoke();
                return true;
            }
            catch
            {
                return false;
            }
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
