// ***********************************************************************
// Author           : kama zheng
// Created          : 04/30/2025
// ***********************************************************************

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Kimi.EFExtensions;

public static class MapperHelper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    /// <summary>
    /// Maps the properties of a source object to an existing destination object using explicit type mappings.
    /// </summary>
    /// <typeparam name="TSource">The type of the source object.</typeparam>
    /// <typeparam name="TDest">The type of the destination object.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <param name="dest">The existing destination object to map to.</param>
    /// <param name="typeMappings">Optional dictionary of type mappings for nested class types. Order source type, then destination type</param>
    /// <param name="ignoreNestedCollections">Flag indicating whether to ignore mapping of nested collections.</param>
    /// <param name="ignoreProperties">Optional array of property names to ignore during mapping.</param>
    public static void Map<TSource, TDest>(this TSource source, TDest dest,
        Dictionary<Type, Type>? typeMappings = null,
        bool ignoreNestedCollections = false,
        params string[] ignoreProperties)
        where TSource : class
        where TDest : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dest);

        var ignoreSet = ignoreProperties.Length > 0
            ? new HashSet<string>(ignoreProperties, StringComparer.OrdinalIgnoreCase)
            : null;

        object mapped = DynamicMap(source, typeof(TDest), typeMappings, ignoreNestedCollections, ignoreSet);

        foreach (var prop in GetProperties(typeof(TDest), ignoreSet))
        {
            if (prop.CanWrite)
            {
                prop.SetValue(dest, prop.GetValue(mapped));
            }
        }
    }

    /// <summary>
    /// Creates a new destination object and maps the properties of a source object to it using explicit type mappings.
    /// </summary>
    /// <typeparam name="TSource">The type of the source object.</typeparam>
    /// <typeparam name="TDest">The type of the destination object.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <param name="typeMappings">Optional dictionary of type mappings for nested class types.</param>
    /// <param name="ignoreNestedCollections">Flag indicating whether to ignore mapping of nested collections.</param>
    /// <param name="ignoreProperties">Optional array of property names to ignore during mapping.</param>
    /// <returns>The new destination object with the mapped properties.</returns>
    public static TDest Map<TSource, TDest>(this TSource source,
        Dictionary<Type, Type>? typeMappings = null,
        bool ignoreNestedCollections = false,
        params string[] ignoreProperties)
        where TSource : class
        where TDest : class, new()
    {
        var ignoreSet = ignoreProperties.Length > 0
            ? new HashSet<string>(ignoreProperties, StringComparer.OrdinalIgnoreCase)
            : null;

        return (TDest)DynamicMap(source, typeof(TDest), typeMappings, ignoreNestedCollections, ignoreSet);
    }

    private static object DynamicMap(object source, Type destType,
        Dictionary<Type, Type>? typeMappings = null,
        bool ignoreNestedCollections = false,
        HashSet<string>? ignoreProperties = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destType);

        object dest = Activator.CreateInstance(destType)!;
        var sourceType = source.GetType();
        var sourceProps = GetProperties(sourceType, ignoreProperties);

        foreach (var sourceProp in sourceProps)
        {
            var destProp = destType.GetProperty(sourceProp.Name);
            if (destProp == null || !destProp.CanWrite)
                continue;

            object? value = sourceProp.GetValue(source);
            if (ShouldSkipMapping(sourceProp.PropertyType, value))
                continue;

            if (destProp.PropertyType.IsAssignableFrom(sourceProp.PropertyType))
            {
                destProp.SetValue(dest, value);
            }
            else if (IsNestedClassType(sourceProp.PropertyType) && value != null)
            {
                Type? mappedType = typeMappings?.GetValueOrDefault(sourceProp.PropertyType) ?? destProp.PropertyType;
                if (mappedType != null)
                {
                    object nestedDest = DynamicMap(value, mappedType, typeMappings, ignoreNestedCollections, ignoreProperties);
                    destProp.SetValue(dest, nestedDest);
                }
            }
            else if (!ignoreNestedCollections && IsCollection(sourceProp.PropertyType) && value != null)
            {
                Type? sourceItemType = GetCollectionItemType(sourceProp.PropertyType);
                Type? destItemType = typeMappings?.GetValueOrDefault(sourceItemType!) ?? GetCollectionItemType(destProp.PropertyType);
                if (destItemType != null)
                {
                    object mappedCollection = MapCollection(value, destItemType, typeMappings, ignoreNestedCollections, ignoreProperties);
                    destProp.SetValue(dest, mappedCollection);
                }
            }
        }
        return dest;
    }

    private static object MapCollection(object sourceCollection, Type destItemType,
        Dictionary<Type, Type>? typeMappings,
        bool ignoreNestedCollections,
        HashSet<string>? ignoreProperties)
    {
        Type destCollectionType = typeof(List<>).MakeGenericType(destItemType);
        object newCollection = Activator.CreateInstance(destCollectionType)!;

        foreach (var item in (IEnumerable)sourceCollection)
        {
            if (item == null) continue;
            object mappedItem = DynamicMap(item, destItemType, typeMappings, ignoreNestedCollections, ignoreProperties);
            ((IList)newCollection).Add(mappedItem);
        }
        return newCollection;
    }

    private static PropertyInfo[] GetProperties(Type type, HashSet<string>? ignoreProperties)
    {
        return _propertyCache.GetOrAdd(type, t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return ignoreProperties == null
                ? props
                : props.Where(p => !ignoreProperties.Contains(p.Name)).ToArray();
        });
    }

    private static bool ShouldSkipMapping(Type propertyType, object? value)
    {
        return value == null && (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null);
    }

    private static bool IsNestedClassType(Type type)
    {
        return type.IsClass && type != typeof(string) && !IsCollection(type);
    }

    private static bool IsCollection(Type type)
    {
        return typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string);
    }

    private static Type? GetCollectionItemType(Type collectionType)
    {
        return collectionType.IsGenericType ? collectionType.GetGenericArguments().FirstOrDefault() : null;
    }
}
