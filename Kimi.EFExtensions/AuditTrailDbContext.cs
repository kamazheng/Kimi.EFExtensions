// ***********************************************************************
// Author           : Kama Zheng
// Created          : 03/17/2025
// ***********************************************************************

using Kimi.EFExtensions.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Kimi.EFExtensions
{
    /// <summary>
    /// Defines the <see cref="AuditTrailDbContext" />.
    /// </summary>
    public class AuditTrailDbContext : SoftDeleteBaseDbContext
    {
        #region Constants

        /// <summary>
        /// Defines the ProxyNameSpace.
        /// </summary>
        internal const string ProxyNameSpace = "Castle.Proxies";

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="AuditTrailDbContext"/> class.
        /// </summary>
        public AuditTrailDbContext() : base()
        {
        }

        #endregion

        #region Properties

        public DbSet<Trail> AuditTrails => Set<Trail>();

        #endregion

        #region Methods

        /// <summary>
        /// The SaveChangesAsync.
        /// </summary>
        /// <param name="userName">The userName<see cref="string"/>.</param>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="int"/>.</returns>
        public override async Task<int> SaveChangesAsync(string userName, CancellationToken cancellationToken = new CancellationToken())
        {
            ChangeTracker.DetectChanges();
            SoftDelete(userName);
            var auditEntries = HandleAuditingBeforeSaveChanges(userName);
            int result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await HandleAuditingAfterSaveChangesAsync(auditEntries, cancellationToken);
            return result;
        }

        /// <summary>
        /// The HandleAuditingAfterSaveChangesAsync.
        /// </summary>
        /// <param name="trailEntries">The trailEntries<see cref="List{AuditTrail}"/>.</param>
        /// <param name="cancellationToken">The cancellationToken<see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private Task HandleAuditingAfterSaveChangesAsync(List<AuditTrail> trailEntries, CancellationToken cancellationToken = new())
        {
            if (trailEntries == null || trailEntries.Count == 0)
            {
                return Task.CompletedTask;
            }

            foreach (var entry in trailEntries)
            {
                foreach (var prop in entry.TemporaryProperties)
                {
                    if (prop.Metadata.IsPrimaryKey())
                    {
                        entry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    else
                    {
                        entry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }

                AuditTrails.Add(entry.ToAuditTrail());
            }
            //Important to call base method, otherwise, will trigger again.
            return base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// The HandleAuditingBeforeSaveChanges.
        /// </summary>
        /// <param name="userId">The userId<see cref="string"/>.</param>
        /// <returns>The <see cref="List{AuditTrail}"/>.</returns>
        private List<AuditTrail> HandleAuditingBeforeSaveChanges(string userId)
        {
            foreach (var entry in ChangeTracker.Entries<IAuditableEntity>().ToList())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.CreatedOn = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Property(x => x.CreatedBy).IsModified = false;
                    entry.Property(x => x.CreatedOn).IsModified = false;
                }
            }

            ChangeTracker.DetectChanges();

            var trailEntries = new List<AuditTrail>();
            foreach (var entry in ChangeTracker.Entries<IAuditableEntity>()
                .Where(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
                .ToList())
            {
                var tableType = entry.Entity.GetType();
                if (tableType.Namespace == ProxyNameSpace)
                {
                    tableType = tableType.BaseType;
                }
                var tableName = tableType?.Name;

                var trailEntry = new AuditTrail(entry)
                {
                    TableName = tableName,
                    UserId = userId
                };
                trailEntries.Add(trailEntry);
                foreach (var property in entry.Properties)
                {
                    if (property.IsTemporary)
                    {
                        trailEntry.TemporaryProperties.Add(property);
                        continue;
                    }

                    string propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        trailEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            trailEntry.TrailType = TrailType.Create;
                            trailEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            trailEntry.TrailType = TrailType.Delete;
                            trailEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (propertyName == nameof(IAuditableEntity.CreatedOn))
                            {
                                property.CurrentValue = property.OriginalValue;
                            }
                            if (property.IsModified && entry.Entity is ISoftDeleteEntity
                                && propertyName == nameof(ISoftDeleteEntity.Active) && Convert.ToBoolean(property.CurrentValue) == false)
                            {
                                trailEntry.ChangedColumns.Add(propertyName);
                                trailEntry.TrailType = TrailType.Delete;
                                trailEntry.OldValues[propertyName] = property.OriginalValue;
                                trailEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            else if (property.IsModified && property.OriginalValue?.Equals(property.CurrentValue) == false)
                            {
                                trailEntry.ChangedColumns.Add(propertyName);
                                if (trailEntry.TrailType == default) trailEntry.TrailType = TrailType.Update;
                                trailEntry.OldValues[propertyName] = property.OriginalValue;
                                trailEntry.NewValues[propertyName] = property.CurrentValue;
                            }

                            break;
                    }
                }
            }

            foreach (var auditEntry in trailEntries.Where(e => !e.HasTemporaryProperties))
            {
                AuditTrails.Add(auditEntry.ToAuditTrail());
            }

            return trailEntries.Where(e => e.HasTemporaryProperties).ToList();
        }

        #endregion
    }
}
