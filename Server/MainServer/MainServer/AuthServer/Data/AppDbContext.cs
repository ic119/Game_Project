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
        public DbSet<CharacterSlot> CharacterSlots => Set<CharacterSlot>();
        public DbSet<CharacterItem> CharacterItems => Set<CharacterItem>();

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
                entity.HasIndex(c => c.UserId); // 계정당 여러 캐릭터 허용(개수 제한은 CharacterSlot.MaxSlotCount가 담당), 조회 성능을 위한 비유니크 인덱스만 유지
                entity.HasOne(c => c.User)
                      .WithMany(u => u.Characters)
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            _modelBuilder.Entity<CharacterSlot>(entity =>
            {
                entity.ToTable("character_slots");
                entity.HasKey(cs => cs.UserId);
                entity.HasOne(cs => cs.User)
                      .WithOne(u => u.CharacterSlot)
                      .HasForeignKey<CharacterSlot>(cs => cs.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            _modelBuilder.Entity<CharacterItem>(entity =>
            {
                entity.ToTable("character_items");
                // 캐릭터당 같은 ItemId를 한 행으로만 유지(스택형 인벤토리) - 처치 보상 적용 시
                // 이 인덱스로 기존 행을 찾아 수량만 증가시키고, 없으면 새로 만든다.
                entity.HasIndex(ci => new { ci.CharacterId, ci.ItemId }).IsUnique();
                entity.HasOne(ci => ci.Character)
                      .WithMany()
                      .HasForeignKey(ci => ci.CharacterId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}