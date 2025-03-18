// ***********************************************************************
// Author           : Kama Zheng
// Created          : 01/13/2025
// ***********************************************************************

using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Kimi.EFExtensions
{
    /// <summary>
    /// Defines the <see cref="DataDictionary" />
    /// </summary>
    public static class DataDictionary
    {
        #region Fields

        /// <summary>
        /// Defines the SqlQueries
        /// </summary>
        private static readonly Dictionary<string, string> SqlQueries = new()
        {
            { "SqlServer", @"
                SELECT 
                    t.name AS Table_Name,
                    c.name AS Column_Name,
                    ty.name AS Data_Type,
                    ISNULL(ep.value, 'No comment') AS Column_Comment,
                    CASE WHEN c.is_nullable = 1 THEN 'YES' ELSE 'NO' END AS Is_Nullable,
                    ISNULL(dc.definition, 'None') AS Default_Value,
                    CASE WHEN pks.column_id IS NOT NULL THEN 'YES' ELSE 'NO' END AS Is_Primary_Key,
                    ISNULL(fk.name, 'None') AS Foreign_Key
                FROM 
                    sys.tables t
                INNER JOIN 
                    sys.columns c ON t.object_id = c.object_id
                INNER JOIN 
                    sys.types ty ON c.user_type_id = ty.user_type_id
                LEFT JOIN 
                    sys.extended_properties ep ON t.object_id = ep.major_id 
                    AND c.column_id = ep.minor_id
                    AND ep.name = 'MS_Description'
                LEFT JOIN 
                    sys.default_constraints dc ON c.default_object_id = dc.object_id
                LEFT JOIN 
                    (
                        SELECT i.object_id, ic.column_id
                        FROM sys.indexes i
                        INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                        WHERE i.is_primary_key = 1
                    ) pks ON c.object_id = pks.object_id AND c.column_id = pks.column_id
                LEFT JOIN 
                    sys.foreign_key_columns fkc ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
                LEFT JOIN
                    sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
                ORDER BY 
                    Table_Name, Column_Name;
              "
            },
            { "Npgsql", @"
                SELECT 
                    cols.table_name AS Table_Name,
                    cols.column_name AS Column_Name,
                    cols.data_type AS Data_Type,
                    pg_catalog.col_description(c.oid, cols.ordinal_position::int) AS Column_Comment,
                    cols.is_nullable AS Is_Nullable,
                    cols.column_default AS Default_Value,
                    CASE WHEN pk.constraint_type = 'PRIMARY KEY' THEN 'YES' ELSE 'NO' END AS Is_Primary_Key,
                    COALESCE(fk.constraint_name, 'None') AS Foreign_Key
                FROM 
                    information_schema.columns cols
                LEFT JOIN 
                    pg_catalog.pg_class c ON c.relname = cols.table_name AND c.relnamespace = (SELECT oid FROM pg_catalog.pg_namespace WHERE nspname = cols.table_schema)
                LEFT JOIN 
                    information_schema.key_column_usage kcu ON cols.table_name = kcu.table_name AND cols.column_name = kcu.column_name AND cols.table_schema = kcu.table_schema
                LEFT JOIN 
                    information_schema.table_constraints pk ON kcu.constraint_name = pk.constraint_name AND pk.constraint_type = 'PRIMARY KEY' AND kcu.table_schema = pk.table_schema
                LEFT JOIN
                    information_schema.referential_constraints rc ON rc.constraint_name = kcu.constraint_name
                LEFT JOIN
                    information_schema.key_column_usage fkc ON rc.unique_constraint_name = fkc.constraint_name
                LEFT JOIN
                    information_schema.table_constraints fk ON fk.constraint_name = rc.constraint_name
                WHERE 
                    cols.table_schema NOT IN ('information_schema', 'pg_catalog')
                ORDER BY 
                    cols.table_name, cols.column_name;
              "
            },
            { "MySql", @"
                SELECT 
                    TABLE_NAME AS Table_Name,
                    COLUMN_NAME AS Column_Name,
                    COLUMN_TYPE AS Data_Type,
                    COLUMN_COMMENT AS Column_Comment,
                    IS_NULLABLE AS Is_Nullable,
                    COLUMN_DEFAULT AS Default_Value,
                    CASE WHEN COLUMN_KEY = 'PRI' THEN 'YES' ELSE 'NO' END AS Is_Primary_Key,
                    IFNULL(CONSTRAINT_NAME, 'None') AS Foreign_Key
                FROM 
                    INFORMATION_SCHEMA.COLUMNS
                LEFT JOIN 
                    INFORMATION_SCHEMA.KEY_COLUMN_USAGE ON TABLE_NAME = TABLE_NAME AND COLUMN_NAME = COLUMN_NAME AND TABLE_SCHEMA = DATABASE()
                WHERE 
                    TABLE_SCHEMA = DATABASE()
                ORDER BY 
                    TABLE_NAME, COLUMN_NAME;"
            },
            { "Oracle", @"
                SELECT 
                    tbl.table_name AS Table_Name,
                    col.column_name AS Column_Name,
                    col.data_type AS Data_Type,
                    cmt.comments AS Column_Comment,
                    col.nullable AS Is_Nullable,
                    col.data_default AS Default_Value,
                    CASE WHEN cons.constraint_type = 'P' THEN 'YES' ELSE 'NO' END AS Is_Primary_Key,
                    NVL(fk.constraint_name, 'None') AS Foreign_Key
                FROM 
                    user_tab_columns col
                JOIN 
                    user_tables tbl ON col.table_name = tbl.table_name
                LEFT JOIN 
                    user_col_comments cmt ON col.table_name = cmt.table_name AND col.column_name = cmt.column_name
                LEFT JOIN 
                    user_cons_columns ucc ON col.table_name = ucc.table_name AND col.column_name = ucc.column_name
                LEFT JOIN 
                    user_constraints cons ON ucc.constraint_name = cons.constraint_name AND cons.constraint_type = 'P'
                LEFT JOIN 
                    user_constraints fk ON ucc.constraint_name = fk.constraint_name AND fk.constraint_type = 'R'
                ORDER BY 
                    tbl.table_name, col.column_name;"
            }
        };

        #endregion

        #region Methods

        /// <summary>
        /// The GenerateMarkdownDocumentation
        /// </summary>
        /// <param name="context">The context<see cref="DbContext"/></param>
        /// <param name="outputPath">The outputPath<see cref="string"/></param>
        /// <returns>The <see cref="Task"/></returns>
        public static async Task GenerateMarkdownDocumentation(DbContext context, string outputPath)
        {
            try
            {
                var commentsAndTypes = await GetAllColumnCommentsAndTypesAsync(context);
                var markdownContent = GenerateMarkdown(commentsAndTypes);
                await File.WriteAllTextAsync(outputPath, markdownContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while generating documentation: {ex.Message}");
                // Consider logging the error as well
            }
        }

        public static async Task<Dictionary<string, Dictionary<string, (string DataType, string Comment, string IsNullable, string DefaultValue, string IsPrimaryKey, string ForeignKey)>>>
        GetAllColumnCommentsAndTypesAsync(DbContext context)
        {
            var commentsAndTypes = new Dictionary<string, Dictionary<string, (string, string, string, string, string, string)>>();

            try
            {
                using var connection = context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                var providerName = context.Database.ProviderName ?? throw new NotSupportedException("Database provider is undefined.");
                var query = SqlQueries.FirstOrDefault(q => providerName.Contains(q.Key)).Value
                            ?? throw new NotSupportedException($"Database provider {providerName} is not supported.");

                using var command = connection.CreateCommand();
                command.CommandText = query;

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var tableName = reader.GetString(0);
                    var columnName = reader.GetString(1);
                    var dataType = reader.GetString(2);
                    var comment = await reader.IsDBNullAsync(3) ? "Null" : reader.GetString(3);
                    var isNullable = reader.GetString(4);
                    var defaultValue = await reader.IsDBNullAsync(5) ? "Null" : reader.GetString(5);
                    var isPrimaryKey = reader.GetString(6);
                    var foreignKey = await reader.IsDBNullAsync(7) ? "Null" : reader.GetString(7);

                    if (!commentsAndTypes.TryGetValue(tableName, out var columns))
                    {
                        columns = [];
                        commentsAndTypes[tableName] = columns;
                    }

                    columns[columnName] = (dataType, comment, isNullable, defaultValue, isPrimaryKey, foreignKey);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while retrieving data: {ex.Message}");
                // Consider logging the error as well
            }

            return commentsAndTypes;
        }

        private static string GenerateMarkdown(Dictionary<string, Dictionary<string, (string DataType, string Comment, string IsNullable, string DefaultValue, string IsPrimaryKey, string ForeignKey)>> commentsAndTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Database Schema Documentation\n");

            foreach (var (table, columns) in commentsAndTypes)
            {
                sb.AppendLine($"## Table: {table}\n");
                sb.AppendLine("| Column Name | Data Type | Comment | Is Nullable | Default Value | Is Primary Key | Foreign Key |");
                sb.AppendLine("|-------------|-----------|---------|-------------|---------------|----------------|-------------|");

                foreach (var (columnName, (dataType, columnComment, isNullable, defaultValue, isPrimaryKey, foreignKey)) in columns)
                {
                    sb.AppendLine($"| {columnName} | {dataType} | {columnComment ?? "No comment"} | {isNullable} | {defaultValue ?? "None"} | {isPrimaryKey} | {foreignKey} |");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion
    }
}

