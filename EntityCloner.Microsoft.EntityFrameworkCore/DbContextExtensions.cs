using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using EntityCloner.Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityCloner.Microsoft.EntityFrameworkCore
{
    public static class DbContextExtensions
    {
        public static async Task<IList<TEntity>> CloneAsync<TEntity>(this DbContext source, IQueryable<TEntity> queryable,
             Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
            where TEntity : class
        {
            var entities = await queryable.AsNoTracking().ToListAsync();

            var clonedEntities = source.InternalCloneCollection(new Dictionary<object, object>(), typeof(TEntity), null, null, entities);

            return clonedEntities.Cast<TEntity>().ToList();
        }

        public static async Task<TEntity> CloneAsync<TEntity>(this DbContext source, TEntity entityOrListOfEntities,
             Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
            where TEntity : class
        {
            IReadOnlyEntityType entityType;
            if ((typeof(TEntity).IsGenericType && typeof(TEntity).GetGenericTypeDefinition() == typeof(IEnumerable<>)) ||
                (typeof(TEntity).GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))))
            {
                var entityClrType = (typeof(TEntity).HasElementType ? typeof(TEntity).GetElementType() : typeof(TEntity).GenericTypeArguments[0]) ?? typeof(TEntity);

                entityType = source.FindCurrentEntityType(entityClrType, null, null);
                if (entityType == null)
                {
                    throw new ArgumentException("Argument should be a known entity of the DbContext", nameof(entityOrListOfEntities));
                }

                var clonedEntities = source.InternalCloneCollection(new Dictionary<object, object>(), entityClrType, null, null, (IEnumerable)entityOrListOfEntities, itemActionFilter, itemAction);

                return (TEntity)clonedEntities;
            }

            entityType = source.FindCurrentEntityType(typeof(TEntity), null, null);

            if (entityType == null)
            {
                throw new ArgumentException("Argument should be a known entity of the DbContext", nameof(entityOrListOfEntities));
            }

            var clonedEntity = (TEntity)source.InternalClone(entityOrListOfEntities, null, null, new Dictionary<object, object>(),
                itemActionFilter, itemAction);

            return await Task.FromResult(clonedEntity);
        }

        public static Task<TEntity> CloneAsync<TEntity>(this DbContext source, params object[] primaryKey)
            where TEntity : class
        {
            return source.CloneAsync<TEntity>(includeQuery => includeQuery, primaryKey);
        }

        public static async Task<TEntity> CloneAsync<TEntity>(this DbContext source, Func<IClonableQueryable<TEntity>, IClonableQueryable<TEntity>> includeQuery, params object[] primaryKey)
            where TEntity : class
        {
            var entityType = source.FindCurrentEntityType(typeof(TEntity), null, null);
            var primaryKeyProperties = entityType?.FindPrimaryKey()?.Properties.Select(p => p.PropertyInfo).ToList();
            if (primaryKeyProperties == null)
            {
                throw new NotSupportedException("CloneAsync only can handle types with PrimaryKey configuration'");
            }

            var primaryKeyExpression = CreatePrimaryKeyExpression<TEntity>(primaryKey, primaryKeyProperties);

            var query = source.Set<TEntity>().AsNoTracking().Where(primaryKeyExpression);

            var clonableQueryable = new ClonableQueryable<TEntity>(query);

            clonableQueryable = (ClonableQueryable<TEntity>)includeQuery(clonableQueryable);

            var entity = await clonableQueryable.Queryable.SingleAsync();

            var clonedEntity = (TEntity)source.InternalClone(entity, null, null, new Dictionary<object, object>());

            return clonedEntity;
        }

        /// <summary>
        /// this method name variation is caused by
        /// method selection errors under .net 8
        /// whereby CloneAsync<TEntity>(this DbContext source, params object[] primaryKey)
        /// greedy matches, potentially with uncleaned projects
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <param name="source"></param>
        /// <param name="includeQuery"></param>
        /// <param name="itemActionFilter"></param>
        /// <param name="itemAction"></param>
        /// <param name="primaryKey"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static async Task<TEntity> CloneWithGraphVisitorAsync<TEntity>(this DbContext source, Func<IClonableQueryable<TEntity>, 
            IClonableQueryable<TEntity>> includeQuery,
            Func<TypeInfo, bool>? itemActionFilter, Func<object, object>? itemAction, params object[] primaryKey)
        where TEntity : class
        {
            var entityType = source.FindCurrentEntityType(typeof(TEntity), null, null);
            var primaryKeyProperties = entityType?.FindPrimaryKey()?.Properties.Select(p => p.PropertyInfo).ToList();
            if (primaryKeyProperties == null)
            {
                throw new NotSupportedException("CloneAsync only can handle types with PrimaryKey configuration'");
            }

            var primaryKeyExpression = CreatePrimaryKeyExpression<TEntity>(primaryKey, primaryKeyProperties);

            var query = source.Set<TEntity>().AsNoTracking().Where(primaryKeyExpression);

            var clonableQueryable = new ClonableQueryable<TEntity>(query);

            clonableQueryable = (ClonableQueryable<TEntity>)includeQuery(clonableQueryable);

            var entity = await clonableQueryable.Queryable.SingleAsync();

            var clonedEntity = (TEntity)source.InternalClone(entity, null, null, new Dictionary<object, object>(),
                itemActionFilter, itemAction);

            return clonedEntity;
        }


        private static object ConvertToCollectionType(Type collectionType, Type entityType, ICollection collectionValue)
        {
            if (collectionType.IsInterface)
            {
                var list = Activator.CreateInstance(typeof(List<>).MakeGenericType(entityType), collectionValue);
                return list;
            }

            if (collectionType.IsArray)
            {
                var array = Array.CreateInstance(entityType, collectionValue.Count);
                var index = 0;
                foreach (var item in collectionValue)
                {
                    array.SetValue(item, index);
                    index++;
                }

                return array;
            }

            return Activator.CreateInstance(collectionType, collectionValue);
        }

        private static Expression<Func<TEntity, bool>> CreatePrimaryKeyExpression<TEntity>(object[] primaryKey, List<PropertyInfo> primaryKeyProperties)
            where TEntity : class
        {
            Expression<Func<TEntity, bool>> primaryKeyExpression = e => true;

            for (int i = 0; i < primaryKeyProperties.Count; i++)
            {
                var primaryKeyProperty = primaryKeyProperties[i];
                var idPart = primaryKey[i];

                if (idPart?.GetType() != primaryKeyProperty.PropertyType)
                {
                    throw new NotSupportedException(
                        $"CloneAsync only can handle id of type '{primaryKeyProperty.PropertyType.FullName}', passed id of type '{idPart?.GetType().FullName}'");
                }

                var idPartPredicate = BuildPredicate<TEntity>(primaryKeyProperty.Name, ExpressionType.Equal, idPart);
                primaryKeyExpression = AndAlso(primaryKeyExpression, idPartPredicate);
            }

            return primaryKeyExpression;
        }

        private static Expression<Func<T, bool>> BuildPredicate<T>(string propertyName, ExpressionType comparison, object value)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var left = propertyName.Split('.').Aggregate((Expression)parameter, Expression.Property);
            var body = MakeBinary(comparison, left, value);
            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }

        private static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
        {
            // need to detect whether they use the same
            // parameter instance; if not, they need fixing
            ParameterExpression param = expr1.Parameters[0];
            if (ReferenceEquals(param, expr2.Parameters[0]))
            {
                // simple version
                return Expression.Lambda<Func<T, bool>>(
                    Expression.AndAlso(expr1.Body, expr2.Body), param);
            }
            // otherwise, keep expr1 "as is" and invoke expr2
            return Expression.Lambda<Func<T, bool>>(
                Expression.AndAlso(
                    expr1.Body,
                    Expression.Invoke(expr2, param)), param);
        }

        private static Expression MakeBinary(ExpressionType type, Expression left, object value)
        {
            var right = Expression.Constant(value, left.Type);
            return Expression.MakeBinary(type, left, right);
        }

        private static object InternalClone(this DbContext source, object entity, string definingNavigationName, 
            IReadOnlyEntityType definingEntityType, Dictionary<object, object> references, 
            Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
        {
            if (references.ContainsKey(entity))
            {
                return references[entity];
            }

            JsonSerializerOptions jsonSerializerOptions = new()
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                WriteIndented = true
            };
            string jsonString = JsonSerializer.Serialize(entity, jsonSerializerOptions);
            object clonedEntity = JsonSerializer.Deserialize(jsonString, entity.GetType(), jsonSerializerOptions);


            references.Add(entity, clonedEntity);
            // source.CloneOwnedEntityProperties(entity, definingNavigationName, definingEntityType, references, clonedEntity);

            source.ResetEntityProperties(entity, definingNavigationName, definingEntityType, clonedEntity, itemActionFilter, itemAction);

            source.ResetNavigationProperties(entity, definingNavigationName, definingEntityType, references, clonedEntity, itemActionFilter, itemAction);

            return clonedEntity;
        }

        //private static void CloneOwnedEntityProperties(this DbContext source, object entity, string definingNavigationName, IEntityType definingEntityType, Dictionary<object, object> references, object clonedEntity)
        //{
        //    foreach (var navigation in source.FindCurrentEntityType(entity.GetType(), definingNavigationName, definingEntityType).GetNavigations())
        //    {
        //        var navigationValue = navigation.PropertyInfo.GetValue(entity);

        //        if(navigation.ForeignKey.DeclaringEntityType.DefiningEntityType != null && navigation.ForeignKey.DeclaringEntityType.DefiningNavigationName != null)
        //        {
        //            navigation.PropertyInfo.SetValue(clonedEntity, navigationValue);
        //        }            
        //    }
        //}

        private static void ResetNavigationProperties(this DbContext source, object entity, string definingNavigationName, 
            IReadOnlyEntityType definingEntityType, Dictionary<object, object> references, object clonedEntity,
            Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
        {
            var entityType = source.FindCurrentEntityType(entity.GetType(), definingNavigationName, definingEntityType);
            if (entityType != null)
            {
                foreach (var navigation in entityType.GetNavigations())
                {
                    ResetNavigationProperty(source, entity, references, clonedEntity, navigation, itemActionFilter, itemAction);
                }

                IEnumerable<IReadOnlySkipNavigation> skipNavigations = entityType.GetSkipNavigations();
                foreach (var navigation in skipNavigations)
                {
                    ResetSkipNavigationProperty(source, entity, references, clonedEntity, navigation, itemActionFilter, itemAction);
                }
            }
        }

        private static void ResetNavigationProperty<TNavigation>(DbContext source, object entity, Dictionary<object, object> references, object clonedEntity, TNavigation navigation, Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
            where TNavigation : IReadOnlyNavigation
        {
            var navigationValue = navigation.PropertyInfo?.GetValue(entity);

            if (navigation.IsOnDependent && navigationValue != null)
            {
                foreach (var foreignKeyProperty in navigation.ForeignKey.Properties)
                {
                    ResetProperty(foreignKeyProperty, clonedEntity);
                }

                if (itemActionFilter != null && itemAction != null && itemActionFilter(clonedEntity.GetType().GetTypeInfo()))
                {
                    clonedEntity = itemAction(clonedEntity);
                }
            }

            if (navigationValue != null)
            {
                if (navigation.IsCollection)
                {
                    //var collection = source.InternalCloneCollection(references, entity, navigation.ClrType.GenericTypeArguments[0], navigation.ForeignKey.DeclaringEntityType.DefiningNavigationName, navigation.ForeignKey.DeclaringEntityType.DefiningEntityType, (IEnumerable)navigationValue);
                    var collection = source.InternalCloneCollection(references, navigation.ClrType.GenericTypeArguments[0], navigation.Name, navigation.DeclaringEntityType, (IEnumerable)navigationValue, itemActionFilter, itemAction);
                    navigation.PropertyInfo.SetValue(clonedEntity, collection);
                }
                else
                {
                    //var clonedPropertyValue = source.InternalClone(navigationValue, navigation.ForeignKey.DeclaringEntityType.DefiningNavigationName, navigation.ForeignKey.DeclaringEntityType.DefiningEntityType, references);
                    var clonedPropertyValue = source.InternalClone(navigationValue, navigation.Name, navigation.DeclaringEntityType, references, itemActionFilter, itemAction);
                    navigation.PropertyInfo.SetValue(clonedEntity, clonedPropertyValue);
                }
            }
        }


        private static void ResetSkipNavigationProperty<TNavigation>(DbContext source, object entity,
            Dictionary<object, object> references, object clonedEntity, TNavigation navigation, 
            Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
            where TNavigation : IReadOnlySkipNavigation
        {
            var navigationValue = navigation.PropertyInfo?.GetValue(entity);
            if (navigationValue != null)
            {
                if (navigation.ForeignKey != null && navigation.IsOnDependent)
                {
                    foreach (var foreignKeyProperty in navigation.ForeignKey.Properties)
                    {
                        ResetProperty(foreignKeyProperty, clonedEntity);
                    }
                }

                if (navigation.IsCollection)
                {
                    //var collection = source.InternalCloneCollection(references, entity, navigation.ClrType.GenericTypeArguments[0], navigation.ForeignKey.DeclaringEntityType.DefiningNavigationName, navigation.ForeignKey.DeclaringEntityType.DefiningEntityType, (IEnumerable)navigationValue);
                    var collection = source.InternalCloneCollection(references, navigation.ClrType.GenericTypeArguments[0], navigation.Name, navigation.DeclaringEntityType, (IEnumerable)navigationValue, itemActionFilter, itemAction);
                    navigation.PropertyInfo.SetValue(clonedEntity, collection);
                }
                else
                {
                    //var clonedPropertyValue = source.InternalClone(navigationValue, navigation.ForeignKey.DeclaringEntityType.DefiningNavigationName, navigation.ForeignKey.DeclaringEntityType.DefiningEntityType, references);
                    var clonedPropertyValue = source.InternalClone(navigationValue, navigation.Name, navigation.DeclaringEntityType, references, itemActionFilter, itemAction);

                    if (itemActionFilter != null && itemAction != null && itemActionFilter(clonedPropertyValue.GetType().GetTypeInfo()))
                    {
                        clonedPropertyValue = itemAction(clonedPropertyValue);
                    }

                    navigation.PropertyInfo.SetValue(clonedEntity, clonedPropertyValue);
                }
            }
        }

        private static IEnumerable InternalCloneCollection(this DbContext source, Dictionary<object, object> references, Type collectionItemType,
            string definingNavigationName, IReadOnlyEntityType definingEntityType, IEnumerable collectionValue,
            Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
        {
            // https://learn.microsoft.com/en-us/ef/core/modeling/relationships/navigations#collection-types
            // The underlying collection instance must be implement ICollection<T>, and must have a working Add method. It is common to use List<T> or HashSet<T>
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(collectionItemType));
            if (list == null)
            {
                throw new ArgumentNullException(nameof(collectionItemType));
            }
            foreach (var item in collectionValue)
            {
                var clonedItemValue = source.InternalClone(item, definingNavigationName, definingEntityType, references, itemActionFilter, itemAction);
                if(itemActionFilter  != null && itemAction != null && itemActionFilter(clonedItemValue.GetType().GetTypeInfo()))
                {
                    var mutatedValue = itemAction(clonedItemValue);
                    list.Add(mutatedValue);
                }
                else
                {
                    list.Add(clonedItemValue);
                }
            }

            return (IEnumerable)ConvertToCollectionType(collectionValue.GetType(), collectionItemType, list);
        }

        private static void ResetEntityProperties(this DbContext source, object entity, 
            string definingNavigationName, IReadOnlyEntityType definingEntityType, 
            object clonedEntity, Func<TypeInfo, bool>? itemActionFilter = null, Func<object, object>? itemAction = null)
        {
            foreach (var property in source.FindCurrentEntityType(entity.GetType(), definingNavigationName, definingEntityType).GetProperties())
            {
                if (property.IsConcurrencyToken)
                {
                    ResetProperty(property, clonedEntity);
                }

                if (property.IsPrimaryKey())
                {
                    ResetProperty(property, clonedEntity);
                }
            }

            // apply visitor delegate filter
            var clonedTypeInfo = clonedEntity.GetType().GetTypeInfo();
            if (itemActionFilter != null && itemAction != null && itemActionFilter(clonedTypeInfo))
            {
                // apply visitor delegate action
                clonedEntity = itemAction(clonedEntity);
            }
        }

        private static IReadOnlyEntityType FindCurrentEntityType(this DbContext source, Type entityClrType, string definingNavigationName, IReadOnlyEntityType definingEntityType)
        {
            if (!string.IsNullOrEmpty(definingNavigationName) && definingEntityType != null)
            {
                var entity = source.Model.FindEntityType(entityClrType, definingNavigationName, definingEntityType);
                if (entity != null)
                {
                    return entity;
                }
            }
            return source.Model.FindEntityType(entityClrType);
        }

        private static void ResetProperty(IReadOnlyProperty property, object entity)
        {
            if (property.PropertyInfo == null)
            {
                return;
            }

            property.PropertyInfo.SetValue(entity, property.IsNullable ? null : GetDefault(property.ClrType));
        }

        private static object GetDefault(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }
    }
}