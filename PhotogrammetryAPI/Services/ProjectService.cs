using PhotogrammetryAPI.Data;
using PhotogrammetryAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace PhotogrammetryAPI.Services;

public interface IProjectService
{
    Task<Project> CreateProjectAsync(int userId, string projectName, string zipFilePath);
    Task<ProjectStatusDto?> GetProjectStatusAsync(int projectId, int userId);
    Task<List<ProjectStatusDto>> GetUserProjectsAsync(int userId);
}

public class ProjectService : IProjectService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    
    public ProjectService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }
    
    public async Task<Project> CreateProjectAsync(int userId, string projectName, string zipFilePath)
    {
        var project = new Project
        {
            Name = projectName,
            UserId = userId,
            ZipFilePath = zipFilePath,
            Status = ProcessingStatus.InQueue,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        
        return project;
    }
    
    public async Task<ProjectStatusDto?> GetProjectStatusAsync(int projectId, int userId)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
        
        if (project == null)
            return null;
        
        var baseUrl = _configuration["BaseUrl"] ?? "http://localhost:5000";
        
        return new ProjectStatusDto
        {
            Id = project.Id,
            Name = project.Name,
            Status = project.Status,
            CreatedAt = project.CreatedAt,
            ProcessingStartedAt = project.ProcessingStartedAt,
            CompletedAt = project.CompletedAt,
            ErrorMessage = project.ErrorMessage,
            DownloadUrl = project.Status == ProcessingStatus.Finished && project.OutputModelPath != null
                ? $"{baseUrl}/api/projects/{project.Id}/download"
                : null
        };
    }
    
    public async Task<List<ProjectStatusDto>> GetUserProjectsAsync(int userId)
    {
        var projects = await _context.Projects
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        
        var baseUrl = _configuration["BaseUrl"] ?? "http://localhost:5000";
        
        return projects.Select(p => new ProjectStatusDto
        {
            Id = p.Id,
            Name = p.Name,
            Status = p.Status,
            CreatedAt = p.CreatedAt,
            ProcessingStartedAt = p.ProcessingStartedAt,
            CompletedAt = p.CompletedAt,
            ErrorMessage = p.ErrorMessage,
            DownloadUrl = p.Status == ProcessingStatus.Finished && p.OutputModelPath != null
                ? $"{baseUrl}/api/projects/{p.Id}/download"
                : null
        }).ToList();
    }
}
