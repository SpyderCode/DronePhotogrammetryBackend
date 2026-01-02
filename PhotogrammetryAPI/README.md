# Photogrammetry API - Indoor Scene 3D Reconstruction

A complete C# ASP.NET Core backend API for processing indoor photogrammetry images into 3D models using COLMAP.

## Features

- ✅ **User Authentication** - JWT-based authentication with BCrypt password hashing
- ✅ **Project Management** - Upload and manage photogrammetry projects
- ✅ **Large File Upload** - Support for ZIP files up to 10GB containing thousands of images
- ✅ **Distributed Queue System** - RabbitMQ for multi-server processing capability
- ✅ **COLMAP Integration** - Automated 3D reconstruction for indoor scenes
- ✅ **Status Tracking** - Real-time processing status (InQueue → Processing → Finished)
- ✅ **Model Download** - Download completed 3D models
- ✅ **MySQL Database** - Persistent storage with Entity Framework Core

## Architecture

```
User → API Upload → File Storage → RabbitMQ Queue → Background Worker → COLMAP → 3D Model
                                                           ↓
                                                      MySQL Database
```

## Prerequisites

### Required
- .NET 8.0 SDK
- MySQL Server 8.0+
- RabbitMQ Server

### Optional (for actual 3D processing)
- **COLMAP** (COLMAP) - For photogrammetry processing

## Installation

### 1. Install MySQL
```bash
# Ubuntu/Debian
sudo apt update
sudo apt install mysql-server
sudo mysql_secure_installation

# Start MySQL
sudo systemctl start mysql
sudo systemctl enable mysql
```

### 2. Install RabbitMQ
```bash
# Ubuntu/Debian
sudo apt install rabbitmq-server

# Start RabbitMQ
sudo systemctl start rabbitmq-server
sudo systemctl enable rabbitmq-server

# Enable management plugin (optional)
sudo rabbitmq-plugins enable rabbitmq_management
```

### 3. Install COLMAP (Optional - for actual 3D processing)

**Option A: Using Snap (Recommended)**
```bash
sudo snap install colmap
```

**Option B: Manual Installation**
```bash
# Download from https://github.com/alicevision/COLMAP/releases
wget https://github.com/alicevision/COLMAP/releases/download/v2023.3.0/COLMAP-2023.3.0-linux-cuda11.tar.gz
tar -xzf COLMAP-2023.3.0-linux-cuda11.tar.gz
sudo mv COLMAP-2023.3.0 /opt/colmap

# Add to PATH
echo 'export PATH="/opt/colmap:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

**Note:** COLMAP requires a CUDA-capable GPU for full functionality. Without COLMAP, the API will create dummy models for testing.

### 4. Setup Database
```bash
# Run as root or with appropriate MySQL user
mysql -u root -p < database_setup.sql
```

### 5. Configure Application
Edit `appsettings.json` to match your environment:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=photogrammetry;user=root;password=YOUR_PASSWORD"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": "5672",
    "Username": "guest",
    "Password": "guest"
  }
}
```

## Running the Application

### Development Mode
```bash
cd PhotogrammetryAPI
dotnet run
```

The API will be available at: `http://localhost:5000`

### Production Mode
```bash
dotnet publish -c Release -o ./publish
cd publish
dotnet PhotogrammetryAPI.dll
```

## API Endpoints

### Authentication

#### Register User
```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "johndoe",
  "email": "john@example.com",
  "password": "SecurePass123"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "johndoe",
  "email": "john@example.com"
}
```

#### Login
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "john@example.com",
  "password": "SecurePass123"
}
```

### Projects

**Note:** All project endpoints require authentication. Include the JWT token in the Authorization header:
```
Authorization: Bearer YOUR_TOKEN_HERE
```

#### Upload Project
```http
POST /api/projects/upload
Content-Type: multipart/form-data
Authorization: Bearer YOUR_TOKEN

ProjectName=MyIndoorScan
ZipFile=@images.zip
```

**Response:**
```json
{
  "projectId": 1,
  "message": "Project uploaded successfully and queued for processing"
}
```

#### Get Project Status
```http
GET /api/projects/1/status
Authorization: Bearer YOUR_TOKEN
```

**Response:**
```json
{
  "id": 1,
  "name": "MyIndoorScan",
  "status": 2,
  "createdAt": "2025-12-22T00:00:00Z",
  "processingStartedAt": "2025-12-22T00:01:00Z",
  "completedAt": "2025-12-22T00:15:00Z",
  "errorMessage": null,
  "downloadUrl": "http://localhost:5000/api/projects/1/download"
}
```

**Status Values:**
- `0` = InQueue
- `1` = Processing
- `2` = Finished
- `3` = Failed

#### Get All User Projects
```http
GET /api/projects
Authorization: Bearer YOUR_TOKEN
```

#### Download 3D Model
```http
GET /api/projects/1/download
Authorization: Bearer YOUR_TOKEN
```

## Usage Example

### Using cURL

```bash
# 1. Register
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"test","email":"test@test.com","password":"Test123!"}' \
  | jq -r '.token')

# 2. Upload images
PROJECT_ID=$(curl -s -X POST http://localhost:5000/api/projects/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=IndoorRoom" \
  -F "ZipFile=@indoor_images.zip" \
  | jq -r '.projectId')

# 3. Check status
curl -X GET "http://localhost:5000/api/projects/$PROJECT_ID/status" \
  -H "Authorization: Bearer $TOKEN"

# 4. Download model (when finished)
curl -X GET "http://localhost:5000/api/projects/$PROJECT_ID/download" \
  -H "Authorization: Bearer $TOKEN" \
  --output model.obj
```

### Using Python

```python
import requests
import time

BASE_URL = "http://localhost:5000"

# Register/Login
response = requests.post(f"{BASE_URL}/api/auth/register", json={
    "username": "testuser",
    "email": "test@example.com",
    "password": "SecurePass123"
})
token = response.json()["token"]

headers = {"Authorization": f"Bearer {token}"}

# Upload project
with open("indoor_images.zip", "rb") as f:
    files = {"ZipFile": f}
    data = {"ProjectName": "My Indoor Scan"}
    response = requests.post(
        f"{BASE_URL}/api/projects/upload",
        headers=headers,
        data=data,
        files=files
    )
    project_id = response.json()["projectId"]

# Poll for completion
while True:
    response = requests.get(
        f"{BASE_URL}/api/projects/{project_id}/status",
        headers=headers
    )
    status = response.json()
    print(f"Status: {status['status']}")
    
    if status["status"] == 2:  # Finished
        print(f"Download URL: {status['downloadUrl']}")
        break
    elif status["status"] == 3:  # Failed
        print(f"Error: {status['errorMessage']}")
        break
    
    time.sleep(5)

# Download model
response = requests.get(
    f"{BASE_URL}/api/projects/{project_id}/download",
    headers=headers
)
with open("model.obj", "wb") as f:
    f.write(response.content)
```

## Preparing Images for Processing

### Best Practices for Indoor Photogrammetry

1. **Image Quality**
   - Use high-resolution images (minimum 1920x1080)
   - Ensure good lighting (avoid shadows and glare)
   - Keep camera settings consistent (aperture, ISO, white balance)

2. **Capture Technique**
   - Take 50-200 overlapping photos
   - Maintain 60-80% overlap between consecutive images
   - Capture from multiple angles and heights
   - Move around the object/room in a systematic pattern

3. **File Organization**
   - Place all images in a single folder
   - Compress to ZIP format before upload
   - Supported formats: JPG, JPEG, PNG
   - Remove blurry or poorly lit images

4. **Example Folder Structure**
   ```
   indoor_scan.zip
   └── images/
       ├── IMG_001.jpg
       ├── IMG_002.jpg
       ├── IMG_003.jpg
       └── ...
   ```

## Scaling for Production

### Multiple Worker Servers

The system supports distributed processing. To add more worker servers:

1. Install the API on multiple servers
2. Configure all servers to connect to the same MySQL and RabbitMQ instances
3. Each server will process jobs from the shared queue

### Performance Considerations

- **Storage**: Ensure sufficient disk space for uploads and generated models
- **Memory**: COLMAP processing can require 8-16GB RAM per project
- **GPU**: CUDA-capable GPU significantly speeds up processing
- **Network**: Fast upload speeds for large ZIP files

## Troubleshooting

### RabbitMQ Connection Issues
```bash
# Check RabbitMQ status
sudo systemctl status rabbitmq-server

# View logs
sudo journalctl -u rabbitmq-server -f
```

### MySQL Connection Issues
```bash
# Check MySQL status
sudo systemctl status mysql

# Test connection
mysql -u root -p -e "SHOW DATABASES;"
```

### COLMAP Not Found
If COLMAP is not installed, the API will create dummy models for testing. Install COLMAP for actual 3D reconstruction.

### Worker Not Processing
Check the application logs for worker errors:
```bash
dotnet run --project PhotogrammetryAPI
# Look for "Worker error" or "Error processing message" in output
```

## Project Structure

```
PhotogrammetryAPI/
├── Controllers/
│   ├── AuthController.cs          # Authentication endpoints
│   └── ProjectsController.cs      # Project management endpoints
├── Data/
│   ├── ApplicationDbContext.cs    # EF Core context
│   └── ApplicationDbContextFactory.cs
├── Models/
│   ├── User.cs                    # User entity
│   ├── Project.cs                 # Project entity
│   └── DTOs.cs                    # Data transfer objects
├── Services/
│   ├── AuthService.cs             # Authentication logic
│   ├── TokenService.cs            # JWT token generation
│   ├── ProjectService.cs          # Project management
│   ├── FileStorageService.cs      # File handling
│   ├── RabbitMQService.cs         # Queue management
│   └── PhotogrammetryWorker.cs    # Background processor
├── Program.cs                      # Application startup
├── appsettings.json               # Configuration
└── database_setup.sql             # Database schema
```

## Security Notes

1. **Change Default Secrets**: Update `Jwt:Key` in `appsettings.json` before production
2. **MySQL Password**: Set a strong password for the database user
3. **HTTPS**: Use HTTPS in production (configure SSL certificates)
4. **File Validation**: Only ZIP files are accepted for upload
5. **Authentication**: All project endpoints require valid JWT tokens

## License

This project is provided as-is for photogrammetry processing applications.

## Support

For issues or questions, please check:
- COLMAP documentation: https://github.com/alicevision/COLMAP
- RabbitMQ documentation: https://www.rabbitmq.com/documentation.html
- Entity Framework Core: https://docs.microsoft.com/ef/core/
