namespace PhotogrammetryAPI.Services;

public interface IFileStorageService
{
    Task<string> SaveZipFileAsync(IFormFile file, int userId);
    Task<string> GetModelFilePathAsync(int projectId);
}

public class FileStorageService : IFileStorageService
{
    private readonly string _projectsPath;
    
    public FileStorageService(IConfiguration configuration)
    {
        _projectsPath = configuration["Storage:ProjectsPath"] ?? "Projects";
        Directory.CreateDirectory(_projectsPath);
    }
    
    public async Task<string> SaveZipFileAsync(IFormFile file, int userId)
    {
        var folderName = $"{userId}_{Guid.NewGuid()}";
        var folderPath = Path.Combine(_projectsPath, folderName);
        Directory.CreateDirectory(folderPath);
        
        var fileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}_{DateTime.UtcNow.Ticks}.zip";
        var filePath = Path.Combine(folderPath, fileName);
        
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        
        return filePath;
    }
    
    public Task<string> GetModelFilePathAsync(int projectId)
    {
        var modelPath = Path.Combine(_projectsPath, $"project_{projectId}");
        return Task.FromResult(modelPath);
    }
}
