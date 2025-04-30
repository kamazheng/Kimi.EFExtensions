// ***********************************************************************
// Author           : kama zheng
// Created          : 03/18/2025
// ***********************************************************************

using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Kimi.EFExtensions.DynamicLinqs;

public static class DynamicQuery
{

    public static string GetTableFullName(this DbContext dbContext, Type entityType)
    {
        var ientityType = dbContext.Model.FindEntityType(entityType);
        if (ientityType == null) return string.Empty;
        var schema = ientityType.GetSchema();
        var tableName = ientityType.GetTableName();
        return $"[{schema}].[{tableName}]";
    }

    /// <summary>
    /// Executes a raw SQL query asynchronously and maps the result to a list of entities.
    /// </summary>
    /// <typeparam name="T">The type of the entities to be returned.</typeparam>
    /// <param name="context">The <see cref="DbContext"/> instance.</param>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="map">The mapping function to convert the <see cref="DbDataReader"/> result to an entity of type <typeparamref name="T"/>.</param>
    /// <returns>A list of entities resulting from the SQL query.</returns>
    public static async Task<List<T>> RawSqlQueryAsync<T>(this DbContext context, string query, Func<DbDataReader, T> map)
    {
        using (context)
        {
            using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                command.CommandType = CommandType.Text;
                await context.Database.OpenConnectionAsync();

                using (var result = await command.ExecuteReaderAsync())
                {
                    var entities = new List<T>();

                    while (await result.ReadAsync())
                    {
                        entities.Add(map(result));
                    }

                    return entities;
                }
            }
        }
    }

    public static async Task<DataTable> RawSqlQueryAsync(
        this DbContext context,
        string query,
        IDictionary<string, object>? parameters = null)
    {
        // Validate inputs
        if (context == null) throw new ArgumentNullException(nameof(context), "DbContext cannot be null.");
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query cannot be null or empty.", nameof(query));

        using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            try
            {
                // Set up the command
                command.CommandText = query;
                command.CommandType = CommandType.Text;

                // Add parameters if provided
                if (parameters != null && parameters.Count > 0)
                {
                    foreach (var param in parameters)
                    {
                        var dbParam = command.CreateParameter();
                        dbParam.ParameterName = param.Key; // Parameter name (e.g., "@name")
                        dbParam.Value = param.Value ?? DBNull.Value; // Handle null values
                        command.Parameters.Add(dbParam);
                    }
                }

                // Open the connection and execute the query
                await context.Database.OpenConnectionAsync();

                DataTable myTable = new DataTable();
                using (var reader = await command.ExecuteReaderAsync())
                {
                    myTable.Load(reader); // Load data into the DataTable
                }

                return myTable;
            }
            catch (Exception ex)
            {
                // Log and rethrow exceptions
                await Console.Error.WriteLineAsync($"Error executing raw SQL query: {ex.Message}");
                throw new InvalidOperationException("An error occurred while executing the raw SQL query.", ex);
            }
            finally
            {
                // Ensure the connection is closed
                if (context.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    await context.Database.CloseConnectionAsync();
                }
            }
        }
    }

    public static IEnumerable<Type> GetTableClassesFromDbContext(this DbContext dbContext)
    {
        return dbContext.GetType().GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0]);
    }

    public static async Task<List<object>?> GetDbRecordsByRawSql(
        this DbContext dbContext,
        Type tableType,
        string? whereClause = "",
        int topQty = 1000,
        int page = 1,
        string? orderBy = "", // Optional: User-provided ORDER BY clause
        bool isDescending = true)
    {
        // Input validation
        if (dbContext == null) throw new ArgumentNullException(nameof(dbContext), "DbContext cannot be null.");
        if (tableType == null) throw new ArgumentNullException(nameof(tableType), "Table type cannot be null.");
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page number must be greater than or equal to 1.");
        if (topQty <= 0) throw new ArgumentOutOfRangeException(nameof(topQty), "Top quantity must be greater than 0.");

        // Sensitive word check
        string[] sensitiveWords = { "DROP ", "DELETE ", "TRUNCATE " };
        if (whereClause?.ContainsSensitiveWords(sensitiveWords) == true)
        {
            throw new ArgumentException("Where clause contains sensitive words.", nameof(whereClause));
        }

        // Entity and table metadata
        var entityType = dbContext.Model.FindEntityType(tableType.FullName ?? throw new InvalidOperationException("Table type must have a full name."));
        if (entityType == null) throw new InvalidOperationException($"Entity type '{tableType.FullName}' not found in the DbContext model.");

        var schemaName = entityType.GetSchema() ?? "dbo";
        var tableName = entityType.GetTableName() ?? throw new InvalidOperationException("Table name not found for the entity type.");

        // Cache property-to-column mapping
        var entityProperties = entityType.GetProperties();
        var propertyToColumnMap = entityProperties.ToDictionary(
            p => p.Name,
            p => p.GetColumnName() ?? throw new InvalidOperationException($"Column name not found for property '{p.Name}'.")
        );

        // Determine the ORDER BY column
        string? orderByColumn;
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            // Use the user-provided ORDER BY clause
            orderByColumn = orderBy; // Wrap in brackets for safety
        }
        else
        {
            // Use the first primary key for ordering if no ORDER BY is provided
            var primaryKeyProperties = entityType.FindPrimaryKey()?.Properties;
            orderByColumn = primaryKeyProperties?.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(orderByColumn))
            {
                orderByColumn = "(SELECT NULL)"; // Default fallback if no primary key exists
            }
            else
            {
                orderByColumn = $"[{orderByColumn}]"; // Wrap in brackets for safety
                if (isDescending) orderByColumn = orderByColumn + " DESC";
            }
        }

        // Build the SQL query
        var queryBuilder = new StringBuilder();
        var quotedColumns = propertyToColumnMap.Select(p => $"[{p.Key}]"); // Quote column names
        var columns = string.Join(", ", quotedColumns); // Explicitly specify columns
        queryBuilder.Append($"SELECT {columns} FROM [{schemaName}].[{tableName}]");

        // Handle WHERE clause with parameterization
        var parameters = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            queryBuilder.Append(" WHERE ");
            queryBuilder.Append($"({whereClause})"); // Ensure whereClause is parameterized externally
        }

        // Handle ORDER BY clause
        queryBuilder.Append($" ORDER BY {orderByColumn}");

        // Handle pagination
        int offset = (page - 1) * topQty;
        queryBuilder.Append($" OFFSET {offset} ROWS FETCH NEXT {topQty} ROWS ONLY");

        var query = queryBuilder.ToString();
        query = ReplaceEscapedFieldNamesWithColumnNames(query, propertyToColumnMap);
        try
        {
            // Log the query for debugging purposesw
            Console.WriteLine($"Executing query: {query}");

            // Execute the query with parameters
            var dataTable = await dbContext.RawSqlQueryAsync(query, parameters);
            // If no data is returned, return null
            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                return null;
            }
            return dataTable.MapTableToList(tableType);
        }
        catch (Exception ex)
        {
            // Log and rethrow exceptions
            await Console.Error.WriteLineAsync($"Error executing query: {ex.Message}");
            throw new InvalidOperationException("An error occurred while executing the query.", ex);
        }
    }

    public static async Task<List<object>?> GetDbRecordsByRawSql(
        this DbContext dbContext,
        string tableTypeName,
        string? whereClause = "",
        int topQty = 1000,
        int page = 1,
        string? orderBy = "", // Optional: User-provided ORDER BY clause
        bool isDescending = true)
    {
        // Input validation
        if (dbContext == null) throw new ArgumentNullException(nameof(dbContext), "DbContext cannot be null.");
        if (string.IsNullOrWhiteSpace(tableTypeName)) throw new ArgumentException("Table type name cannot be null or empty.", nameof(tableTypeName));

        Type? entityType = dbContext.GetEntityTypeByName(tableTypeName);

        // Call the existing method to get the DataTable
        var dataTable = await dbContext.GetDbRecordsByRawSql(
            entityType,
            whereClause,
            topQty,
            page,
            orderBy,
            isDescending
        );
        return dataTable;
    }

    public static Type GetEntityTypeByName(this DbContext dbContext, string tableTypeName)
    {
        // Find the entity type by matching either the table name or the class name
        var entityType = dbContext.Model.GetEntityTypes()
            .FirstOrDefault(et =>
                (et.GetTableName()?.Equals(tableTypeName, StringComparison.OrdinalIgnoreCase) ?? false) || // Match table name (null-safe)
                et.ClrType.Name.Equals(tableTypeName, StringComparison.OrdinalIgnoreCase) // Match class name
            )?.ClrType;

        if (entityType == null)
        {
            throw new InvalidOperationException($"Entity type with name '{tableTypeName}' not found in the DbContext model.");
        }

        return entityType;
    }

    public static async Task<object?> GetDbRecordByPrimaryKey(
        this DbContext dbContext,
        string tableTypeName,
        object primaryKeyValue)
    {
        // Input validation
        if (dbContext == null) throw new ArgumentNullException(nameof(dbContext), "DbContext cannot be null.");
        if (string.IsNullOrWhiteSpace(tableTypeName)) throw new ArgumentException("Table type name cannot be null or empty.", nameof(tableTypeName));

        // Find the entity type by matching either the table name or the class name
        var entityType = dbContext.Model.GetEntityTypes()
            .FirstOrDefault(et =>
                (et.GetTableName()?.Equals(tableTypeName, StringComparison.OrdinalIgnoreCase) ?? false) || // Match table name (null-safe)
                et.ClrType.Name.Equals(tableTypeName, StringComparison.OrdinalIgnoreCase) // Match class name
            );

        if (entityType == null)
        {
            throw new InvalidOperationException($"Entity type with name '{tableTypeName}' not found in the DbContext model.");
        }

        // Get the first primary key property
        var primaryKeyProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (primaryKeyProperty == null)
        {
            throw new InvalidOperationException($"No primary key found for entity type '{tableTypeName}'.");
        }

        // Get the column name and data type of the primary key
        var primaryKeyColumnName = primaryKeyProperty.GetColumnName()
                                   ?? throw new InvalidOperationException($"Column name not found for primary key of entity type '{tableTypeName}'.");
        var primaryKeyColumnType = primaryKeyProperty.ClrType;

        // Construct the WHERE clause based on the data type and nullability
        string whereClause;
        if (primaryKeyValue == null)
        {
            // Handle null values
            whereClause = $"[{primaryKeyColumnName}] IS NULL";
        }
        else
        {
            // Format the value based on its type
            string formattedPrimaryKeyValue;
            if (primaryKeyColumnType == typeof(string))
            {
                // Enclose strings in single quotes
                formattedPrimaryKeyValue = $"'{primaryKeyValue.ToString() ?? string.Empty}'";
            }
            else if (primaryKeyColumnType == typeof(DateTime) || primaryKeyColumnType == typeof(DateTimeOffset))
            {
                // Format dates in ISO 8601 format and enclose in single quotes
                formattedPrimaryKeyValue = $"'{((DateTime)primaryKeyValue).ToString("yyyy-MM-dd HH:mm:ss")}'";
            }
            else if (Nullable.GetUnderlyingType(primaryKeyColumnType) != null)
            {
                // Handle nullable types (e.g., int?, DateTime?)
                var underlyingType = Nullable.GetUnderlyingType(primaryKeyColumnType);
                if (underlyingType == typeof(string))
                {
                    formattedPrimaryKeyValue = $"'{primaryKeyValue.ToString() ?? string.Empty}'";
                }
                else if (underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
                {
                    formattedPrimaryKeyValue = $"'{((DateTime)primaryKeyValue).ToString("yyyy-MM-dd HH:mm:ss")}'";
                }
                else
                {
                    // No quotes for numeric nullable types
                    formattedPrimaryKeyValue = primaryKeyValue.ToString() ?? string.Empty;
                }
            }
            else
            {
                // No quotes for numeric types
                formattedPrimaryKeyValue = primaryKeyValue.ToString() ?? string.Empty;
            }

            whereClause = $"[{primaryKeyColumnName}] = {formattedPrimaryKeyValue}";
        }

        // Call the existing method to get records
        var records = await dbContext.GetDbRecordsByRawSql(
            tableTypeName: tableTypeName,
            whereClause: whereClause,
            topQty: 1, // Only fetch one record
            page: 1,
            orderBy: "", // No need for ordering
            isDescending: false
        );

        // Return the first record or null if no records are found
        return records?.FirstOrDefault();

    }

    public static string ReplaceEscapedFieldNamesWithColumnNames(string sqlQuery, Dictionary<string, string> propertyToColumnMap)
    {
        foreach (var kvp in propertyToColumnMap.OrderByDescending(kvp => kvp.Key.Length))
        {
            string escapedPropertyName = $"[{kvp.Key}]";
            string escapedColumnName = $"[{kvp.Value}]";
            if (escapedPropertyName != escapedColumnName)
            {
                sqlQuery = sqlQuery.Replace(escapedPropertyName, escapedColumnName, StringComparison.OrdinalIgnoreCase);
            }
        }
        return sqlQuery;
    }
}
