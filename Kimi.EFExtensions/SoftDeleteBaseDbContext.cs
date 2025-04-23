// ***********************************************************************
// Author           : Kama Zheng
// Created          : 01/13/2025
// ***********************************************************************

using Kimi.EFExtensions.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Kimi.EFExtensions
{
    /// <summary>
    /// Represents the database context for MlxBase.
    /// </summary>
    public class SoftDeleteBaseDbContext : DbContext
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SoftDeleteBaseDbContext"/> class.
        /// </summary>
        /// <param name="options">The options<see cref="DbContextOptions"/>.</param>
        public SoftDeleteBaseDbContext(DbContextOptions options) : base(options)
        {
        }

        #endregion

        #region Methods

        /// <summary>
        /// The DbContextBaseSaveChangesAsync.
        /// </summary>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="int"/>.</returns>
        protected async Task<int> DbContextBaseSaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// The SaveChanges.
        /// </summary>
        /// <returns>The <see cref="int"/>.</returns>
        public override int SaveChanges()
        {
            throw new NotSupportedException("Please use await SaveChangesAsync(userName)");
        }

        /// <summary>
        /// The SaveChangesAsync.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The <see cref="int"/>.</returns>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Please use await SaveChangesAsync(userName)");
        }

        /// <summary>
        /// Saves all changes made in this context to the database asynchronously.
        /// </summary>
        /// <param name="userName">The user name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous save operation. The task result contains the number of state entries written to the database.</returns>
        public virtual async Task<int> SaveChangesAsync(string userName, CancellationToken cancellationToken = new CancellationToken())
        {
            ChangeTracker.DetectChanges();
            SoftDelete(userName);
            int result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Configures the model that was discovered by convention from the entity types exposed in <see cref="DbSet{TEntity}"/> properties on the derived context.
        /// </summary>
        /// <param name="modelBuilder">The builder being used to construct the model for this context.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AppendGlobalQueryFilter<ISoftDeleteEntity>(s => s.Active);
        }

        /// <summary>
        /// Soft deletes the entities that are marked for deletion or updates the entities that are modified or added.
        /// </summary>
        /// <param name="userName">The user name.</param>
        protected void SoftDelete(string userName)
        {
            foreach (var entry in ChangeTracker.Entries<ISoftDeleteEntity>().ToList())
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.Entity.Active = false;
                    entry.State = EntityState.Modified;
                    entry.Entity.Updated = DateTime.UtcNow;
                    entry.Entity.Updatedby = userName;
                }
                else if ((entry.State == EntityState.Modified || entry.State == EntityState.Added) &&
                    entry.Properties.Any(p => p.IsModified
                    && !p.Metadata.Name.Equals(nameof(ISoftDeleteEntity.Updated), StringComparison.CurrentCultureIgnoreCase)))
                {
                    entry.Entity.Active = true;
                    entry.Entity.Updated = DateTime.UtcNow;
                    entry.Entity.Updatedby = userName;
                }
            }
        }

        #endregion
    }
}
