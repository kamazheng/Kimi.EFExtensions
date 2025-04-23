// ***********************************************************************
// <copyright file="EqualityHelper.cs" company="Molex(Chengdu)">
//     Copyright © Molex(Chengdu) 2025
// </copyright>
// ***********************************************************************
// Author           : MOLEX\kzheng
// Created          : 04/22/2025
// Last Modified    : 04/22/2025
// Description      : Provides utility methods for deep equality comparison of objects,
//                    with support for ignoring specified properties and interfaces.
// ***********************************************************************

namespace Kimi.EFExtensions;

using Kimi.EFExtensions.Interfaces;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class EqualityHelper
{
    // Cache property info to avoid repeated reflection calls
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    public static bool EntityAreEqual(object? obj1, object? obj2, IEnumerable<string>? propertiesToIgnore = null)
    {
        return AreEqual(obj1, obj2, propertiesToIgnore, typeof(ISoftDeleteEntity), typeof(IAuditableEntity));
    }

    public static bool AreEqual(object? obj1, object? obj2, IEnumerable<string>? propertiesToIgnore = null, params Type[] interfacesToIgnore)
    {
        // Step 1: Handle null cases
        if (ReferenceEquals(obj1, obj2)) return true; // Same reference or both null
        if (obj1 is null || obj2 is null) return false;

        // Step 2: Type check
        Type type1 = obj1.GetType();
        Type type2 = obj2.GetType();
        if (type1 != type2) return false;

        // Step 3: Get properties from cache or reflection
        var properties = _propertyCache.GetOrAdd(type1, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        // Convert propertiesToIgnore to a HashSet for O(1) lookups
        var ignoreSet = propertiesToIgnore != null
            ? new HashSet<string>(propertiesToIgnore, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var property in properties)
        {
            // Skip ignored properties (case-insensitive for flexibility)
            if (ignoreSet?.Contains(property.Name) == true)
                continue;

            // Skip navigation properties (collections or virtual properties)
            if (IsNavigationProperty(property))
                continue;

            // Skip properties from ignored interfaces
            if (interfacesToIgnore.Any(iface =>
                iface.IsAssignableFrom(type1) && iface.GetProperty(property.Name) != null))
                continue;

            // Compare property values
            var value1 = property.GetValue(obj1);
            var value2 = property.GetValue(obj2);

            if (!AreValuesEqual(value1, value2))
                return false;
        }

        return true;
    }

    private static bool IsNavigationProperty(PropertyInfo property)
    {
        // Simplified and clarified navigation property detection
        return property.PropertyType.IsClass &&
               property.PropertyType != typeof(string) &&
               (property.GetGetMethod()?.IsVirtual == true ||
                property.PropertyType.IsAssignableTo(typeof(IEnumerable<>)));
    }

    private static bool AreValuesEqual(object? value1, object? value2)
    {
        // Step 1: Handle null and same-reference cases
        if (ReferenceEquals(value1, value2)) return true; // Same reference or both null
        if (value1 is null || value2 is null) return false;

        // Step 2: Handle nullable types and unify underlying types
        Type type1 = Nullable.GetUnderlyingType(value1.GetType()) ?? value1.GetType();
        Type type2 = Nullable.GetUnderlyingType(value2.GetType()) ?? value2.GetType();
        if (type1 != type2) return false; // Different types after unboxing

        // Step 3: Handle floating-point numbers with tolerance
        if (type1 == typeof(float))
        {
            float f1 = (float)value1;
            float f2 = (float)value2;
            if (float.IsNaN(f1) && float.IsNaN(f2)) return true;
            if (float.IsInfinity(f1) && float.IsInfinity(f2)) return true;
            return Math.Abs(f1 - f2) <= 1e-6f; // Practical tolerance
        }
        if (type1 == typeof(double))
        {
            double d1 = (double)value1;
            double d2 = (double)value2;
            if (double.IsNaN(d1) && double.IsNaN(d2)) return true;
            if (double.IsInfinity(d1) && double.IsInfinity(d2)) return true;
            return Math.Abs(d1 - d2) <= 1e-6; // Practical tolerance
        }

        // Step 4: Handle decimal (exact comparison)
        if (type1 == typeof(decimal))
            return (decimal)value1 == (decimal)value2;

        // Step 5: Handle numeric types (exact equality)
        if (type1.IsPrimitive && type1 != typeof(bool) && type1 != typeof(char))
            return value1.Equals(value2);

        // Step 6: Handle strings
        if (type1 == typeof(string))
            return string.Equals((string)value1, (string)value2, StringComparison.Ordinal);

        // Step 7: Handle collections
        if (typeof(IEnumerable).IsAssignableFrom(type1) && type1 != typeof(string))
        {
            var enum1 = ((IEnumerable)value1).Cast<object>();
            var enum2 = ((IEnumerable)value2).Cast<object>();
            return enum1.SequenceEqual(enum2, EqualityComparer<object>.Default);
        }

        // Step 8: Handle nested objects (avoid deep recursion)
        if (type1.IsClass)
        {
            // Use a stack to prevent stack overflow for deep objects
            var stack = new Stack<(object, object)>();
            stack.Push((value1, value2));

            while (stack.Count > 0)
            {
                var (current1, current2) = stack.Pop();
                if (!AreValuesEqual(current1, current2)) return false; // Reuse this method for nested properties
            }
            return true;
        }

        // Step 9: Default comparison
        return value1.Equals(value2);
    }
}