using Investment.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Investment.Core.Extensions
{
    public static class QueryExtensions
    {
        public static IQueryable<T> ApplySoftDeleteFilter<T>(this IQueryable<T> query, bool? isDeleted, bool includeAll = false) where T : class
        {
            if (includeAll)
                return query.IgnoreQueryFilters();

            if (!isDeleted.HasValue)
                return query;

            var param = Expression.Parameter(typeof(T), "x");

            Expression? property = null;

            if (typeof(IBaseEntity).IsAssignableFrom(typeof(T)))
                property = Expression.Property(Expression.Convert(param, typeof(IBaseEntity)), nameof(IBaseEntity.IsDeleted));
            else if (typeof(BaseEntity).IsAssignableFrom(typeof(T)))
                property = Expression.Property(Expression.Convert(param, typeof(BaseEntity)), nameof(BaseEntity.IsDeleted));

            if (property == null)
                return query;

            var condition = Expression.Equal(property, Expression.Constant(isDeleted.Value));

            var lambda = Expression.Lambda<Func<T, bool>>(condition, param);

            return query.IgnoreQueryFilters().Where(lambda);
        }
    }
}
