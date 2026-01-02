using System.ComponentModel.DataAnnotations;

namespace PhotogrammetryAPI.Models;

public class RegisterDto
{
    [Required]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    [Required]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    public string Password { get; set; } = string.Empty;
}

public class ProjectUploadDto
{
    [Required]
    public string ProjectName { get; set; } = string.Empty;
    
    [Required]
    public IFormFile ZipFile { get; set; } = null!;
}

public class ProjectStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DownloadUrl { get; set; }
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
