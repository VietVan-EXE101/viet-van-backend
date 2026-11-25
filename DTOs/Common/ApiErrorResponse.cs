namespace VietVan.API.DTOs.Common;

public sealed record ApiErrorResponse(string Message, string? TraceId = null, string? Code = null);
