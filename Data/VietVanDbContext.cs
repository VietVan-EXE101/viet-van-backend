using Microsoft.EntityFrameworkCore; using VietVan.API.Entities;
namespace VietVan.API.Data;
public class VietVanDbContext(DbContextOptions<VietVanDbContext> options):DbContext(options){ public DbSet<DramaProduction> DramaProductions => Set<DramaProduction>();
public DbSet<DramaScene> DramaScenes => Set<DramaScene>();
public DbSet<DramaAsset> DramaAssets => Set<DramaAsset>();
public DbSet<DramaHotspot> DramaHotspots => Set<DramaHotspot>();
public DbSet<CulturalInsightView> CulturalInsightViews => Set<CulturalInsightView>();
public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
public DbSet<PaymentBankTransaction> PaymentBankTransactions => Set<PaymentBankTransaction>();
public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
public DbSet<PaymentAuditLog> PaymentAuditLogs => Set<PaymentAuditLog>();
public DbSet<UserAuthState> UserAuthStates => Set<UserAuthState>();
public DbSet<EmailVerificationRecord> EmailVerificationTokens => Set<EmailVerificationRecord>();
public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
public DbSet<DramaManifest> DramaManifests => Set<DramaManifest>();
public DbSet<DramaViewing> DramaViewings => Set<DramaViewing>();
public DbSet<PasswordResetRecord> PasswordResetTokens => Set<PasswordResetRecord>();
public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
public DbSet<User> Users=>Set<User>(); public DbSet<SubscriptionPackage> SubscriptionPackages=>Set<SubscriptionPackage>(); public DbSet<UserSubscription> UserSubscriptions=>Set<UserSubscription>(); public DbSet<LiteraryWork> LiteraryWorks=>Set<LiteraryWork>(); public DbSet<RevenueTransaction> RevenueTransactions=>Set<RevenueTransaction>();
protected override void OnModelCreating(ModelBuilder b){
b.Entity<UserAvatar>(e=>{e.ToTable("UserAvatars");e.HasKey(x=>x.UserId);e.Property(x=>x.ObjectKey).HasMaxLength(600).IsRequired().IsConcurrencyToken();e.Property(x=>x.FileName).HasMaxLength(260).IsRequired();e.Property(x=>x.ContentType).HasMaxLength(100).IsRequired();e.HasOne<User>().WithOne().HasForeignKey<UserAvatar>(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);});
b.Entity<User>().Property(x=>x.FullName).HasMaxLength(150);
b.Entity<DramaManifest>(e=>{e.ToTable("DramaManifests");e.HasKey(x=>x.DramaProductionId);e.Property(x=>x.Revision).IsConcurrencyToken();e.HasOne<DramaProduction>().WithOne().HasForeignKey<DramaManifest>(x=>x.DramaProductionId).OnDelete(DeleteBehavior.NoAction);});
b.Entity<DramaViewing>(e=>{e.ToTable("DramaViewings");e.HasKey(x=>new{x.UserId,x.DramaProductionId});e.Property(x=>x.VersionType).HasMaxLength(20);e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.NoAction);e.HasOne<DramaProduction>().WithMany().HasForeignKey(x=>x.DramaProductionId).OnDelete(DeleteBehavior.NoAction);});
b.Entity<PasswordResetRecord>(e=>{e.ToTable("PasswordResetTokens");e.Property(x=>x.TokenHash).HasColumnType("binary(32)");e.Property(x=>x.EmailSnapshot).HasMaxLength(150);e.HasIndex(x=>x.TokenHash).IsUnique();e.HasIndex(x=>new{x.UserId,x.CreatedAt});e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.NoAction);});
b.Entity<AdminAuditLog>(e=>{e.ToTable("AdminAuditLogs");e.Property(x=>x.Action).HasMaxLength(80);e.HasIndex(x=>new{x.TargetUserId,x.CreatedAt});e.HasOne<User>().WithMany().HasForeignKey(x=>x.AdminUserId).OnDelete(DeleteBehavior.NoAction);e.HasOne<User>().WithMany().HasForeignKey(x=>x.TargetUserId).OnDelete(DeleteBehavior.NoAction);});
b.Entity<Review>(e=>{
    e.ToTable("Reviews", t=>{t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");t.HasCheckConstraint("CK_Reviews_Target", "([LiteraryWorkId] IS NOT NULL AND [DramaProductionId] IS NULL) OR ([LiteraryWorkId] IS NULL AND [DramaProductionId] IS NOT NULL)");t.HasCheckConstraint("CK_Reviews_Status", "[Status] IN ('Published','Hidden')");});
    e.Property(x=>x.Comment).HasMaxLength(2000).IsRequired();e.Property(x=>x.Status).HasMaxLength(16).IsRequired();
    e.HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne(x=>x.LiteraryWork).WithMany().HasForeignKey(x=>x.LiteraryWorkId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne(x=>x.DramaProduction).WithMany().HasForeignKey(x=>x.DramaProductionId).OnDelete(DeleteBehavior.NoAction);
    e.HasIndex(x=>new{x.UserId,x.LiteraryWorkId}).IsUnique().HasFilter("[LiteraryWorkId] IS NOT NULL");
    e.HasIndex(x=>new{x.UserId,x.DramaProductionId}).IsUnique().HasFilter("[DramaProductionId] IS NOT NULL");
    e.HasIndex(x=>new{x.LiteraryWorkId,x.Status,x.CreatedAt});e.HasIndex(x=>new{x.DramaProductionId,x.Status,x.CreatedAt});
});
b.Entity<ReviewModerationLog>(e=>{e.ToTable("ReviewModerationLogs");e.Property(x=>x.OldStatus).HasMaxLength(16).IsRequired();e.Property(x=>x.NewStatus).HasMaxLength(16).IsRequired();e.HasIndex(x=>new{x.ReviewId,x.CreatedAt});e.HasOne<User>().WithMany().HasForeignKey(x=>x.AdminUserId).OnDelete(DeleteBehavior.NoAction);});
b.Entity<UserAuthState>(e=>{e.ToTable("UserAuthStates");e.HasKey(x=>x.UserId);e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
b.Entity<EmailVerificationRecord>(e=>{e.ToTable("EmailVerificationTokens");e.Property(x=>x.EmailSnapshot).HasMaxLength(150).IsRequired();e.Property(x=>x.TokenHash).HasColumnType("binary(32)");e.HasIndex(x=>x.TokenHash).IsUnique();e.HasIndex(x=>new{x.UserId,x.CreatedAt});e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
b.Entity<UserExternalLogin>(e=>{e.ToTable("UserExternalLogins");e.Property(x=>x.Provider).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");e.Property(x=>x.ProviderSubject).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");e.HasIndex(x=>new{x.Provider,x.ProviderSubject}).IsUnique();e.HasIndex(x=>x.UserId);e.HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);});
b.Entity<CulturalInsightView>(e => { e.ToTable("CulturalInsightViews"); e.HasKey(x => new { x.UserId, x.DramaProductionId, x.DramaHotspotId }); });
// Module Drama riêng biệt: tránh tác động tới các entity đang hoạt động.
b.Entity<DramaProduction>(e => {
    e.ToTable("DramaProductions");
    e.Property(x => x.Title).HasMaxLength(250).IsRequired();
    e.Property(x => x.AccessLevel).HasMaxLength(30).IsRequired();
    e.Property(x => x.Status).HasMaxLength(16).IsRequired();
    e.HasIndex(x => new { x.Status, x.IsPublished });
    e.HasOne<DramaAsset>().WithMany().HasForeignKey(x=>x.FreeVideoAssetId).OnDelete(DeleteBehavior.NoAction);
    e.Property(x => x.Description).HasMaxLength(3000);
    e.HasOne(x => x.LiteraryWork).WithMany().HasForeignKey(x => x.LiteraryWorkId).OnDelete(DeleteBehavior.NoAction);
});
b.Entity<DramaAsset>(e => {
    e.ToTable("DramaAssets");
    e.Property(x => x.Kind).HasMaxLength(30).IsRequired();
    e.Property(x => x.Title).HasMaxLength(200).IsRequired();
    e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
    e.Property(x => x.RelativePath).HasMaxLength(600).IsRequired();
    e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
    e.Property(x=>x.NameVi).HasMaxLength(200);e.Property(x=>x.NameEn).HasMaxLength(200);
    e.HasOne(x => x.DramaProduction).WithMany(x => x.Assets).HasForeignKey(x => x.DramaProductionId).OnDelete(DeleteBehavior.Cascade);
    e.HasIndex(x => new { x.DramaProductionId, x.Kind });
});
b.Entity<DramaScene>(e => {
    e.ToTable("DramaScenes");
    e.Property(x => x.SceneKey).HasMaxLength(80).IsRequired();
    e.Property(x => x.Title).HasMaxLength(250).IsRequired();
    e.HasOne(x => x.DramaProduction).WithMany(x => x.Scenes).HasForeignKey(x => x.DramaProductionId).OnDelete(DeleteBehavior.Cascade);
    e.HasOne(x => x.BackgroundAsset).WithMany().HasForeignKey(x => x.BackgroundAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne(x => x.AudioAsset).WithMany().HasForeignKey(x => x.AudioAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne(x => x.VideoAsset).WithMany().HasForeignKey(x => x.VideoAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne<DramaAsset>().WithMany().HasForeignKey(x => x.PreviewAudioAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasOne<DramaAsset>().WithMany().HasForeignKey(x => x.PreviewVideoAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasIndex(x => new { x.DramaProductionId, x.SceneKey }).IsUnique();
    e.HasIndex(x => new { x.DramaProductionId, x.SortOrder });
});
b.Entity<DramaHotspot>(e => {
    e.ToTable("DramaHotspots");
    e.Property(x => x.Kind).HasMaxLength(30).IsRequired();
    e.Property(x => x.NameVi).HasMaxLength(200).IsRequired();
    e.Property(x => x.NameEn).HasMaxLength(200);
    e.Property(x => x.MeaningVi).HasColumnType("nvarchar(max)").IsRequired();
    e.Property(x => x.MeaningEn).HasColumnType("nvarchar(max)");
    e.Property(x => x.ActionKey).HasMaxLength(120);
    e.Property(x => x.TargetSegmentIdsJson).HasColumnType("nvarchar(max)").IsRequired();
    e.HasOne(x => x.DramaScene).WithMany(x => x.Hotspots).HasForeignKey(x => x.DramaSceneId).OnDelete(DeleteBehavior.Cascade);
    e.HasOne(x => x.VisualAsset).WithMany().HasForeignKey(x => x.VisualAssetId).OnDelete(DeleteBehavior.NoAction);
    e.HasIndex(x => new { x.DramaSceneId, x.ZIndex });
});
b.Entity<User>(e=>{e.HasIndex(x=>x.Username).IsUnique();e.HasIndex(x=>x.Email).IsUnique();e.Property(x=>x.Username).HasMaxLength(50).IsRequired();e.Property(x=>x.Email).HasMaxLength(150).IsRequired();e.Property(x=>x.Role).HasMaxLength(20).IsRequired();});
b.Entity<SubscriptionPackage>(e=>{e.Property(x=>x.Price).HasColumnType("decimal(18,2)");e.Property(x=>x.Name).HasMaxLength(100);e.Property(x=>x.Code).HasMaxLength(32);e.Property(x=>x.BillingPeriod).HasMaxLength(16);e.HasIndex(x=>x.Code).IsUnique().HasFilter("[Code] IS NOT NULL");e.Property(x=>x.Description).HasMaxLength(1000);e.Property(x=>x.Benefits).HasMaxLength(3000);e.HasIndex(x=>x.TierLevel);});
b.Entity<UserSubscription>(e=>{e.HasOne(x=>x.User).WithMany(x=>x.Subscriptions).HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);e.HasOne(x=>x.Package).WithMany(x=>x.UserSubscriptions).HasForeignKey(x=>x.PackageId).OnDelete(DeleteBehavior.Restrict);e.HasIndex(x=>new{x.UserId,x.IsActive});e.HasIndex(x=>x.ActivationPaymentOrderId).IsUnique().HasFilter("[ActivationPaymentOrderId] IS NOT NULL");});
b.Entity<LiteraryWork>(e=>{e.Property(x=>x.Title).HasMaxLength(250).IsRequired();e.Property(x=>x.Author).HasMaxLength(150).IsRequired();e.Property(x=>x.Content).HasColumnType("nvarchar(max)");e.HasIndex(x=>new{x.IsActive,x.RequiredTierLevel});});
b.Entity<PaymentOrder>(e=>{
    e.ToTable("PaymentOrders");e.Property(x=>x.OrderCode).HasMaxLength(30).IsRequired();
    e.Property(x=>x.IdempotencyKey).HasMaxLength(100).IsRequired();
    e.Property(x=>x.PackageNameSnapshot).HasMaxLength(100).IsRequired();
    e.Property(x=>x.BillingPeriodSnapshot).HasMaxLength(16);
    e.Property(x=>x.Amount).HasColumnType("decimal(18,2)");
    e.Property(x=>x.Currency).HasMaxLength(3);e.Property(x=>x.Provider).HasMaxLength(20);
    e.Property(x=>x.Status).HasMaxLength(25);e.Property(x=>x.ReviewReason).HasMaxLength(500);
    e.HasIndex(x=>x.OrderCode).IsUnique();e.HasIndex(x=>new{x.UserId,x.IdempotencyKey}).IsUnique();
    e.HasIndex(x=>new{x.Status,x.ExpiresAt});
    e.HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
    e.HasOne(x=>x.Package).WithMany().HasForeignKey(x=>x.PackageId).OnDelete(DeleteBehavior.Restrict);
});
b.Entity<PaymentBankTransaction>(e=>{
    e.ToTable("PaymentBankTransactions");e.Property(x=>x.Provider).HasMaxLength(20);
    e.Property(x=>x.ProviderTransactionId).HasMaxLength(100);
    e.Property(x=>x.Amount).HasColumnType("decimal(18,2)");
    e.Property(x=>x.Currency).HasMaxLength(3);e.Property(x=>x.Status).HasMaxLength(25);
    e.Property(x=>x.ReviewReason).HasMaxLength(500);e.Property(x=>x.AccountNumberMasked).HasMaxLength(30);
    e.Property(x=>x.ClaimedOrderCode).HasMaxLength(30);
    e.HasIndex(x=>new{x.Provider,x.ProviderTransactionId}).IsUnique();
    e.HasOne(x=>x.PaymentOrder).WithMany(x=>x.BankTransactions).HasForeignKey(x=>x.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
});
b.Entity<PaymentWebhookEvent>(e=>{
    e.ToTable("PaymentWebhookEvents");e.Property(x=>x.Provider).HasMaxLength(20);
    e.Property(x=>x.ProviderEventId).HasMaxLength(100);e.Property(x=>x.PayloadHash).HasMaxLength(64);
    e.Property(x=>x.Outcome).HasMaxLength(100);
    e.HasIndex(x=>new{x.Provider,x.ProviderEventId}).IsUnique();
});
b.Entity<PaymentAuditLog>(e=>{
    e.ToTable("PaymentAuditLogs");e.Property(x=>x.Action).HasMaxLength(100);e.Property(x=>x.Detail).HasMaxLength(1000);
    e.HasOne(x=>x.PaymentOrder).WithMany().HasForeignKey(x=>x.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
    e.HasIndex(x=>new{x.PaymentOrderId,x.CreatedAt});
});
b.Entity<RevenueTransaction>(e=>{e.Property(x=>x.Amount).HasColumnType("decimal(18,2)");e.Property(x=>x.Status).HasMaxLength(20);e.HasOne(x=>x.User).WithMany(x=>x.Transactions).HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);e.HasOne(x=>x.Package).WithMany(x=>x.Transactions).HasForeignKey(x=>x.PackageId).OnDelete(DeleteBehavior.Restrict);e.HasIndex(x=>new{x.Status,x.PaymentDate});});}}
