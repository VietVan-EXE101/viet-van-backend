using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietVan.API.Data;
using VietVan.API.DTOs.Payments;
using VietVan.API.Entities;

namespace VietVan.API.Services.Payments;

public sealed class PaymentService(VietVanDbContext db, IPaymentGateway gateway,
    IOptions<SePayOptions> settings, EntitlementService entitlements)
{
    private readonly SePayOptions options = settings.Value;

    public async Task<List<PackageDto>> PackagesAsync(bool admin = false)
    {
        try
        {
            return await db.SubscriptionPackages.AsNoTracking()
            .Where(p => admin || p.IsActive)
            .OrderBy(p => p.TierLevel).ThenBy(p => p.Price)
            .Select(p => new PackageDto(p.Id,p.Name,p.Description,p.Price,p.DurationInDays,
                p.TierLevel,p.Benefits,p.IsActive,p.Code,p.BillingPeriod)).ToListAsync();
        }
        // Danh sách chỉ đọc không có xung đột trạng thái nghiệp vụ.
        // EF có thể bọc lỗi provider trong InvalidOperationException: giữ nguyên cause để phân loại SQL.
        catch (InvalidOperationException ex) { throw new SubscriptionPackagesReadException(ex); }
    }

    public async Task<PackageDto> SavePackageAsync(int? id, EditPackageRequest input)
    {
        if (input.TierLevel == 0 && input.Price != 0) throw new ArgumentException("Gói Free phải có giá 0.");
        if (input.TierLevel > 0 && (input.Price <= 0 || input.Price != decimal.Truncate(input.Price)))
            throw new ArgumentException("Giá VIP phải là số nguyên VND dương.");
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Tên gói không được trống.");
        SubscriptionPackage p;
        if (id is null) { p = new SubscriptionPackage(); db.SubscriptionPackages.Add(p); }
        else p = await db.SubscriptionPackages.FindAsync(id.Value)
            ?? throw new KeyNotFoundException("Không tìm thấy gói.");
        if (p.Code is not null && (p.TierLevel != input.TierLevel || p.DurationInDays != input.DurationInDays))
            throw new ArgumentException("Không thể thay đổi tier hoặc thời hạn của gói chuẩn.");
        p.Name = input.Name.Trim();p.Description=(input.Description ?? "").Trim();p.Benefits=(input.Benefits ?? "").Trim();
        p.Price=input.Price;p.DurationInDays=input.DurationInDays;p.TierLevel=input.TierLevel;
        p.IsActive=input.IsActive;p.UpdatedAt=DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new PackageDto(p.Id,p.Name,p.Description,p.Price,p.DurationInDays,p.TierLevel,p.Benefits,p.IsActive,p.Code,p.BillingPeriod);
    }

    public async Task<object> MySubscriptionAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var current = await db.UserSubscriptions.AsNoTracking().Where(s => s.UserId == userId)
            .OrderByDescending(s=>s.EndDate)
            .Select(s => new { s.Id, s.PackageId, PackageName=s.Package.Name,
                TierLevel = s.GrantedTierLevel ?? s.Package.TierLevel, s.StartDate, s.EndDate, s.IsActive }).ToListAsync();
        var tier = await entitlements.CurrentTierAsync(userId);
        var until = await entitlements.VipUntilAsync(userId);
        return new { TierLevel=tier, TierName=EntitlementService.Policy(tier).TierName,
            VipUntil=until.HasValue ? Utc(until.Value) : (DateTime?)null, IsVip=tier>0,
            Subscriptions=current.Select(s=>new { s.Id,s.PackageId,s.PackageName,s.TierLevel,
                StartDate=Utc(s.StartDate),EndDate=Utc(s.EndDate),
                ActiveNow=s.IsActive && s.StartDate<=now && s.EndDate>now }) };
    }

    public async Task<PaymentOrderDto> CreateOrderAsync(int userId, int packageId, string idemKey)
    {
        if (string.IsNullOrWhiteSpace(idemKey) || idemKey.Length is < 8 or > 100 ||
            idemKey.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Cần header Idempotency-Key (8-100 ký tự chữ/số/-/_).");
        if (options.OrderLifetimeMinutes is < 5 or > 120)
            throw new InvalidOperationException("OrderLifetimeMinutes phải từ 5 đến 120.");
        var existing = await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(x=>x.UserId==userId && x.IdempotencyKey==idemKey);
        if (existing is not null)
        {
            if(existing.PackageId!=packageId) throw new InvalidOperationException("Idempotency-Key đã được dùng cho gói khác.");
            return ToDto(existing);
        }
        var pkg=await db.SubscriptionPackages.AsNoTracking()
            .FirstOrDefaultAsync(x=>x.Id==packageId && x.IsActive)
            ?? throw new ArgumentException("Gói không tồn tại hoặc đã ngừng bán.");
        if (pkg.TierLevel<=0 || pkg.Price<=0 || pkg.Price!=decimal.Truncate(pkg.Price) || pkg.DurationInDays<=0)
            throw new ArgumentException("Gói Free không cần thanh toán hoặc cấu hình gói không hợp lệ.");
        var now=DateTime.UtcNow;
        var order=new PaymentOrder {
            OrderCode="VV"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            UserId=userId,PackageId=pkg.Id,PackageNameSnapshot=pkg.Name,Amount=pkg.Price,
            DurationDaysSnapshot=pkg.DurationInDays,TierLevelSnapshot=pkg.TierLevel,BillingPeriodSnapshot=pkg.BillingPeriod,
            IdempotencyKey=idemKey,CreatedAt=now,ExpiresAt=now.AddMinutes(options.OrderLifetimeMinutes)
        };
        // Kiểm tra cấu hình ngân hàng TRƯỚC khi lưu đơn hàng.
        var result=ToDto(order);
        db.PaymentOrders.Add(order);
        db.PaymentAuditLogs.Add(new PaymentAuditLog { PaymentOrder=order,ActorUserId=userId,
            Action="OrderCreated",Detail="Tạo yêu cầu thanh toán từ giá trong database." });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            existing=await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(x=>x.UserId==userId && x.IdempotencyKey==idemKey);
            if (existing is null) throw new InvalidOperationException("Trùng mã đơn hàng; vui lòng tạo lại với khóa mới.");
            if(existing.PackageId!=packageId) throw new InvalidOperationException("Idempotency-Key đã được dùng cho gói khác.");
            return ToDto(existing);
        }
        return result;
    }

    public async Task<PaymentOrderDto> GetOrderAsync(int userId, string code)
    {
        var order=await db.PaymentOrders.FirstOrDefaultAsync(o=>o.OrderCode==code && (o.UserId==userId || db.Users.Any(u=>u.Id==userId && u.Role=="Admin")))
            ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng của bạn.");
        return ToDto(order);
    }

    public async Task<List<PaymentOrderDto>> HistoryAsync(int userId)
    {
        var orders=await db.PaymentOrders.AsNoTracking().Where(o=>o.UserId==userId)
            .OrderByDescending(o=>o.CreatedAt).Take(100).ToListAsync();
        return orders.Select(o=>ToDto(o,false)).ToList();
    }

    public async Task<List<object>> AdminOrdersAsync(string? status, string? code, int? userId, DateTime? fromUtc, DateTime? toUtc)
    {
        var q=db.PaymentOrders.AsNoTracking().AsQueryable();
        if(!string.IsNullOrEmpty(status)) q=q.Where(x=>x.Status==status);
        if(!string.IsNullOrEmpty(code)) q=q.Where(x=>x.OrderCode.Contains(code));
        if(userId.HasValue) q=q.Where(x=>x.UserId==userId.Value);
        if(fromUtc.HasValue) q=q.Where(x=>x.CreatedAt>=fromUtc.Value);
        if(toUtc.HasValue) q=q.Where(x=>x.CreatedAt<toUtc.Value);
        var rows=await q.OrderByDescending(x=>x.CreatedAt).Take(200)
            .Select(x=>new {x.Id,x.OrderCode,x.UserId,x.PackageId,x.PackageNameSnapshot,
                x.Amount,x.Status,x.CreatedAt,x.ExpiresAt,x.PaidAt,x.ReviewReason}).ToListAsync();
        return rows.Cast<object>().ToList();
    }

    public async Task<object> AdminDetailAsync(long id)
    {
        var order=await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id)
            ?? throw new KeyNotFoundException("Không có đơn hàng.");
        var transactions=await db.PaymentBankTransactions.AsNoTracking()
            .Where(x=>x.PaymentOrderId==id).OrderByDescending(x=>x.ReceivedAt)
            .Select(x=>new{x.Provider,x.ProviderTransactionId,x.Amount,x.Status,x.ReviewReason,x.ReceivedAt}).ToListAsync();
        var audits=await db.PaymentAuditLogs.AsNoTracking().Where(x=>x.PaymentOrderId==id)
            .OrderByDescending(x=>x.CreatedAt).Select(x=>new{x.Action,x.Detail,x.CreatedAt}).ToListAsync();
        return new {Order=new {order.Id,order.OrderCode,order.UserId,order.PackageId,order.PackageNameSnapshot,
            order.Amount,order.Currency,order.Provider,order.Status,order.DurationDaysSnapshot,order.TierLevelSnapshot,
            order.BillingPeriodSnapshot,order.CreatedAt,order.ExpiresAt,order.PaidAt,order.ReviewReason},Transactions=transactions,Audits=audits};
    }

    public async Task<PaymentOrderDto> CancelAsync(int userId, string code)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        var order=await db.PaymentOrders.FirstOrDefaultAsync(x=>x.OrderCode==code && x.UserId==userId)
            ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        await LockAsync("order:"+order.Id);
        await db.Entry(order).ReloadAsync();
        if(order.Status!=PaymentStates.Pending || order.ExpiresAt<=DateTime.UtcNow)
            throw new InvalidOperationException("Chỉ có thể hủy đơn đang chờ thanh toán và còn hạn.");
        order.Status=PaymentStates.Cancelled;
        db.PaymentAuditLogs.Add(new PaymentAuditLog {PaymentOrderId=order.Id,ActorUserId=userId,
            Action="OrderCancelled",Detail="Khách hàng hủy đơn chưa thanh toán."});
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(order);
    }

    public async Task<object> AdminReconciliationAsync()
    {
        var unlinked=await db.PaymentBankTransactions.AsNoTracking()
            .Where(x=>x.Status==PaymentStates.RequiresReview)
            .OrderByDescending(x=>x.ReceivedAt).Take(100)
            .Select(x=>new{x.Id,x.ProviderTransactionId,x.PaymentOrderId,x.ClaimedOrderCode,
                x.Amount,x.Currency,x.Status,x.ReviewReason,x.ReceivedAt}).ToListAsync();
        return new {Items=unlinked,Count=unlinked.Count,Message="Đối soát thủ công với dữ liệu ngân hàng/SePay trước khi can thiệp. Không có API tự đánh dấu Paid."};
    }

    private PaymentOrderDto ToDto(PaymentOrder o, bool includeQr=true)
    {
        var qr = includeQr && o.Status==PaymentStates.Pending && o.ExpiresAt>DateTime.UtcNow
            ? gateway.BuildQrUrl(o) : "";
        var effectiveStatus=o.Status==PaymentStates.Pending && o.ExpiresAt<=DateTime.UtcNow
            ? PaymentStates.Expired : o.Status;
        return new PaymentOrderDto(o.OrderCode,o.PackageNameSnapshot,o.Amount,o.Currency,
            o.DurationDaysSnapshot,effectiveStatus,Utc(o.CreatedAt),Utc(o.ExpiresAt),
            o.PaidAt.HasValue ? Utc(o.PaidAt.Value) : (DateTime?)null,qr,
            o.OrderCode,gateway.BankName,gateway.AccountNumber,gateway.AccountHolder,o.ReviewReason,o.PackageId);
    }

    public async Task<string> ProcessVerifiedSePayAsync(SePayReceipt r)
    {
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        // SQL Server application lock khóa đồng thời trên nhiều instance API (không dùng lock trong RAM).
        await LockAsync("sepay:"+r.TransactionId);
        if (await db.PaymentWebhookEvents.AnyAsync(x=>x.Provider=="SePay" && x.ProviderEventId==r.TransactionId))
        { await tx.CommitAsync(); return "Duplicate"; }
        var code=SePayWebhookParser.ExtractOrderCode(r);
        PaymentOrder? order=code is null ? null : await db.PaymentOrders.FirstOrDefaultAsync(x=>x.OrderCode==code);
        if(order is not null) await LockAsync("order:"+order.Id);
        // Re-read under SQL lock; một lần nữa kiểm tra trạng thái trước khi cấp VIP.
        if(order is not null) await db.Entry(order).ReloadAsync();
        var now=DateTime.UtcNow;
        var reason="";
        if (r.TransferType != "in") reason="Giao dịch không phải tiền vào";
        else if (r.Amount<=0 || r.Amount!=decimal.Truncate(r.Amount)) reason="Số tiền giao dịch không hợp lệ";
        else if (!string.Equals(r.Currency,"VND",StringComparison.OrdinalIgnoreCase)) reason="Tiền tệ không phù hợp";
        else if (string.IsNullOrWhiteSpace(options.AccountNumber) || r.AccountNumber != options.AccountNumber) reason="Sai tài khoản thụ hưởng";
        else if (code is null) reason="Không xác định được duy nhất mã đơn";
        else if (order is null) reason="Không tìm thấy đơn";
        else if (order.Amount!=r.Amount) reason="Số tiền khác với đơn";
        else if (order.ExpiresAt<=now) reason="Tiền đến sau thời hạn đơn";
        else if (order.Status!=PaymentStates.Pending) reason="Đơn không ở trạng thái Pending";
        if (reason.Length==0 && order is not null)
        {
            await LockAsync("vip-user:"+order.UserId);
            // Khóa theo UserId ngăn hai đơn hợp lệ đồng thời đọc cùng thời hạn rồi gây mất ngày VIP.
            var from=await db.UserSubscriptions.Where(s=>s.UserId==order.UserId && s.IsActive && (s.GrantedTierLevel ?? s.Package.TierLevel)>0 && s.EndDate>now)
                .Select(s=>(DateTime?)s.EndDate).MaxAsync() ?? now;
            var sub=new UserSubscription {UserId=order.UserId,PackageId=order.PackageId,
                StartDate=from,EndDate=SubscriptionRules.EndDate(from, order.BillingPeriodSnapshot, order.DurationDaysSnapshot),IsActive=true,
                ActivationPaymentOrderId=order.Id,GrantedTierLevel=order.TierLevelSnapshot};
            db.UserSubscriptions.Add(sub);
            var user=await db.Users.FirstAsync(u=>u.Id==order.UserId);
            user.VipExpiresAt=sub.EndDate; // Thời hạn hiển thị; backend vẫn kiểm tra UserSubscriptions khi phân quyền.
            order.Status=PaymentStates.Paid;order.PaidAt=now;
            // Giữ bảng lịch sử giao dịch cũ cho báo cáo cũ, không biến nó thành nơi quyết định cấp VIP.
            db.RevenueTransactions.Add(new RevenueTransaction {UserId=order.UserId,PackageId=order.PackageId,
                Amount=order.Amount,PaymentDate=now,Status="Success"});
            db.PaymentAuditLogs.Add(new PaymentAuditLog {PaymentOrderId=order.Id,
                Action="VipActivated",Detail=$"Đã cấp {order.DurationDaysSnapshot} ngày; từ {from:O}."});
        }
        else
        {
            if (order is not null && (order.Status is PaymentStates.Pending or PaymentStates.Expired))
            {
                order.Status=PaymentStates.RequiresReview;order.ReviewReason=reason;
            }
            db.PaymentAuditLogs.Add(new PaymentAuditLog {PaymentOrderId=order?.Id,
                Action="PaymentRequiresReview",Detail=reason});
        }
        var bankTx=new PaymentBankTransaction {ProviderTransactionId=r.TransactionId,
            PaymentOrderId=order?.Id,Amount=r.Amount,Currency=r.Currency,
            AccountNumberMasked=Mask(r.AccountNumber),ClaimedOrderCode=code,
            Status=reason.Length==0 ? PaymentStates.Paid:PaymentStates.RequiresReview,
            ReviewReason=reason};
        db.PaymentBankTransactions.Add(bankTx);
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent {ProviderEventId=r.TransactionId,
            PayloadHash=r.BodyHash,Outcome=reason.Length==0 ? PaymentStates.Paid:PaymentStates.RequiresReview});
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return reason.Length==0 ? PaymentStates.Paid : PaymentStates.RequiresReview;
    }

    private async Task LockAsync(string resource)
    {
        // Throw khi không lấy được lock; webhook trả 503 để SePay có cơ hội gửi lại.
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Payment database lock timeout', 1;");
    }
    // SQL Server datetime2 không lưu DateTime.Kind: JSON cần Z để countdown trình duyệt chính xác.
    private static DateTime Utc(DateTime date) => DateTime.SpecifyKind(date,DateTimeKind.Utc);
    private static string Mask(string n)=>n.Length<=4 ? "****" : new string('*',n.Length-4)+n[^4..];
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
