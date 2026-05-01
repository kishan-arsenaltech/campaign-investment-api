using Investment.Core.Entities;

namespace Investment.Core.Extensions
{
    public static class SoftDeleteExtensions
    {
        public static void Restore<TEntity>(this TEntity entity) where TEntity : class
        {
            if (entity == null) return;

            if (entity is IBaseEntity iBase)
            {
                iBase.IsDeleted = false;
                iBase.DeletedAt = null;
                iBase.DeletedBy = null;
            }
            else if (entity is BaseEntity baseEntity)
            {
                baseEntity.IsDeleted = false;
                baseEntity.DeletedAt = null;
                baseEntity.DeletedBy = null;
            }
        }

        public static void RestoreRange<TEntity>(this IEnumerable<TEntity> entities) where TEntity : class
        {
            if (entities == null) return;

            foreach (var entity in entities)
                entity.Restore();
        }
    }
}
