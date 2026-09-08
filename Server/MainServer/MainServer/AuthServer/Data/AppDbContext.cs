using MainServer.AuthServer.Entities;
using MainServer.CharacterServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace MainServer.AuthServer.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> _options) : base(_options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<Character> Characters => Set<Character>();

        protected override void OnModelCreating(ModelBuilder _modelBuilder)
        {
            _modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasIndex(u => u.Username).IsUnique();
            });

            _modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("refresh_tokens");
                entity.HasIndex(rt => rt.Token).IsUnique();
                entity.HasOne(rt => rt.User)
                      .WithMany()
                      .HasForeignKey(rt => rt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            _modelBuilder.Entity<Character>(entity =>
            {
                entity.ToTable("characters");
                entity.HasIndex(c => c.UserId).IsUnique(); // 계정당 캐릭터 1개
                entity.HasOne(c => c.User)
                      .WithOne()
                      .HasForeignKey<Character>(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}