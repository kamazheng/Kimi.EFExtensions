using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Linq.Dynamic.Core;

namespace Kimi.EFExtensions;

public static class DbContextExtensions
{
    public static IQueryable<object>? Set(this DbContext _context, Type t)
    {
        var method = _context.GetType().GetMethods().First(m => m.Name == "Set" && m.IsGenericMethod).MakeGenericMethod(t);
        var result = method.Invoke(_context, null);
        return (IQueryable<object>?)result;
    }

    public static IQueryable<object>? Set(this DbContext _context, string entityName)
    {
        var st = _context?.Model?.FindEntityType(entityName)?.ClrType;
        if (st is null) return null;
        IQueryable<object>? ObjectContext = _context?.Set(st!);
        return ObjectContext;
    }

    public static object? GetSingleKeyValue(this DbContext _context, object entity)
    {
        var keyName = _context.GetKeyNames(entity)?.Single();
        if (string.IsNullOrEmpty(keyName)) return null;
        var result = entity?.GetType()?.GetProperty(keyName)?.GetValue(entity, null);
        return result;
    }

    public static string? GetKeyName(this DbContext _context, object entity)
    {
        var keyName = _context.GetKeyNames(entity)?.Single();
        return keyName;
    }

    public static IEnumerable<string>? GetKeyNames(this DbContext _context, object entity)
    {
        var keyNames = _context.Model?.FindEntityType(entity.GetType())?.FindPrimaryKey()?.Properties
            .Select(x => x.Name);
        return keyNames;
    }

    public static string GetTableFullName(this DbContext dbContext, Type entityType)
    {
        var ientityType = dbContext.Model.FindEntityType(entityType);
        if (ientityType == null) return string.Empty;
        var schema = ientityType.GetSchema();
        var tableName = ientityType.GetTableName();
        return $"[{schema}].[{tableName}]";
    }

    //https://stackoverflow.com/questions/35631903/raw-sql-query-without-dbset-entity-framework-core
    public static List<T> RawSqlQuery<T>(this DbContext context, string query, Func<DbDataReader, T> map)
    {
        using (context)
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = query;
            command.CommandType = CommandType.Text;
            context.Database.OpenConnection();

            using var result = command.ExecuteReader();
            var entities = new List<T>();

            while (result.Read())
            {
                entities.Add(map(result));
            }

            return entities;
        }
    }

    public static DataTable RawSqlQuery(this DbContext context, string query)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = query;
        command.CommandType = CommandType.Text;
        context.Database.OpenConnection();

        DataTable myTable = new DataTable();
        myTable.Load(command.ExecuteReader());
        return myTable;
    }

    public static DbContext? GetDbContextFromEntityType(Type entityType)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types.Where(type => type.IsSubclassOf(typeof(DbContext))))
            {
                var context = Activator.CreateInstance(type) as DbContext;
                if (context?.Set(entityType)?.Count() >= 0)
                {
                    return context;
                }
            }
        }
        return null;
    }

    public static DbContext? GetDbContextFromTableClassType(this Type tableClassType)
    {
        // Use LINQ to find the first DbContext type that has a non-null queryable for the table class type
        var dbContextType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(DbContext)))
            .FirstOrDefault(t => Activator.CreateInstance(t) is DbContext dbContext && dbContext.Set(tableClassType) != null);

        // Return the instance of the DbContext type, or null if not found
        return dbContextType != null ? Activator.CreateInstance(dbContextType) as DbContext : null;
    }

    public static IEnumerable<Type> GetTableClassesFromDbContext(DbContext dbContext)
    {
        return dbContext.GetType().GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0]);
    }

    public static IEnumerable<Type> GetAllTableClasses()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(DbContext)))
            .SelectMany(type => type.GetProperties()
                .Where(p => p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(p => p.PropertyType.GetGenericArguments()[0]));
    }

    public static Type GetEntityClrType(this DbContext defaultContext, string tableClassName)
    {
        var type = defaultContext.Model.GetEntityTypes()
             .FirstOrDefault(et =>
                 (et.GetTableName()?.Equals(tableClassName, StringComparison.OrdinalIgnoreCase) ?? false) || // Match table name (null-safe)
                 et.ClrType.Name.Equals(tableClassName, StringComparison.OrdinalIgnoreCase) // Match class name
             )?.ClrType;
        if (type != null)
        {
            return type;
        }
        else
        {
            throw new InvalidOperationException($"Entity type {tableClassName} not found.");
        }
    }

    public static async Task UpsertWithoutSaveAsync(this DbContext _dbContext, object input)
    {
        object? pkValue = _dbContext.GetSingleKeyValue(input) ?? default;
        object? exist = await _dbContext.FindAsync(input.GetType(), pkValue);
        if (pkValue.IsPrimaryKeyDefault() || exist == null)
        {
            var pkName = _dbContext.GetKeyName(input);
            var pkProperty = input.GetType().GetProperty(pkName!);
            if (pkProperty is not null && pkProperty.PropertyType == typeof(Guid) && pkValue.IsPrimaryKeyDefault())
            {
                pkProperty.SetValue(input, Guid.NewGuid());
            }
            await _dbContext.AddAsync(input);
        }
        else
        {
            _dbContext.Entry(exist).CurrentValues.SetValues(input); //Important !!!
            _dbContext.Entry(exist).State = EntityState.Modified;
        }
    }

    public static bool IsPrimaryKeyDefault(this object? obj)
    {
        if (obj == null) return true;
        if (obj == default) return true;
        if (obj?.ToString() == "") return true;
        if (obj?.ToString() == "0") return true;
        if (obj?.ToString() == "00000000-0000-0000-0000-000000000000") return true; //Guid
        return false;
    }
}
