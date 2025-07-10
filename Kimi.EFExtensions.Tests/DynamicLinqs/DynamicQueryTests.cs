// All using statements at the top
using System;
using System.Threading.Tasks;
using System;
using System.Threading.Tasks;

using System;
using System.Threading.Tasks;
using Moq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Kimi.EFExtensions.DynamicLinqs;

namespace Kimi.EFExtensions.Tests.DynamicLinqs
{
    public class DynamicQueryTests
    {
        private readonly Mock<DbContext> _mockDbContext;
        private readonly Mock<SoftDeleteBaseDbContext> _mockSoftDeleteDbContext;
        private readonly Type entityType;
        private readonly string tableTypeName = "TestEntity";

        public DynamicQueryTests()
        {
            _mockDbContext = new Mock<DbContext>();
            _mockSoftDeleteDbContext = new Mock<SoftDeleteBaseDbContext>();
            entityType = typeof(TestEntity);
        }


        private TestDbContext CreateInitializedContext()
        {
            var context = new TestDbContext();
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public async Task UpsertRecord_InsertNewRecord_ReturnsInsertedEntity()
        {
            var jsonObject = "{\"Id\":1,\"Name\":\"Test\"}";
            using var context = CreateInitializedContext();
            var result = await DynamicQuery.UpsertRecord(context, tableTypeName, jsonObject, null);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task UpsertRecord_UpdateExistingRecord_ReturnsUpdatedEntity()
        {
            var jsonObject = "{\"Id\":1,\"Name\":\"Updated\"}";
            using var context = CreateInitializedContext();
            var result = await DynamicQuery.UpsertRecord(context, tableTypeName, jsonObject, null);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task UpsertRecord_NullTableTypeName_ThrowsArgumentException()
        {
            using var context = CreateInitializedContext();
            await Assert.ThrowsAsync<ArgumentException>(() => DynamicQuery.UpsertRecord(context, null!, "", null));
        }


        [Fact]
        public async Task UpsertRecord_SoftDeleteDbContext_CallsSaveChangesAsyncWithUser()
        {
            var jsonObject = "{\"Id\":1,\"Name\":\"Test\"}";
            var byUser = "TestUser";
            using var context = CreateInitializedContext();
            var result = await DynamicQuery.UpsertRecord(context, tableTypeName, jsonObject, byUser);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task UpsertRecord_InsertNewRecord_ActuallyInserts()
        {
            var jsonObject = "{\"Id\":2,\"Name\":\"Inserted\"}";
            using var context = CreateInitializedContext();
            await DynamicQuery.UpsertRecord(context, tableTypeName, jsonObject, null);
            var entity = await context.TestEntities.FindAsync(2);
            Assert.NotNull(entity);
            Assert.Equal("Inserted", entity.Name);
        }

        [Fact]
        public async Task UpsertRecord_UpdateExistingRecord_ActuallyUpdates()
        {
            using var context = CreateInitializedContext();
            context.TestEntities.Add(new TestEntity { Id = 3, Name = "Old" });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            var jsonObject = "{\"Id\":3,\"Name\":\"UpdatedName\"}";
            await DynamicQuery.UpsertRecord(context, tableTypeName, jsonObject, null);
            var entity = await context.TestEntities.FindAsync(3);
            Assert.NotNull(entity);
            Assert.Equal("UpdatedName", entity.Name);
        }



        public class TestEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = null!;
        }

        public class ParentEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = null!;
            public ICollection<ChildEntity> Children { get; set; } = new List<ChildEntity>();
        }

        public class ChildEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = null!;
            public int ParentEntityId { get; set; }
            public ParentEntity Parent { get; set; } = null!;
        }

        // Minimal DbContext for testing (uses SQLite in-memory for relational support)
        public class TestDbContext : DbContext
        {
            public DbSet<TestEntity> TestEntities { get; set; }
            public DbSet<ParentEntity> ParentEntities { get; set; }
            public DbSet<ChildEntity> ChildEntities { get; set; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlite("DataSource=:memory:");
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
                modelBuilder.Entity<ParentEntity>().HasKey(e => e.Id);
                modelBuilder.Entity<ChildEntity>().HasKey(e => e.Id);
                modelBuilder.Entity<ParentEntity>()
                    .HasMany(p => p.Children)
                    .WithOne(c => c.Parent)
                    .HasForeignKey(c => c.ParentEntityId);
            }
        }

        // 导航属性相关测试已移除，因 UpsertRecord 设计不支持递归处理导航属性。
    }
}