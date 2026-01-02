using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhotogrammetryAPI.Models;

public class Project
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public int UserId { get; set; }
    
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
    
    [Required]
    [MaxLength(500)]
    public string ZipFilePath { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? OutputModelPath { get; set; }
    
    [Required]
    public ProcessingStatus Status { get; set; } = ProcessingStatus.InQueue;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? ProcessingStartedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }
}

public enum ProcessingStatus
{
    InQueue,
    Processing,
    Finished,
    Failed
}
