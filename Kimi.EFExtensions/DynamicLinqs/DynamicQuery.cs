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
        // If using SQLite, do not include schema
        if (dbContext.Database.ProviderName?.ToLower().Contains("sqlite") == true)
            return $"[{tableName}]";
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

        var schemaName = entityType.GetSchema();
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
        // If using SQLite, do not include schema in table name
        if (dbContext.Database.ProviderName?.ToLower().Contains("sqlite") == true)
            queryBuilder.Append($"SELECT {columns} FROM [{tableName}]");
        else
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

        // Handle pagination (SQLite requires LIMIT with OFFSET)
        int offset = (page - 1) * topQty;
        if (offset > 0 || topQty > 0)
        {
            // Use LIMIT and OFFSET for SQLite compatibility
            queryBuilder.Append($" LIMIT {topQty} OFFSET {offset}");
        }

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


    /// <summary>
    /// 向数据库中插入或更新一条记录。
    /// </summary>
    /// <param name="dbContext">数据库上下文实例。</param>
    /// <param name="tableTypeName">实体类型的名称。</param>
    /// <param name="jsonObject">包含实体数据的 JSON 字符串。</param>
    /// <param name="byUser">执行操作的用户名（可选）。</param>
    /// <returns>插入或更新后的实体对象。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbContext"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="tableTypeName"/> 或 <paramref name="jsonObject"/> 为空或无效时抛出。</exception>
    /// <exception cref="InvalidOperationException">当反序列化失败、未找到主键或主键属性无效时抛出。</exception>
    /// <remarks>
    /// 1. 根据 <paramref name="tableTypeName"/> 获取实体类型。
    /// 2. 将 <paramref name="jsonObject"/> 反序列化为实体对象。
    /// 3. 检查主键是否存在，并处理自增主键的逻辑。
    /// 4. 判断记录是否存在：
    ///    - 若不存在，插入新记录（跳过导航属性）。
    ///    - 若存在，更新记录（跳过导航属性）。
    /// 5. 调用 <see cref="DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/> 
    /// 或 <see cref="Kimi.EFExtensions.SoftDeleteBaseDbContext.SaveChangesAsync(string,CancellationToken)"/> 保存更改。
    /// </remarks>
    public static async Task<object?> UpsertRecord(
                this DbContext dbContext,
                string tableTypeName,
        string jsonObject,
        string? byUser)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (string.IsNullOrWhiteSpace(tableTypeName)) throw new ArgumentException("Table type name cannot be null or empty.", nameof(tableTypeName));
        if (string.IsNullOrWhiteSpace(jsonObject)) throw new ArgumentException("JSON object cannot be null or empty.", nameof(jsonObject));

        // 1. 获取实体类型
        var entityType = dbContext.GetEntityTypeByName(tableTypeName);

        // 2. 反序列化 JSON 为实体对象
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entity = System.Text.Json.JsonSerializer.Deserialize(jsonObject, entityType, options)
            ?? throw new InvalidOperationException("Failed to deserialize JSON to entity.");

        // 3. 获取主键属性
        var efEntityType = dbContext.Model.FindEntityType(entityType);
        var primaryKey = efEntityType?.FindPrimaryKey();
        if (primaryKey == null || primaryKey.Properties.Count == 0)
            throw new InvalidOperationException($"No primary key found for entity type '{tableTypeName}'.");

        var pkProperty = primaryKey.Properties[0];
        var pkClrProperty = entityType.GetProperty(pkProperty.Name)
            ?? throw new InvalidOperationException($"Primary key property '{pkProperty.Name}' not found on entity type '{entityType.Name}'.");

        // 处理自增主键：如果是自增且插入，需将主键设为默认值
        bool isIdentity = pkProperty.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd;
        object? pkValue = pkClrProperty.GetValue(entity);
        if (isIdentity && (pkValue == null || pkValue.Equals(Activator.CreateInstance(pkClrProperty.PropertyType))))
        {
            // 主键为自增且未赋值，插入时EF会自动生成，无需查重
            pkClrProperty.SetValue(entity, Activator.CreateInstance(pkClrProperty.PropertyType));
            pkValue = null;
        }

        // 4. 判断记录是否存在
        object? existing = null;
        if (pkValue != null)
        {
            existing = await dbContext.GetDbRecordByPrimaryKey(tableTypeName, pkValue);
        }
        var efTypeForNav = dbContext.Model.FindEntityType(entityType);
        var navigationProps = efTypeForNav?.GetNavigations().ToList() ?? new List<Microsoft.EntityFrameworkCore.Metadata.INavigation>();
        var navigationNames = navigationProps.Select(n => n.Name).ToHashSet();
        if (existing == null)
        {
            // 插入：赋值所有非导航属性，递归插入集合导航属性（如子表）
            foreach (var prop in entityType.GetProperties())
            {
                if (!prop.CanWrite) continue;
                if (navigationNames.Contains(prop.Name)) continue;
            }
            // 处理集合导航属性（如 Children）
            foreach (var nav in navigationProps)
            {
                var navProp = entityType.GetProperty(nav.Name);
                if (navProp == null) continue;
                var navValue = navProp.GetValue(entity);
                if (navValue is System.Collections.IEnumerable children && navProp.PropertyType != typeof(string))
                {
                    foreach (var child in children)
                    {
                        // 设置外键
                        var fk = nav.ForeignKey.Properties.FirstOrDefault();
                        if (fk != null)
                        {
                            var childType = child.GetType();
                            var fkProp = childType.GetProperty(fk.Name);
                            if (fkProp != null)
                            {
                                fkProp.SetValue(child, pkClrProperty.GetValue(entity));
                            }
                        }
                        dbContext.Add(child);
                    }
                }
            }
            dbContext.Add(entity);
        }
        else
        {
            // 更新：赋值所有非导航属性，递归更新集合导航属性
            foreach (var prop in entityType.GetProperties())
            {
                if (!prop.CanWrite) continue;
                if (navigationNames.Contains(prop.Name)) continue;
                var value = prop.GetValue(entity);
                prop.SetValue(existing, value);
            }
            // 处理集合导航属性（如 Children）
            foreach (var nav in navigationProps)
            {
                var navProp = entityType.GetProperty(nav.Name);
                if (navProp == null) continue;
                var newChildren = navProp.GetValue(entity) as System.Collections.IEnumerable;
                var existingChildren = navProp.GetValue(existing) as System.Collections.IEnumerable;
                if (newChildren == null || navProp.PropertyType == typeof(string)) continue;

                // Convert to list for easier handling
                var newList = newChildren.Cast<object>().ToList();
                var existList = existingChildren?.Cast<object>().ToList() ?? new List<object>();
                // Assume single PK for child
                var childType = navProp.PropertyType.GenericTypeArguments.FirstOrDefault() ?? navProp.PropertyType.GetElementType();
                if (childType == null) continue;
                var childPk = dbContext.Model.FindEntityType(childType)?.FindPrimaryKey()?.Properties.FirstOrDefault();
                if (childPk == null) continue;
                var childPkProp = childType.GetProperty(childPk.Name);
                if (childPkProp == null) continue;

                // Update or add children
                foreach (var newChild in newList)
                {
                    var newChildPk = childPkProp.GetValue(newChild);
                    var existChild = existList.FirstOrDefault(e => Equals(childPkProp.GetValue(e), newChildPk));
                    if (existChild != null)
                    {
                        // Update existing child
                        foreach (var cprop in childType.GetProperties())
                        {
                            if (!cprop.CanWrite) continue;
                            var val = cprop.GetValue(newChild);
                            cprop.SetValue(existChild, val);
                        }
                        dbContext.Update(existChild);
                    }
                    else
                    {
                        // Set FK
                        var fk = nav.ForeignKey.Properties.FirstOrDefault();
                        if (fk != null)
                        {
                            var fkProp = childType.GetProperty(fk.Name);
                            if (fkProp != null)
                                fkProp.SetValue(newChild, pkClrProperty.GetValue(existing));
                        }
                        dbContext.Add(newChild);
                    }
                }
                // Remove children not in new list
                foreach (var existChild in existList)
                {
                    var existChildPk = childPkProp.GetValue(existChild);
                    if (!newList.Any(nc => Equals(childPkProp.GetValue(nc), existChildPk)))
                    {
                        dbContext.Remove(existChild);
                    }
                }
            }
            dbContext.Update(existing);
        }

        // 判断是否为 SoftDeleteBaseDbContext，优先调用带 userName 的 SaveChangesAsync
        if (dbContext is Kimi.EFExtensions.SoftDeleteBaseDbContext softDeleteDbContext)
        {
            // 这里你可以传递 userName，若有需要可扩展参数
            await softDeleteDbContext.SaveChangesAsync(byUser ?? "System");
        }
        else
        {
            await dbContext.SaveChangesAsync();
        }
        return existing ?? entity;
    }
}
