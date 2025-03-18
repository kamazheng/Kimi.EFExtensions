// ***********************************************************************
// Author           : Kama Zheng
// Created          : 01/13/2025
// ***********************************************************************

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace Kimi.EFExtensions;

public static class ModelBuilderExtensions
{
    public static ModelBuilder AppendGlobalQueryFilter<TInterface>(this ModelBuilder modelBuilder, Expression<Func<TInterface, bool>> filter)
    {
        foreach (var item in modelBuilder.Model.GetEntityTypes())
        {
            if (item.BaseType == null && item.ClrType.GetInterface(typeof(TInterface).Name) != null)
            {
                var parameterExpression = Expression.Parameter(item.ClrType);
                var expression = ReplacingExpressionVisitor.Replace(filter.Parameters.Single(), parameterExpression, filter.Body);
                var queryFilter = modelBuilder.Entity(item.ClrType).Metadata.GetQueryFilter();
                if (queryFilter != null)
                {
                    expression = Expression.AndAlso(ReplacingExpressionVisitor.Replace(queryFilter.Parameters.Single(), parameterExpression, queryFilter.Body), expression);
                }

                modelBuilder.Entity(item.ClrType).HasQueryFilter(Expression.Lambda(expression, parameterExpression));
            }
        }

        return modelBuilder;
    }
}

