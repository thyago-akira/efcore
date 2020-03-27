// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class RelationalSqlTranslatingExpressionVisitor : ExpressionVisitor
    {
        private readonly IModel _model;
        private readonly RelationalSqlTranslatingExpressionVisitorDependencies _dependencies;
        private readonly QueryableMethodTranslatingExpressionVisitor _queryableMethodTranslatingExpressionVisitor;
        private readonly SqlTypeMappingVerifyingExpressionVisitor _sqlTypeMappingVerifyingExpressionVisitor;

        public RelationalSqlTranslatingExpressionVisitor(
            [NotNull] RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
            [NotNull] QueryCompilationContext queryCompilationContext,
            [NotNull] QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
        {
            Check.NotNull(dependencies, nameof(dependencies));
            Check.NotNull(queryCompilationContext, nameof(queryCompilationContext));
            Check.NotNull(queryableMethodTranslatingExpressionVisitor, nameof(queryableMethodTranslatingExpressionVisitor));

            _dependencies = dependencies;
            SqlExpressionFactory = dependencies.SqlExpressionFactory;

            _model = queryCompilationContext.Model;
            _queryableMethodTranslatingExpressionVisitor = queryableMethodTranslatingExpressionVisitor;
            _sqlTypeMappingVerifyingExpressionVisitor = new SqlTypeMappingVerifyingExpressionVisitor();
        }

        protected virtual ISqlExpressionFactory SqlExpressionFactory { get; }

        public virtual SqlExpression Translate([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            var result = Visit(expression);

            if (result is SqlExpression translation)
            {
                if (translation is SqlUnaryExpression sqlUnaryExpression
                    && sqlUnaryExpression.OperatorType == ExpressionType.Convert
                    && sqlUnaryExpression.Type == typeof(object))
                {
                    translation = sqlUnaryExpression.Operand;
                }

                translation = SqlExpressionFactory.ApplyDefaultTypeMapping(translation);

                if (translation.TypeMapping == null)
                {
                    // The return type is not-mappable hence return null
                    return null;
                }

                _sqlTypeMappingVerifyingExpressionVisitor.Visit(translation);

                return translation;
            }

            return null;
        }

        public virtual SqlExpression TranslateAverage([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            if (!(expression is SqlExpression sqlExpression))
            {
                sqlExpression = Translate(expression);
            }

            if (sqlExpression == null)
            {
                throw new InvalidOperationException(CoreStrings.TranslationFailed(expression.Print()));
            }

            var inputType = sqlExpression.Type.UnwrapNullableType();
            if (inputType == typeof(int)
                || inputType == typeof(long))
            {
                sqlExpression = SqlExpressionFactory.ApplyDefaultTypeMapping(
                    SqlExpressionFactory.Convert(sqlExpression, typeof(double)));
            }

            return inputType == typeof(float)
                ? SqlExpressionFactory.Convert(
                    SqlExpressionFactory.Function(
                        "AVG",
                        new[] { sqlExpression },
                        nullable: true,
                        argumentsPropagateNullability: new[] { false },
                        typeof(double)),
                    sqlExpression.Type,
                    sqlExpression.TypeMapping)
                : (SqlExpression)SqlExpressionFactory.Function(
                    "AVG",
                    new[] { sqlExpression },
                    nullable: true,
                    argumentsPropagateNullability: new[] { false },
                    sqlExpression.Type,
                    sqlExpression.TypeMapping);
        }

        public virtual SqlExpression TranslateCount([CanBeNull] Expression expression = null)
        {
            if (expression != null)
            {
                // TODO: Translate Count with predicate for GroupBy
                return null;
            }

            return SqlExpressionFactory.ApplyDefaultTypeMapping(
                SqlExpressionFactory.Function(
                    "COUNT",
                    new[] { SqlExpressionFactory.Fragment("*") },
                    nullable: false,
                    argumentsPropagateNullability: new[] { false },
                    typeof(int)));
        }

        public virtual SqlExpression TranslateLongCount([CanBeNull] Expression expression = null)
        {
            if (expression != null)
            {
                // TODO: Translate Count with predicate for GroupBy
                return null;
            }

            return SqlExpressionFactory.ApplyDefaultTypeMapping(
                SqlExpressionFactory.Function(
                    "COUNT",
                    new[] { SqlExpressionFactory.Fragment("*") },
                    nullable: false,
                    argumentsPropagateNullability: new[] { false },
                    typeof(long)));
        }

        public virtual SqlExpression TranslateMax([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            if (!(expression is SqlExpression sqlExpression))
            {
                sqlExpression = Translate(expression);
            }

            return sqlExpression != null
                ? SqlExpressionFactory.Function(
                    "MAX",
                    new[] { sqlExpression },
                    nullable: true,
                    argumentsPropagateNullability: new[] { false },
                    sqlExpression.Type,
                    sqlExpression.TypeMapping)
                : null;
        }

        public virtual SqlExpression TranslateMin([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            if (!(expression is SqlExpression sqlExpression))
            {
                sqlExpression = Translate(expression);
            }

            return sqlExpression != null
                ? SqlExpressionFactory.Function(
                    "MIN",
                    new[] { sqlExpression },
                    nullable: true,
                    argumentsPropagateNullability: new[] { false },
                    sqlExpression.Type,
                    sqlExpression.TypeMapping)
                : null;
        }

        public virtual SqlExpression TranslateSum([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            if (!(expression is SqlExpression sqlExpression))
            {
                sqlExpression = Translate(expression);
            }

            if (sqlExpression == null)
            {
                throw new InvalidOperationException(CoreStrings.TranslationFailed(expression.Print()));
            }

            var inputType = sqlExpression.Type.UnwrapNullableType();

            return inputType == typeof(float)
                ? SqlExpressionFactory.Convert(
                    SqlExpressionFactory.Function(
                        "SUM",
                        new[] { sqlExpression },
                        nullable: true,
                        argumentsPropagateNullability: new[] { false },
                        typeof(double)),
                    inputType,
                    sqlExpression.TypeMapping)
                : (SqlExpression)SqlExpressionFactory.Function(
                    "SUM",
                    new[] { sqlExpression },
                    nullable: true,
                    argumentsPropagateNullability: new[] { false },
                    inputType,
                    sqlExpression.TypeMapping);
        }

        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            Check.NotNull(binaryExpression, nameof(binaryExpression));

            if (binaryExpression.Left.Type == typeof(AnonymousObject)
                && binaryExpression.NodeType == ExpressionType.Equal)
            {
                return Visit(ConvertAnonymousObjectEqualityComparison(binaryExpression));
            }

            var uncheckedNodeTypeVariant = binaryExpression.NodeType switch
            {
                ExpressionType.AddChecked => ExpressionType.Add,
                ExpressionType.SubtractChecked => ExpressionType.Subtract,
                ExpressionType.MultiplyChecked => ExpressionType.Multiply,
                _ => binaryExpression.NodeType
            };

            var left = TryRemoveImplicitConvert(binaryExpression.Left);
            var right = TryRemoveImplicitConvert(binaryExpression.Right);

            return TranslationFailed(binaryExpression.Left, Visit(left), out var sqlLeft)
                || TranslationFailed(binaryExpression.Right, Visit(right), out var sqlRight)
                ? null
                : uncheckedNodeTypeVariant == ExpressionType.Coalesce
                    ? SqlExpressionFactory.Coalesce(sqlLeft, sqlRight)
                    : (Expression)SqlExpressionFactory.MakeBinary(
                        uncheckedNodeTypeVariant,
                        sqlLeft,
                        sqlRight,
                        null);
        }

        protected override Expression VisitConditional(ConditionalExpression conditionalExpression)
        {
            Check.NotNull(conditionalExpression, nameof(conditionalExpression));

            var test = Visit(conditionalExpression.Test);
            var ifTrue = Visit(conditionalExpression.IfTrue);
            var ifFalse = Visit(conditionalExpression.IfFalse);

            return TranslationFailed(conditionalExpression.Test, test, out var sqlTest)
                || TranslationFailed(conditionalExpression.IfTrue, ifTrue, out var sqlIfTrue)
                || TranslationFailed(conditionalExpression.IfFalse, ifFalse, out var sqlIfFalse)
                ? null
                : SqlExpressionFactory.Case(new[] { new CaseWhenClause(sqlTest, sqlIfTrue) }, sqlIfFalse);
        }

        protected override Expression VisitConstant(ConstantExpression constantExpression)
            => new SqlConstantExpression(Check.NotNull(constantExpression, nameof(constantExpression)), null);

        protected override Expression VisitExtension(Expression extensionExpression)
        {
            Check.NotNull(extensionExpression, nameof(extensionExpression));

            switch (extensionExpression)
            {
                case EntityProjectionExpression _:
                case SqlExpression _:
                    return extensionExpression;

                case EntityShaperExpression entityShaperExpression:
                    return new EntityReferenceExpression(entityShaperExpression);

                case ProjectionBindingExpression projectionBindingExpression:
                    return projectionBindingExpression.ProjectionMember != null
                        ? ((SelectExpression)projectionBindingExpression.QueryExpression)
                            .GetMappedProjection(projectionBindingExpression.ProjectionMember)
                        : null;

                default:
                    return null;
            }
        }

        protected override Expression VisitInvocation(InvocationExpression node) => null;
        protected override Expression VisitLambda<T>(Expression<T> node) => null;
        protected override Expression VisitListInit(ListInitExpression node) => null;

        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            Check.NotNull(memberExpression, nameof(memberExpression));

            var innerExpression = Visit(memberExpression.Expression);

            return TryBindMember(innerExpression, MemberIdentity.Create(memberExpression.Member))
                ?? (TranslationFailed(memberExpression.Expression, base.Visit(memberExpression.Expression), out var sqlInnerExpression)
                    ? null
                    : _dependencies.MemberTranslatorProvider.Translate(sqlInnerExpression, memberExpression.Member, memberExpression.Type));
        }

        protected override Expression VisitMemberInit(MemberInitExpression node) => GetConstantOrNull(Check.NotNull(node, nameof(node)));

        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            Check.NotNull(methodCallExpression, nameof(methodCallExpression));

            // EF.Property case
            if (methodCallExpression.TryGetEFPropertyArguments(out var source, out var propertyName))
            {
                return TryBindMember(Visit(source), MemberIdentity.Create(propertyName))
                    ?? throw new InvalidOperationException(CoreStrings.UnableToTranslateEFPropertyToServer(methodCallExpression.Print()));
            }

            // EF Indexer property
            if (methodCallExpression.TryGetIndexerArguments(_model, out source, out propertyName))
            {
                return TryBindMember(Visit(source), MemberIdentity.Create(propertyName));
            }

            // GroupBy Aggregate case
            if (methodCallExpression.Object == null
                && methodCallExpression.Method.DeclaringType == typeof(Enumerable)
                && methodCallExpression.Arguments.Count > 0
                && methodCallExpression.Arguments[0] is GroupByShaperExpression groupByShaperExpression)
            {
                var translatedAggregate = methodCallExpression.Method.Name switch
                {
                    nameof(Enumerable.Average) => TranslateAverage(GetSelectorOnGrouping(methodCallExpression, groupByShaperExpression)),
                    nameof(Enumerable.Count) => TranslateCount(GetPredicateOnGrouping(methodCallExpression, groupByShaperExpression)),
                    nameof(Enumerable.LongCount) => TranslateLongCount(GetPredicateOnGrouping(methodCallExpression, groupByShaperExpression)),
                    nameof(Enumerable.Max) => TranslateMax(GetSelectorOnGrouping(methodCallExpression, groupByShaperExpression)),
                    nameof(Enumerable.Min) => TranslateMin(GetSelectorOnGrouping(methodCallExpression, groupByShaperExpression)),
                    nameof(Enumerable.Sum) => TranslateSum(GetSelectorOnGrouping(methodCallExpression, groupByShaperExpression)),
                    _ => null
                };

                if (translatedAggregate == null)
                {
                    throw new InvalidOperationException(CoreStrings.TranslationFailed(methodCallExpression.Print()));
                }

                return translatedAggregate;
            }

            // Subquery case
            var subqueryTranslation = _queryableMethodTranslatingExpressionVisitor.TranslateSubquery(methodCallExpression);
            if (subqueryTranslation != null)
            {
                static bool IsAggregateResultWithCustomShaper(MethodInfo method)
                {
                    if (method.IsGenericMethod)
                    {
                        method = method.GetGenericMethodDefinition();
                    }

                    return QueryableMethods.IsAverageWithoutSelector(method)
                        || QueryableMethods.IsAverageWithSelector(method)
                        || method == QueryableMethods.MaxWithoutSelector
                        || method == QueryableMethods.MaxWithSelector
                        || method == QueryableMethods.MinWithoutSelector
                        || method == QueryableMethods.MinWithSelector
                        || QueryableMethods.IsSumWithoutSelector(method)
                        || QueryableMethods.IsSumWithSelector(method);
                }

                if (subqueryTranslation.ResultCardinality == ResultCardinality.Enumerable)
                {
                    return null;
                }

                if (subqueryTranslation.ShaperExpression is EntityShaperExpression entityShaperExpression)
                {
                    return new EntityReferenceExpression(subqueryTranslation);
                }

                if (!(subqueryTranslation.ShaperExpression is ProjectionBindingExpression
                    || IsAggregateResultWithCustomShaper(methodCallExpression.Method)))
                {
                    return null;
                }

                var subquery = (SelectExpression)subqueryTranslation.QueryExpression;
                subquery.ApplyProjection();

#pragma warning disable IDE0046 // Convert to conditional expression
                if (subquery.Tables.Count == 0
#pragma warning restore IDE0046 // Convert to conditional expression
                    && methodCallExpression.Method.IsGenericMethod
                    && methodCallExpression.Method.GetGenericMethodDefinition() is MethodInfo genericMethod
                    && (genericMethod == QueryableMethods.AnyWithoutPredicate
                        || genericMethod == QueryableMethods.AnyWithPredicate
                        || genericMethod == QueryableMethods.All
                        || genericMethod == QueryableMethods.Contains))
                {
                    return subquery.Projection[0].Expression;
                }

                return new ScalarSubqueryExpression(subquery);
            }

            if (TranslationFailed(methodCallExpression.Object, Visit(methodCallExpression.Object), out var sqlObject))
            {
                return null;
            }

            var arguments = new SqlExpression[methodCallExpression.Arguments.Count];
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = methodCallExpression.Arguments[i];
                if (TranslationFailed(argument, Visit(argument), out var sqlArgument))
                {
                    return null;
                }

                arguments[i] = sqlArgument;
            }

            return _dependencies.MethodCallTranslatorProvider.Translate(_model, sqlObject, methodCallExpression.Method, arguments);
        }

        protected override Expression VisitNew(NewExpression node) => GetConstantOrNull(Check.NotNull(node, nameof(node)));

        protected override Expression VisitNewArray(NewArrayExpression node) => null;

        protected override Expression VisitParameter(ParameterExpression parameterExpression)
            => new SqlParameterExpression(Check.NotNull(parameterExpression, nameof(parameterExpression)), null);

        protected override Expression VisitTypeBinary(TypeBinaryExpression typeBinaryExpression)
        {
            Check.NotNull(typeBinaryExpression, nameof(typeBinaryExpression));

            var innerExpression = Visit(typeBinaryExpression.Expression);

            if (typeBinaryExpression.NodeType == ExpressionType.TypeIs
                && innerExpression is EntityReferenceExpression entityReferenceExpression)
            {
                var entityType = entityReferenceExpression.EntityType;
                if (entityType.GetAllBaseTypesInclusive().Any(et => et.ClrType == typeBinaryExpression.TypeOperand))
                {
                    return SqlExpressionFactory.Constant(true);
                }

                var derivedType = entityType.GetDerivedTypes().SingleOrDefault(et => et.ClrType == typeBinaryExpression.TypeOperand);
                if (derivedType != null)
                {
                    var concreteEntityTypes = derivedType.GetConcreteDerivedTypesInclusive().ToList();
                    var discriminatorColumn = BindProperty(entityReferenceExpression, entityType.GetDiscriminatorProperty());

                    return concreteEntityTypes.Count == 1
                        ? SqlExpressionFactory.Equal(
                            discriminatorColumn,
                            SqlExpressionFactory.Constant(concreteEntityTypes[0].GetDiscriminatorValue()))
                        : (Expression)SqlExpressionFactory.In(
                            discriminatorColumn,
                            SqlExpressionFactory.Constant(concreteEntityTypes.Select(et => et.GetDiscriminatorValue()).ToList()),
                            negated: false);
                }

                return SqlExpressionFactory.Constant(false);
            }

            return null;
        }

        protected override Expression VisitUnary(UnaryExpression unaryExpression)
        {
            Check.NotNull(unaryExpression, nameof(unaryExpression));

            var operand = Visit(unaryExpression.Operand);

            if (operand is EntityReferenceExpression entityReferenceExpression
                && (unaryExpression.NodeType == ExpressionType.Convert
                    || unaryExpression.NodeType == ExpressionType.ConvertChecked
                    || unaryExpression.NodeType == ExpressionType.TypeAs))
            {
                return entityReferenceExpression.Convert(unaryExpression.Type);
            }

            if (TranslationFailed(unaryExpression.Operand, operand, out var sqlOperand))
            {
                return null;
            }

            switch (unaryExpression.NodeType)
            {
                case ExpressionType.Not:
                    return SqlExpressionFactory.Not(sqlOperand);

                case ExpressionType.Negate:
                    return SqlExpressionFactory.Negate(sqlOperand);

                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.TypeAs:
                    // Object convert needs to be converted to explicit cast when mismatching types
                    if (operand.Type.IsInterface
                        && unaryExpression.Type.GetInterfaces().Any(e => e == operand.Type)
                        || unaryExpression.Type.UnwrapNullableType() == operand.Type.UnwrapNullableType()
                        || unaryExpression.Type.UnwrapNullableType() == typeof(Enum))
                    {
                        return sqlOperand;
                    }

                    // Introduce explicit cast only if the target type is mapped else we need to client eval
                    if (unaryExpression.Type == typeof(object)
                        || SqlExpressionFactory.FindMapping(unaryExpression.Type) != null)
                    {
                        sqlOperand = SqlExpressionFactory.ApplyDefaultTypeMapping(sqlOperand);

                        return SqlExpressionFactory.Convert(sqlOperand, unaryExpression.Type);
                    }

                    break;

                case ExpressionType.Quote:
                    return operand;
            }

            return null;
        }

        private Expression TryBindMember(Expression source, MemberIdentity member)
        {
            if (!(source is EntityReferenceExpression entityReferenceExpression))
            {
                return null;
            }

            var entityType = entityReferenceExpression.EntityType;
            var property = member.MemberInfo != null
                ? entityType.FindProperty(member.MemberInfo)
                : entityType.FindProperty(member.Name);

            return property != null ? BindProperty(entityReferenceExpression, property) : null;
        }

        private SqlExpression BindProperty(EntityReferenceExpression entityReferenceExpression, IProperty property)
        {
            if (entityReferenceExpression.ParameterEntity != null)
            {
                return ((EntityProjectionExpression)Visit(entityReferenceExpression.ParameterEntity.ValueBufferExpression)).BindProperty(property);
            }

            if (entityReferenceExpression.SubqueryEntity != null)
            {
                var entityShaper = (EntityShaperExpression)entityReferenceExpression.SubqueryEntity.ShaperExpression;
                var innerProjection = ((EntityProjectionExpression)Visit(entityShaper.ValueBufferExpression)).BindProperty(property);
                var subSelectExpression = (SelectExpression)entityReferenceExpression.SubqueryEntity.QueryExpression;
                subSelectExpression.AddToProjection(innerProjection);

                return new ScalarSubqueryExpression(subSelectExpression);
            }

            return null;
        }

        private static Expression GetSelectorOnGrouping(
            MethodCallExpression methodCallExpression, GroupByShaperExpression groupByShaperExpression)
        {
            if (methodCallExpression.Arguments.Count == 1)
            {
                return groupByShaperExpression.ElementSelector;
            }

            if (methodCallExpression.Arguments.Count == 2)
            {
                var selectorLambda = methodCallExpression.Arguments[1].UnwrapLambdaFromQuote();
                return ReplacingExpressionVisitor.Replace(
                    selectorLambda.Parameters[0],
                    groupByShaperExpression.ElementSelector,
                    selectorLambda.Body);
            }

            throw new InvalidOperationException(CoreStrings.TranslationFailed(methodCallExpression.Print()));
        }

        private static Expression GetPredicateOnGrouping(
            MethodCallExpression methodCallExpression, GroupByShaperExpression groupByShaperExpression)
        {
            if (methodCallExpression.Arguments.Count == 1)
            {
                return null;
            }

            if (methodCallExpression.Arguments.Count == 2)
            {
                var selectorLambda = methodCallExpression.Arguments[1].UnwrapLambdaFromQuote();
                return ReplacingExpressionVisitor.Replace(
                    selectorLambda.Parameters[0],
                    groupByShaperExpression.ElementSelector,
                    selectorLambda.Body);
            }

            throw new InvalidOperationException(CoreStrings.TranslationFailed(methodCallExpression.Print()));
        }

        private static Expression TryRemoveImplicitConvert(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
            {
                if (unaryExpression.NodeType == ExpressionType.Convert
                    || unaryExpression.NodeType == ExpressionType.ConvertChecked)
                {
                    var innerType = unaryExpression.Operand.Type.UnwrapNullableType();
                    if (innerType.IsEnum)
                    {
                        innerType = Enum.GetUnderlyingType(innerType);
                    }

                    var convertedType = unaryExpression.Type.UnwrapNullableType();

                    if (innerType == convertedType
                        || (convertedType == typeof(int)
                            && (innerType == typeof(byte)
                                || innerType == typeof(sbyte)
                                || innerType == typeof(char)
                                || innerType == typeof(short)
                                || innerType == typeof(ushort))))
                    {
                        return TryRemoveImplicitConvert(unaryExpression.Operand);
                    }
                }
            }

            return expression;
        }

        private static Expression ConvertAnonymousObjectEqualityComparison(BinaryExpression binaryExpression)
        {
            var leftExpressions = ((NewArrayExpression)((NewExpression)binaryExpression.Left).Arguments[0]).Expressions;
            var rightExpressions = ((NewArrayExpression)((NewExpression)binaryExpression.Right).Arguments[0]).Expressions;

            return leftExpressions.Zip(
                    rightExpressions,
                    (l, r) =>
                    {
                        l = RemoveObjectConvert(l);
                        r = RemoveObjectConvert(r);
                        if (l.Type.IsNullableType())
                        {
                            r = r.Type.IsNullableType() ? r : Expression.Convert(r, l.Type);
                        }
                        else if (r.Type.IsNullableType())
                        {
                            l = l.Type.IsNullableType() ? l : Expression.Convert(l, r.Type);
                        }

                        return Expression.Equal(l, r);
                    })
                .Aggregate((a, b) => Expression.AndAlso(a, b));

            static Expression RemoveObjectConvert(Expression expression)
                => expression is UnaryExpression unaryExpression
                    && expression.Type == typeof(object)
                    && expression.NodeType == ExpressionType.Convert
                    ? unaryExpression.Operand
                    : expression;
        }

        private static SqlConstantExpression GetConstantOrNull(Expression expression)
            => CanEvaluate(expression)
                ? new SqlConstantExpression(
                    Expression.Constant(
                        Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object))).Compile().Invoke(),
                        expression.Type),
                    null)
                : null;

        private static bool CanEvaluate(Expression expression)
        {
#pragma warning disable IDE0066 // Convert switch statement to expression
            switch (expression)
#pragma warning restore IDE0066 // Convert switch statement to expression
            {
                case ConstantExpression constantExpression:
                    return true;

                case NewExpression newExpression:
                    return newExpression.Arguments.All(e => CanEvaluate(e));

                case MemberInitExpression memberInitExpression:
                    return CanEvaluate(memberInitExpression.NewExpression)
                        && memberInitExpression.Bindings.All(
                            mb => mb is MemberAssignment memberAssignment && CanEvaluate(memberAssignment.Expression));

                default:
                    return false;
            }
        }

        [DebuggerStepThrough]
        private static bool TranslationFailed(Expression original, Expression translation, out SqlExpression castTranslation)
        {
            if (original != null
                && !(translation is SqlExpression))
            {
                castTranslation = null;
                return true;
            }

            castTranslation = translation as SqlExpression;
            return false;
        }

        private sealed class EntityReferenceExpression : Expression
        {
            public EntityReferenceExpression(EntityShaperExpression parameter)
            {
                ParameterEntity = parameter;
                EntityType = parameter.EntityType;
            }

            public EntityReferenceExpression(ShapedQueryExpression subquery)
            {
                SubqueryEntity = subquery;
                EntityType = ((EntityShaperExpression)subquery.ShaperExpression).EntityType;
            }

            private EntityReferenceExpression(EntityReferenceExpression entityReferenceExpression, IEntityType entityType)
            {
                ParameterEntity = entityReferenceExpression.ParameterEntity;
                SubqueryEntity = entityReferenceExpression.SubqueryEntity;
                EntityType = entityType;
            }

            public EntityShaperExpression ParameterEntity { get; }
            public ShapedQueryExpression SubqueryEntity { get; }
            public IEntityType EntityType { get; }

            public override Type Type => EntityType.ClrType;
            public override ExpressionType NodeType => ExpressionType.Extension;

            public Expression Convert(Type type)
            {
                if (type == typeof(object) // Ignore object conversion
                    || type.IsAssignableFrom(Type)) // Ignore casting to base type/interface
                {
                    return this;
                }

                var derivedEntityType = EntityType.GetDerivedTypes().FirstOrDefault(et => et.ClrType == type);

                return derivedEntityType == null ? null : new EntityReferenceExpression(this, derivedEntityType);
            }
        }

        private sealed class SqlTypeMappingVerifyingExpressionVisitor : ExpressionVisitor
        {
            protected override Expression VisitExtension(Expression node)
            {
                Check.NotNull(node, nameof(node));

                if (node is SqlExpression sqlExpression
                    && !(node is SqlFragmentExpression)
                    && !(node is SqlFunctionExpression sqlFunctionExpression
                        && sqlFunctionExpression.Type.IsQueryableType()))
                {
                    if (sqlExpression.TypeMapping == null)
                    {
                        throw new InvalidOperationException(CoreStrings.NullTypeMappingInSqlTree);
                    }
                }

                return base.VisitExtension(node);
            }
        }
    }
}
