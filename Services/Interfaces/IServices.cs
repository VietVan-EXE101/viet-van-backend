using VietVan.API.DTOs.Auth; using VietVan.API.DTOs.Works; using VietVan.API.DTOs.Analytics; using VietVan.API.Entities;
namespace VietVan.API.Services.Interfaces;
public interface IAuthService { Task<AuthResponse> RegisterAsync(RegisterRequest r); Task<AuthResponse> LoginAsync(LoginRequest r); }
public interface IWorkService { Task<PagedResponse<WorkListItem>> GetPublicAsync(int page,int pageSize,string? search); Task<LiteraryWork> CreateAsync(CreateWorkRequest r); Task<LiteraryWork> ToggleAsync(int id); }
public interface IAnalyticsService { Task<RevenueAnalyticsResponse> RevenueAsync(DateTime? from,DateTime? to); }
