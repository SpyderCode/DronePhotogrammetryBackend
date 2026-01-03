# 🏢 Drone Photogrammetry Backend

A distributed C# ASP.NET Core system for processing drone/indoor photogrammetry images into high-quality 3D models using COLMAP.

## ✨ Features

- 🔐 **JWT Authentication** - Secure user authentication with BCrypt
- 📤 **Large File Support** - Upload up to 50GB of images
- 🎨 **COLMAP Integration** - State-of-the-art 3D reconstruction
- 🚀 **GPU Accelerated** - CUDA support for fast processing on worker nodes
- 📊 **Distributed Processing** - RabbitMQ queue with multiple GPU workers
- 💾 **MySQL Database** - Robust data persistence
- 📡 **RESTful API** - Clean, documented endpoints
- 🔄 **Real-time Status** - Track processing progress with status updates
- 🖥️ **Scalable Architecture** - Separate API and Worker components

## 🚀 Quick Start

### Prerequisites

- .NET 8.0 SDK
- Docker & Docker Compose (for MySQL and RabbitMQ)
- COLMAP 3.14+ (for workers only)
- NVIDIA GPU with CUDA (for workers only)

### Installation

```bash
# Clone repository
git clone https://github.com/SpyderCode/DronePhotogrammetryBackend.git
cd DronePhotogrammetryBackend

# Start services (Docker)
docker compose up -d

# Configure API
cd PhotogrammetryAPI
cp appsettings.json appsettings.Development.json
# Edit connection strings if needed

# Run API
dotnet run
```

API will be available at: http://localhost:5273

### Running a Worker Node

On a machine with GPU and COLMAP installed:

```bash
cd PhotogrammetryWorker
cp appsettings.json appsettings.Development.json
# Configure RabbitMQ host and shared storage path
dotnet run
```

See [PhotogrammetryWorker/README.md](PhotogrammetryWorker/README.md) for details.

## 📖 Documentation

### Getting Started
- **[PhotogrammetryAPI/QUICKSTART.md](PhotogrammetryAPI/QUICKSTART.md)** - API setup guide
- **[PhotogrammetryWorker/README.md](PhotogrammetryWorker/README.md)** - Worker setup guide

### Configuration
- **[PREPARE_TEST_DATA.md](PhotogrammetryAPI/PREPARE_TEST_DATA.md)** - Creating test datasets
- **[LARGE_FILE_UPLOAD.md](PhotogrammetryAPI/LARGE_FILE_UPLOAD.md)** - Handle large datasets (50GB+)
- **[CONFIGURE_RABBITMQ.md](PhotogrammetryAPI/CONFIGURE_RABBITMQ.md)** - RabbitMQ timeout settings

### Architecture
- **[SHARED_STORAGE.md](SHARED_STORAGE.md)** - Shared storage configuration
- **[STATUS_UPDATES.md](STATUS_UPDATES.md)** - Status update messaging system
- **[FAULT_TOLERANCE.md](FAULT_TOLERANCE.md)** - Error handling and recovery

### Testing
- **[TESTING.md](PhotogrammetryAPI/TESTING.md)** - Complete testing guide
- **[CHANGELOG.md](PhotogrammetryAPI/CHANGELOG.md)** - Recent updates

## ��️ Architecture

The system is split into two components for optimal GPU resource utilization:

```
User → API Server (Upload) → Shared Storage → RabbitMQ Queue → Worker Node (GPU) → COLMAP → 3D Model
         ↓                                          ↓                    ↓
    MySQL Database                          Status Updates         Shared Storage
```

**Components:**
- **API Server** (`PhotogrammetryAPI/`): Handles uploads, authentication, and status queries (no GPU needed)
- **Worker Nodes** (`PhotogrammetryWorker/`): Process photogrammetry with COLMAP (requires GPU)
- **Shared Storage**: Common filesystem for projects (`Projects/` folder)
- **RabbitMQ**: Distributes work and handles status updates between API and workers

### Technology Stack

| Component | Technology |
|-----------|-----------|
| Framework | ASP.NET Core 8.0 / C# 12 |
| Database | MySQL 8.0 with Entity Framework Core |
| Queue | RabbitMQ 3.x with bidirectional status updates |
| Photogrammetry | COLMAP 3.14+ with CUDA |
| Authentication | JWT with BCrypt |
| API Documentation | Swagger/OpenAPI |

## 📡 API Endpoints

### Authentication
```http
POST /api/auth/register  # Register new user
POST /api/auth/login     # Login and get JWT token
```

### Projects (Authenticated)
```http
POST   /api/projects/upload         # Upload images (up to 50GB)
GET    /api/projects                # List all projects
GET    /api/projects/{id}/status    # Check processing status
GET    /api/projects/{id}/download  # Download 3D model
```

### Example Usage

```bash
# Register
curl -X POST http://localhost:5273/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"user","email":"user@test.com","password":"Pass123!"}'

# Upload images
curl -X POST http://localhost:5273/api/projects/upload \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "ProjectName=Building" \
  -F "ZipFile=@images.zip"

# Check status
curl http://localhost:5273/api/projects/1/status \
  -H "Authorization: Bearer YOUR_TOKEN"

# Download model
curl http://localhost:5273/api/projects/1/download \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --output model.ply
```

## 🔧 Configuration

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3307;database=photogrammetry;user=root;password=rootpassword"
  },
  "Colmap": {
    "ExecutablePath": "colmap"
  },
  "Storage": {
    "MaxUploadSizeGB": 50
  }
}
```

## 🎯 COLMAP Pipeline

The system uses COLMAP's 7-step photogrammetry pipeline:

1. **Feature Extraction** - SIFT feature detection
2. **Feature Matching** - Find correspondences
3. **Sparse Reconstruction** - Structure-from-Motion
4. **Image Undistortion** - Prepare for dense reconstruction
5. **Dense Stereo Matching** - GPU-accelerated depth estimation
6. **Stereo Fusion** - Fuse depth maps into point cloud
7. **Poisson Meshing** - Generate final 3D mesh

**Output**: High-quality PLY point clouds and meshes

## ⚡ Performance

### Processing Times (with GPU)

| Images | Upload | Processing | Output Size |
|--------|--------|------------|-------------|
| 10 | 5s | ~10 min | 5-10 MB |
| 50 | 20s | ~30 min | 50-100 MB |
| 100 | 40s | ~1 hour | 100-200 MB |
| 500 | 3 min | ~4 hours | 500MB-1GB |
| 1000 | 5 min | ~8 hours | 1-2 GB |

*Times assume NVIDIA GPU with CUDA*

## 🐳 Docker Deployment

```yaml
# docker-compose.yml
services:
  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: rootpassword
      MYSQL_DATABASE: photogrammetry
    ports:
      - "3307:3306"

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
```

## 🔒 Security

- ✅ JWT authentication with 7-day expiration
- ✅ BCrypt password hashing (work factor 10)
- ✅ SQL injection protection (EF Core)
- ✅ File type validation (ZIP only)
- ✅ Size limits enforced (50GB max)
- ✅ User-specific project isolation

**Production checklist**:
- [ ] Update JWT secret key
- [ ] Enable HTTPS
- [ ] Configure CORS properly
- [ ] Set up rate limiting
- [ ] Implement per-user quotas
- [ ] Add API key authentication (optional)

## 🧪 Testing

### Quick Test (10 images, ~10 minutes)
```bash
cd PhotogrammetryAPI
./quick_test.sh
```

### Full Test (128 images, ~2-4 hours with GPU)
```bash
cd PhotogrammetryAPI
./test_full_dataset.sh
```

### Monitor Running Project
```bash
cd PhotogrammetryAPI
./monitor_project.sh PROJECT_ID TOKEN
```

See [TESTING.md](PhotogrammetryAPI/TESTING.md) for details.

## 📊 System Requirements

### Minimum
- 4-core CPU
- 8GB RAM
- 100GB storage
- CPU-only processing (slow)

### Recommended
- 8+ core CPU
- 32GB RAM
- 500GB SSD storage
- **NVIDIA GPU with 8GB+ VRAM**
- CUDA 11.0+

### Optimal
- 16+ core CPU
- 64GB RAM
- 2TB NVMe storage
- **NVIDIA RTX 3080 or better**
- 10 Gigabit network

## 🤝 Contributing

1. Fork the repository
2. Create feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open Pull Request

## 📝 License

This project uses:
- ASP.NET Core (MIT License)
- COLMAP (BSD License)
- RabbitMQ (MPL 2.0)

## 🔗 Links

- **COLMAP Documentation**: https://colmap.github.io/
- **API Documentation**: http://localhost:5273/swagger (when running)
- **Issues**: https://github.com/SpyderCode/DronePhotogrammetryBackend/issues

## 📮 Support

For questions or issues:
1. Check the [documentation](PhotogrammetryAPI/)
2. Review [TESTING.md](PhotogrammetryAPI/TESTING.md) for troubleshooting
3. Open an issue on GitHub

---

**Built with ❤️ using C# and COLMAP**

*Transform your images into stunning 3D models!* 🎨✨
