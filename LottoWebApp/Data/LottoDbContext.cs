using LottoWebApp.Models;
using Microsoft.EntityFrameworkCore;

namespace LottoWebApp.Data
{
    public class LottoDbContext : DbContext
    {
        public LottoDbContext(DbContextOptions<LottoDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<UserEncryptionKey> UserEncryptionKeys { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<AdminEncryptionKey> AdminEncryptionKeys { get; set; }
        public DbSet<Lottery1224BY> Lottery1224BY { get; set; }
        public DbSet<Lottery536BY> Lottery536BY { get; set; }
        public DbSet<Lottery649BY> Lottery649BY { get; set; }
        public DbSet<LotteryBlitzBY> LotteryBlitzBY { get; set; }
        public DbSet<LotteryKenoBY> LotteryKenoBY { get; set; }
        public DbSet<Lottery320BY> Lottery320BY { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ===== USERS =====
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(u => u.Id);

                entity.Property(u => u.Login).HasMaxLength(255).IsRequired();
                entity.Property(u => u.Password).HasMaxLength(512).IsRequired();
                entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
                entity.Property(u => u.Phone).HasMaxLength(100);
                entity.Property(u => u.Activity).IsRequired();

                entity.HasOne(u => u.EncryptionKey)
                      .WithOne(k => k.User)
                      .HasForeignKey<UserEncryptionKey>(k => k.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== ADMINS =====
            modelBuilder.Entity<Admin>(entity =>
            {
                entity.ToTable("Admins");
                entity.HasKey(a => a.Id);

                entity.Property(a => a.Login).HasMaxLength(255).IsRequired();
                entity.Property(a => a.Password).HasMaxLength(512).IsRequired();
                entity.Property(a => a.Email).HasMaxLength(255).IsRequired();
                entity.Property(a => a.Phone).HasMaxLength(100);
                entity.Property(a => a.Activity).IsRequired();

                entity.HasOne(a => a.EncryptionKey)
                      .WithOne(k => k.Admin)
                      .HasForeignKey<AdminEncryptionKey>(k => k.AdminId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ===== USER ENCRYPTION KEYS =====
            modelBuilder.Entity<UserEncryptionKey>(entity =>
            {
                entity.ToTable("UserEncryptionKeys");
                entity.HasKey(k => k.Id);

                entity.Property(k => k.EncryptionKey).HasMaxLength(512).IsRequired();
                entity.Property(k => k.IV).HasMaxLength(256).IsRequired();
            });

            // ===== ADMIN ENCRYPTION KEYS =====
            modelBuilder.Entity<AdminEncryptionKey>(entity =>
            {
                entity.ToTable("AdminEncryptionKeys");
                entity.HasKey(k => k.Id);

                entity.Property(k => k.EncryptionKey).HasMaxLength(512).IsRequired();
                entity.Property(k => k.IV).HasMaxLength(256).IsRequired();
            });

            // ===== ЛОТЕРЕИ =====
            modelBuilder.Entity<Lottery1224BY>(entity =>
            {
                entity.ToTable("1224BY");
                entity.HasKey(e => e.Draw);
            });

            modelBuilder.Entity<Lottery536BY>(entity =>
            {
                entity.ToTable("536BY");
                entity.HasKey(e => e.Draw);
            });

            modelBuilder.Entity<Lottery649BY>(entity =>
            {
                entity.ToTable("649BY");
                entity.HasKey(e => e.Draw);
            });

            modelBuilder.Entity<LotteryBlitzBY>(entity =>
            {
                entity.ToTable("BLITZBY");
                entity.HasKey(e => e.Draw);
            });

            modelBuilder.Entity<LotteryKenoBY>(entity =>
            {
                entity.ToTable("KENOBY");
                entity.HasKey(e => e.Draw);
            });
            modelBuilder.Entity<Lottery320BY>(entity =>
            {
                entity.ToTable("320BY");
                entity.HasKey(e => e.Draw);
            });

        }
    }
}
