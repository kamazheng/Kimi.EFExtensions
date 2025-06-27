using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Kimi.EFExtensions.DynamicLinqs;

public static class TypeConverter
{
    public static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                throw new ArgumentNullException(nameof(value), $"Cannot convert null to non-nullable value type {targetType.Name}.");
            return null;
        }

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                throw new ArgumentException($"Cannot convert empty string to non-nullable value type {targetType.Name}.");
            return null;
        }

        try
        {
            if (targetType == typeof(Guid) || targetType == typeof(Guid?))
                return Guid.Parse(value.ToString()!);

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            var effectiveType = underlyingType ?? targetType;
            if (effectiveType.IsEnum)
            {
                if (value is string enumStr)
                    return Enum.Parse(effectiveType, enumStr, ignoreCase: true);
                if (Enum.IsDefined(effectiveType, value))
                    return Enum.ToObject(effectiveType, value);
                throw new ArgumentException($"Value '{value}' is not a valid member of enum {effectiveType.Name}.");
            }

            if (IsConvertibleType(effectiveType))
                return Convert.ChangeType(value, effectiveType);

            var converter = TypeDescriptor.GetConverter(effectiveType);
            if (converter != null && converter.CanConvertFrom(value.GetType()))
                return converter.ConvertFrom(value);

            var parseMethod = effectiveType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (parseMethod != null)
                return parseMethod.Invoke(null, new[] { value.ToString() });

            throw new InvalidOperationException($"Cannot convert value of type {value.GetType().Name} to {targetType.Name}.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to convert value '{value}' to type {targetType.Name}: {ex.Message}", ex);
        }
    }

    public static object? NoExceptionConvertValue(object? value, Type targetType)
    {
        try
        {
            return ConvertValue(value, targetType);
        }
        catch
        {
            // Return null if conversion fails
            return null;
        }
    }
    private static bool IsConvertibleType(Type type)
    {
        return type == typeof(bool) ||
               type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(char) ||
               type == typeof(decimal) ||
               type == typeof(double) ||
               type == typeof(float) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(string) ||
               type == typeof(DateTime);
    }
}