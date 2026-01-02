namespace PhotogrammetryAPI.Services;

public interface IFileStorageService
{
    Task<string> SaveZipFileAsync(IFormFile file, int userId);
    Task<string> GetModelFilePathAsync(int projectId);
}

public class FileStorageService : IFileStorageService
{
    private readonly string _uploadPath;
    private readonly string _modelsPath;
    
    public FileStorageService(IConfiguration configuration)
    {
        _uploadPath = configuration["Storage:UploadsPath"] ?? "uploads";
        _modelsPath = configuration["Storage:ModelsPath"] ?? "models";
        
        Directory.CreateDirectory(_uploadPath);
        Directory.CreateDirectory(_modelsPath);
    }
    
    public async Task<string> SaveZipFileAsync(IFormFile file, int userId)
    {
        var folderName = $"{userId}_{Guid.NewGuid()}";
        var folderPath = Path.Combine(_uploadPath, folderName);
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
        var modelPath = Path.Combine(_modelsPath, $"project_{projectId}");
        return Task.FromResult(modelPath);
    }
}
