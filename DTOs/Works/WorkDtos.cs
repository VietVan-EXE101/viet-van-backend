using System.ComponentModel.DataAnnotations;
namespace VietVan.API.DTOs.Works;
public class CreateWorkRequest { [Required,MaxLength(250)] public string Title {get;set;}=""; [Required,MaxLength(150)] public string Author {get;set;}=""; public string? Description {get;set;} public string Content {get;set;}=""; public string? CoverImageUrl {get;set;} [Range(0,100)] public int RequiredTierLevel {get;set;} public bool IsActive {get;set;}=true; }
public class CreateWorkFormRequest : CreateWorkRequest { [Microsoft.AspNetCore.Mvc.FromForm(Name = "coverImage")] public IFormFile? CoverImage { get; set; } }
public record WorkListItem(int Id,string Title,string Author,string? Description,string? CoverImageUrl,int RequiredTierLevel);
public record AdminWorkListItem(int Id, string Title, string Author, bool IsActive, int RequiredTierLevel);
public record PagedResponse<T>(IReadOnlyList<T> Items,int Page,int PageSize,int TotalItems,int TotalPages);
public sealed record WorkMetadataDto(int Id, string Title, string Author, string? Description, string? CoverImageUrl, int RequiredTierLevel, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public static WorkMetadataDto From(VietVan.API.Entities.LiteraryWork work) => new(work.Id, work.Title, work.Author, work.Description, work.CoverImageUrl, work.RequiredTierLevel, work.IsActive, work.CreatedAt, work.UpdatedAt);
}
