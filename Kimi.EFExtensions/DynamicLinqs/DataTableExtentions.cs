using System.Data;
using System.Dynamic;
using System.Reflection;

namespace Kimi.EFExtensions.DynamicLinqs;

public static class DataTableExtensions
{
    public static List<ExpandoObject> ToDynamicList(this DataTable dt)
    {
        if (dt == null)
            throw new ArgumentNullException(nameof(dt), "DataTable cannot be null.");

        var list = new List<ExpandoObject>(dt.Rows.Count);

        foreach (DataRow row in dt.Rows)
        {
            var expando = new ExpandoObject();
            var expandoDict = (IDictionary<string, object?>)expando;

            foreach (DataColumn col in dt.Columns)
            {
                expandoDict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }

            list.Add(expando);
        }

        return list;
    }

    public static List<T> MapTableToList<T>(this DataTable table) where T : class, new()
    {
        return MapTableToList(table, typeof(T)).Cast<T>().ToList();
    }

    public static List<object> MapTableToList(this DataTable table, Type objectType)
    {
        // Validate inputs
        if (table == null)
            throw new ArgumentNullException(nameof(table), "DataTable cannot be null.");
        if (objectType == null)
            throw new ArgumentNullException(nameof(objectType), "Object type cannot be null.");
        if (!objectType.IsClass || objectType.IsAbstract)
            throw new ArgumentException($"Type {objectType.Name} must be a non-abstract class.", nameof(objectType));

        // Cache property information
        var properties = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(prop => prop.CanWrite && table.Columns.Contains(prop.Name))
            .Select(prop => new
            {
                Property = prop,
                TargetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType,
                IsNullable = Nullable.GetUnderlyingType(prop.PropertyType) != null
            })
            .ToList();

        var result = new List<object>(table.Rows.Count);

        foreach (DataRow row in table.Rows)
        {
            var item = Activator.CreateInstance(objectType)!;

            foreach (var propInfo in properties)
            {
                var columnValue = row[propInfo.Property.Name];
                if (columnValue != DBNull.Value)
                {
                    var convertedValue = TypeConverter.NoExceptionConvertValue(columnValue, propInfo.TargetType);
                    propInfo.Property.SetValue(item, convertedValue);
                }
                else if (propInfo.IsNullable)
                {
                    propInfo.Property.SetValue(item, null);
                }
            }

            result.Add(item);
        }

        return result;
    }
}

