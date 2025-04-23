using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kimi.EFExtensions
{
    /// <summary>
    /// Provides extension methods for automatic database migration.
    /// </summary>
    public static class AutoMigration
    {
        /// <summary>
        /// Migrates the database to the latest version for the specified DbContext type.
        /// </summary>
        /// <typeparam name="T">The type of the DbContext.</typeparam>
        /// <param name="service">The IServiceProvider instance.</param>
        /// <param name="onlyTrustConnection">A flag indicating whether to only work a trusted connection. Default is true.</param>
        public static void DbMigrate<T>(this IServiceProvider service, bool onlyTrustConnection = true) where T : DbContext
        {
            using var scope = service.CreateScope();
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<T>();
            var connectString = dbContext.Database.GetConnectionString();
            if (string.IsNullOrEmpty(connectString))
            {
                throw new InvalidOperationException("Connection string is not properly configured.");
            }
            var pendingMigrations = dbContext.Database.GetPendingMigrations();
            if (pendingMigrations.Any() && (!onlyTrustConnection || connectString.IsTrustedConnection()))
            {
                dbContext.Database.Migrate();
            }
        }

        /// <summary>
        /// Checks if the connection string is a trusted connection.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>True if the connection string is a trusted connection, otherwise false.</returns>
        private static bool IsTrustedConnection(this string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            return builder.IntegratedSecurity;
        }
    }
}
