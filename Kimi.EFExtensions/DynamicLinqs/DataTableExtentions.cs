using System.Data;
using System.Dynamic;

namespace Kimi.EFExtensions.DynamicLinqs;

public static class DataTableExtentions
{
    public static List<ExpandoObject> ToDynamicList(this DataTable dt)
    {
        // Validate input
        if (dt == null) throw new ArgumentNullException(nameof(dt), "DataTable cannot be null.");

        var list = new List<ExpandoObject>(dt.Rows.Count); // Preallocate capacity for better performance

        foreach (DataRow row in dt.Rows)
        {
            dynamic expando = new ExpandoObject();
            var expandoDict = (IDictionary<string, object?>)expando; // Use object? to allow null values

            foreach (DataColumn col in dt.Columns)
            {
                // Handle DBNull.Value by converting it to null
                expandoDict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
            }

            list.Add(expando);
        }

        return list;
    }

    public static List<object> MapTableToList(this DataTable table, Type objectType)
    {
        // Validate inputs
        if (table == null)
        {
            throw new ArgumentNullException(nameof(table), "DataTable cannot be null!");
        }
        if (objectType == null)
        {
            throw new ArgumentNullException(nameof(objectType), "ObjectType cannot be null!");
        }

        // Cache property information to avoid repeated reflection calls
        var properties = objectType.GetProperties()
            .Where(prop => table.Columns.Contains(prop.Name)) // Only consider properties that match column names
            .ToList();

        // Prepare the result list
        var result = new List<object>();

        // Iterate through each row in the DataTable
        foreach (DataRow row in table.Rows)
        {
            // Create an instance of the target object type
            var item = Activator.CreateInstance(objectType)!;

            // Map values from the DataRow to the object properties
            foreach (var property in properties)
            {
                var columnValue = row[property.Name];
                if (columnValue != DBNull.Value) // Handle DBNull values
                {
                    // Check if the property type is nullable
                    var propertyType = property.PropertyType;
                    var underlyingType = Nullable.GetUnderlyingType(propertyType);

                    if (underlyingType != null)
                    {
                        // Property is nullable (e.g., bool?, int?, etc.)
                        var convertedValue = Convert.ChangeType(columnValue, underlyingType);
                        property.SetValue(item, convertedValue);
                    }
                    else
                    {
                        // Property is not nullable
                        var convertedValue = Convert.ChangeType(columnValue, propertyType);
                        property.SetValue(item, convertedValue);
                    }
                }
                else
                {
                    // Handle DBNull by setting null for nullable types
                    if (Nullable.GetUnderlyingType(property.PropertyType) != null)
                    {
                        property.SetValue(item, null);
                    }
                }
            }

            // Add the mapped object to the result list
            result.Add(item);
        }

        return result;
    }

    public static List<T> MapTableToList<T>(this DataTable table) where T : class, new()
    {
        // Validate inputs
        if (table == null)
        {
            throw new ArgumentNullException(nameof(table), "DataTable cannot be null!");
        }

        // Cache property information to avoid repeated reflection calls
        var properties = typeof(T).GetProperties()
            .Where(prop => table.Columns.Contains(prop.Name)) // Only consider properties that match column names
            .ToList();

        // Prepare the result list
        var result = new List<T>();

        // Iterate through each row in the DataTable
        foreach (DataRow row in table.Rows)
        {
            // Create an instance of the target object type
            var item = new T();

            // Map values from the DataRow to the object properties
            foreach (var property in properties)
            {
                var columnValue = row[property.Name];
                if (columnValue != DBNull.Value) // Handle DBNull values
                {
                    property.SetValue(item, Convert.ChangeType(columnValue, property.PropertyType));
                }
            }

            // Add the mapped object to the result list
            result.Add(item);
        }

        return result;
    }
}
