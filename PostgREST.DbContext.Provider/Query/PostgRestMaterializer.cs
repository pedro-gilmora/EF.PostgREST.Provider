using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

using System.Linq.Expressions;
using System.Reflection;

using static System.Linq.Expressions.Expression;

namespace PostgREST.DbContext.Provider.Query;

internal class PostgRestMaterializer
{
#pragma warning disable EF1001 // Internal EF Core API usage.
    readonly static Type _injectableType = typeof(IInjectableService), _entityClrType = typeof(IEntityType), _snapshotType = typeof(ISnapshot);
    public static Expression Build(IEntityType entityType, ParameterExpression instanceParam)
    {
        var classType = entityType.ClrType;
        var objType = typeof(object);
        var queryContextParam = QueryCompilationContext.QueryContextParameter;
        var hasNullKeyVar = Variable(typeof(bool), "hasNullKey1");
        var entryVar = Variable(typeof(InternalEntityEntry), "entry1");
        var shadowSnapshot1 = Variable(_snapshotType, "shadowSnapshot1");

        if (entityType.FindPrimaryKey() is not { } pk) return instanceParam;

        return Block(
            [entryVar, hasNullKeyVar],
            Assign(entryVar,
                Call(queryContextParam, "TryGetEntry", [], Constant(pk), GetPrimaryKeyValues(), Constant(true), hasNullKeyVar)),
            IfThen(Not(hasNullKeyVar),
            Block(
                Condition(NotEqual(entryVar, Default(entryVar.Type)),
                    Assign(instanceParam, Convert(MakeMemberAccess(entryVar, entryVar.Type.GetProperty("Entity")!), classType)),
                    Block([shadowSnapshot1],
                        Assign(shadowSnapshot1, Constant(Snapshot.Empty)),
                        IfThen(TypeIs(instanceParam, _injectableType),
                            Call(
                                Convert(instanceParam, _injectableType),
                                 _injectableType.GetMethod("Injected")!,
                                MakeMemberAccess(queryContextParam, queryContextParam.Type.GetProperty("Context")!),
                                instanceParam,
                                Constant(QueryTrackingBehavior.TrackAll, typeof(QueryTrackingBehavior?)),
                                Constant(entityType))),
                        IfThen(NotEqual(Constant(entityType), Default(_entityClrType)),
                            Assign(entryVar,
                                Call(
                                    queryContextParam,
                                    queryContextParam.Type.GetMethod("StartTracking")!,
                                    Constant(entityType),
                                    instanceParam,
                                    shadowSnapshot1))),
                    instanceParam)),
            instanceParam)),
            instanceParam);

        NewArrayExpression GetPrimaryKeyValues()
        {
            return NewArrayInit(objType,
                pk.Properties.Select(p =>
                {
                    var propValueExpr = MakeMemberAccess(instanceParam, p.PropertyInfo ?? (MemberInfo)p.FieldInfo!);
                    return p.ClrType == objType ? propValueExpr : (Expression)Convert(propValueExpr, objType);
                }));
        }
    }
#pragma warning restore EF1001 // Internal EF Core API usage.
}