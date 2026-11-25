using System.ComponentModel.DataAnnotations;
namespace VietVan.API.DTOs.Auth;
public class RegisterRequest { [StringLength(50,MinimumLength=3)] public string? Username {get;set;} [MaxLength(150)] public string? FullName {get;set;} [Required,EmailAddress,MaxLength(150)] public string Email {get;set;}=""; [Required,StringLength(128,MinimumLength=8)] public string Password {get;set;}=""; }
public class LoginRequest { [Required] public string UsernameOrEmail {get;set;}=""; [Required] public string Password {get;set;}=""; }
public record AuthResponse(int UserId,string Username,string Role,int SubscriptionTierLevel,string Token,string? FullName = null,
    bool EmailVerificationRequired = false, bool? VerificationEmailSent = null);
