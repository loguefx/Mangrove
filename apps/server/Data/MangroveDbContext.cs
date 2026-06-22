using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Data;

public class MangroveDbContext : DbContext
{
    public MangroveDbContext(DbContextOptions<MangroveDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<LibraryPath> LibraryPaths => Set<LibraryPath>();
    public DbSet<LibraryAccess> LibraryAccess => Set<LibraryAccess>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<MangaFile> MangaFiles => Set<MangaFile>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<JobLog> JobLogs => Set<JobLog>();
    public DbSet<AgeRestriction> AgeRestrictions => Set<AgeRestriction>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();
    public DbSet<ReadingList> ReadingLists => Set<ReadingList>();
    public DbSet<ReadingListItem> ReadingListItems => Set<ReadingListItem>();
    public DbSet<WantToRead> WantToRead => Set<WantToRead>();
    public DbSet<SeriesReview> SeriesReviews => Set<SeriesReview>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Username).IsUnique();

        b.Entity<UserRole>().HasKey(x => new { x.UserId, x.RoleId });
        b.Entity<UserRole>()
            .HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId);
        b.Entity<UserRole>()
            .HasOne(x => x.Role).WithMany(r => r.UserRoles).HasForeignKey(x => x.RoleId);

        b.Entity<Role>().HasIndex(r => r.Type).IsUnique();

        b.Entity<LibraryAccess>().HasKey(x => new { x.UserId, x.LibraryId });
        b.Entity<LibraryAccess>()
            .HasOne(x => x.User).WithMany(u => u.LibraryAccess).HasForeignKey(x => x.UserId);
        b.Entity<LibraryAccess>()
            .HasOne(x => x.Library).WithMany(l => l.LibraryAccess).HasForeignKey(x => x.LibraryId);

        b.Entity<Library>()
            .HasOne(l => l.Credential).WithMany(c => c.Libraries)
            .HasForeignKey(l => l.CredentialId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<LibraryPath>()
            .HasOne(p => p.Library).WithMany(l => l.Paths)
            .HasForeignKey(p => p.LibraryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<LibraryPath>()
            .HasOne(p => p.Credential).WithMany()
            .HasForeignKey(p => p.CredentialId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Series>().HasIndex(s => new { s.LibraryId, s.Name });
        b.Entity<Series>()
            .HasOne(s => s.Library).WithMany(l => l.Series)
            .HasForeignKey(s => s.LibraryId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Volume>()
            .HasOne(v => v.Series).WithMany(s => s.Volumes)
            .HasForeignKey(v => v.SeriesId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Chapter>()
            .HasOne(c => c.Volume).WithMany(v => v.Chapters)
            .HasForeignKey(c => c.VolumeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<MangaFile>()
            .HasOne(f => f.Chapter).WithMany(c => c.Files)
            .HasForeignKey(f => f.ChapterId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MangaFile>().HasIndex(f => f.StoragePath);

        b.Entity<ReadingProgress>().HasIndex(p => new { p.UserId, p.ChapterId }).IsUnique();
        b.Entity<ReadingProgress>()
            .HasOne(p => p.User).WithMany(u => u.ReadingProgress)
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReadingProgress>()
            .HasOne(p => p.Chapter).WithMany(c => c.ReadingProgress)
            .HasForeignKey(p => p.ChapterId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RefreshToken>().HasIndex(t => t.TokenHash);
        b.Entity<RefreshToken>()
            .HasOne(t => t.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<AppSetting>().HasKey(s => s.Key);

        b.Entity<UserPreference>().HasIndex(p => new { p.UserId, p.Key }).IsUnique();
        b.Entity<UserPreference>()
            .HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        // ---- Phase 3: organize + users ----
        b.Entity<AgeRestriction>().HasIndex(a => a.UserId).IsUnique();
        b.Entity<AgeRestriction>()
            .HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Collection>()
            .HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CollectionItem>()
            .HasOne(i => i.Collection).WithMany(c => c.Items).HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CollectionItem>()
            .HasOne(i => i.Series).WithMany().HasForeignKey(i => i.SeriesId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CollectionItem>().HasIndex(i => new { i.CollectionId, i.SeriesId }).IsUnique();

        b.Entity<ReadingList>()
            .HasOne(r => r.Owner).WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReadingListItem>()
            .HasOne(i => i.ReadingList).WithMany(r => r.Items).HasForeignKey(i => i.ReadingListId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ReadingListItem>()
            .HasOne(i => i.Chapter).WithMany().HasForeignKey(i => i.ChapterId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<WantToRead>().HasIndex(w => new { w.UserId, w.SeriesId }).IsUnique();
        b.Entity<WantToRead>()
            .HasOne(w => w.User).WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WantToRead>()
            .HasOne(w => w.Series).WithMany().HasForeignKey(w => w.SeriesId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<SeriesReview>().HasIndex(r => new { r.UserId, r.SeriesId }).IsUnique();
        b.Entity<SeriesReview>()
            .HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SeriesReview>()
            .HasOne(r => r.Series).WithMany().HasForeignKey(r => r.SeriesId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Bookmark>()
            .HasOne(bk => bk.User).WithMany().HasForeignKey(bk => bk.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Bookmark>()
            .HasOne(bk => bk.Chapter).WithMany().HasForeignKey(bk => bk.ChapterId).OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(b);
    }
}
