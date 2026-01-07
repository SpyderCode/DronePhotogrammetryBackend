using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotogrammetryAPI.Models;
using PhotogrammetryAPI.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PhotogrammetryAPI.Data;

namespace PhotogrammetryAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IQueueService _queueService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    
    public ProjectsController(
        IProjectService projectService, 
        IFileStorageService fileStorageService,
        IQueueService queueService,
        ApplicationDbContext context,
        IConfiguration configuration)
    {
        _projectService = projectService;
        _fileStorageService = fileStorageService;
        _queueService = queueService;
        _context = context;
        _configuration = configuration;
    }
    
    private int GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(userIdClaim ?? "0");
    }
    
    [HttpPost("upload")]
    [RequestSizeLimit(53687091200)] // 50GB
    [RequestFormLimits(MultipartBodyLengthLimit = 53687091200)]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadProject([FromForm] ProjectUploadDto dto)
    {
        var userId = GetUserId();
        
        if (dto.ZipFile == null || dto.ZipFile.Length == 0)
            return BadRequest(new { message = "No file uploaded" });
        
        if (!dto.ZipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only ZIP files are allowed" });
        
        var filePath = await _fileStorageService.SaveZipFileAsync(dto.ZipFile, userId);
        
        var project = await _projectService.CreateProjectAsync(userId, dto.ProjectName, filePath);
        
        await _queueService.PublishProjectAsync(project.Id);
        
        return Ok(new
        {
            projectId = project.Id,
            message = "Project uploaded successfully and queued for processing"
        });
    }
    
    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetProjectStatus(int id)
    {
        var userId = GetUserId();
        var status = await _projectService.GetProjectStatusAsync(id, userId);
        
        if (status == null)
            return NotFound(new { message = "Project not found" });
        
        return Ok(status);
    }
    
    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        var userId = GetUserId();
        var projects = await _projectService.GetUserProjectsAsync(userId);
        
        return Ok(projects);
    }
    
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadModel(int id)
    {
        var userId = GetUserId();
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        
        if (project == null)
            return NotFound(new { message = "Project not found" });
        
        if (project.Status != ProcessingStatus.Finished)
            return BadRequest(new { message = "Project processing is not complete" });
        
        if (string.IsNullOrEmpty(project.OutputModelPath))
            return NotFound(new { message = "Model file path not set" });
        
        // Get configured Projects path
        var projectsBasePath = _configuration["Storage:ProjectsPath"] ?? "../Projects";
        projectsBasePath = Path.GetFullPath(projectsBasePath);
        
        // Remove any leading path separators from relative paths
        var relativePath = project.OutputModelPath.TrimStart('/', '\\');
        
        var fullPath = Path.IsPathRooted(project.OutputModelPath) 
            ? project.OutputModelPath 
            : Path.Combine(projectsBasePath, relativePath);
        
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = $"Model file not found at: {fullPath}" });
        
        var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = Path.GetFileName(fullPath);
        
        return File(fileStream, "application/octet-stream", fileName);
    }
}
