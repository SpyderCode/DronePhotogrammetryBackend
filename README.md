# Drone Photogrammetry Backend

A distributed C# backend system for processing photogrammetry images into 3D models using COLMAP. Designed for scalability with separate API and GPU worker nodes.

## Features

- 🔐 **JWT Authentication** - Secure user authentication with BCrypt password hashing
- 📤 **Large File Upload** - Support for ZIP files up to 50GB with thousands of images
- 🎨 **COLMAP Integration** - High-quality 3D reconstruction with GPU acceleration
- 📊 **Distributed Queue** - RabbitMQ for multi-worker processing
- 🔄 **Fault Tolerant** - Automatic retry and dead letter queue for failures
- 📡 **Status Updates** - Real-time project status via bidirectional messaging
- 💾 **MySQL Database** - Persistent storage with Entity Framework Core
- 📝 **Structured Logging** - Serilog with file and console output

## Architecture

```
User → API Server (Upload/Status) → Shared Storage → RabbitMQ → Worker Nodes (GPU)
                ↓                                          ↓              ↓
           MySQL Database  ←──────────  Status Updates  ←─┘              ↓
                                                                   3D Models
```

### Components

- **PhotogrammetryAPI**: REST API for uploads, authentication, and status queries (no GPU required)
- **PhotogrammetryWorker**: Background processor that runs COLMAP on GPU-enabled machines
- **Shared Storage**: Common filesystem (`Projects/` folder) for images and models
- **RabbitMQ**: Message queue for work distribution and status updates
- **MySQL**: Database for users, projects, and metadata

## Prerequisites

### API Server
- .NET 8.0 SDK
- Docker (for MySQL and RabbitMQ) or native installations
- Access to shared storage

### Worker Nodes
- .NET 8.0 SDK
- **COLMAP 3.14+** (https://colmap.github.io/)
- **NVIDIA GPU** with CUDA 11.0+ (8GB+ VRAM recommended)
- Access to same shared storage
- RabbitMQ connection

## Quick Start

### 1. Clone Repository

```bash
git clone https://github.com/SpyderCode/DronePhotogrammetryBackend.git
cd DronePhotogrammetryBackend
```

### 2. Start Services

```bash
# Start MySQL and RabbitMQ with Docker
docker-compose up -d

# Or install natively (Ubuntu/Debian)
sudo apt install mysql-server rabbitmq-server
```

### 3. Configure Shared Storage

Create shared projects folder:
```bash
mkdir -p Projects
```

For distributed setup, use NFS or SMB (see `PhotogrammetryAPI/README.md` for details).

### 4. Run API Server

```bash
cd PhotogrammetryAPI
dotnet run
# API available at: http://localhost:5273
```

### 5. Run Worker (on GPU machine)

```bash
cd PhotogrammetryWorker
dotnet run
# Worker connects to RabbitMQ and waits for projects
```

## Basic Usage

### 1. Register User

```bash
curl -X POST http://localhost:5273/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"test","email":"test@example.com","password":"Test123!"}'
```

Response contains JWT token.

### 2. Upload Images

```bash
TOKEN="your_token_here"

curl -X POST http://localhost:5273/api/projects/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=MyBuilding" \
  -F "ZipFile=@images.zip"
```

Response contains `projectId`.

### 3. Check Status

```bash
curl http://localhost:5273/api/projects/1/status \
  -H "Authorization: Bearer $TOKEN"
```

Status values:
- `0` = InQueue
- `1` = Processing  
- `2` = Completed
- `3` = Failed

### 4. Download Model

```bash
curl http://localhost:5273/api/projects/1/download \
  -H "Authorization: Bearer $TOKEN" \
  --output model.ply
```

## Configuration

### API (`PhotogrammetryAPI/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3307;database=photogrammetry;user=root;password=rootpassword"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": "5672"
  },
  "Storage": {
    "ProjectsPath": "../Projects",
    "MaxUploadSizeGB": 50
  },
  "Jwt": {
    "Key": "YourSecretKeyHere_ChangeInProduction",
    "Issuer": "PhotogrammetryAPI"
  }
}
```

### Worker (`PhotogrammetryWorker/appsettings.json`)

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": "5672"
  },
  "Storage": {
    "ProjectsPath": "../Projects",
    "LocalCachePath": "/tmp/colmap_cache"
  },
  "Colmap": {
    "ExecutablePath": "colmap",
    "Quality": {
      "SiftMaxNumFeatures": 16384,
      "StereoMaxImageSize": 3200,
      "StereoWindowStep": 1
    }
  },
  "Logging": {
    "MinimumLevel": "Information"
  }
}
```

## Project Structure

```
DronePhotogrammetryBackend/
├── PhotogrammetryAPI/          # REST API (no GPU needed)
│   ├── Controllers/           # API endpoints
│   ├── Services/              # Business logic
│   ├── Models/                # Data entities
│   ├── Data/                  # EF Core DbContext
│   └── README.md             # API documentation
├── PhotogrammetryWorker/       # GPU worker
│   ├── ColmapWorkerService.cs # COLMAP processing
│   ├── StatusUpdateService.cs # Status messaging
│   └── README.md             # Worker documentation
├── Projects/                   # Shared storage
│   ├── project_1/
│   │   ├── images/           # Input images
│   │   └── output/           # Generated models
│   └── project_2/
├── docker-compose.yml          # MySQL + RabbitMQ
└── README.md                  # This file
```

## Performance

### Processing Times (with GPU)

| Images | Upload | COLMAP Processing | Output Size |
|--------|--------|-------------------|-------------|
| 10     | 5s     | ~10 min          | 5-10 MB     |
| 50     | 20s    | ~30 min          | 50-100 MB   |
| 100    | 40s    | ~1 hour          | 100-200 MB  |
| 500    | 3 min  | ~4 hours         | 500MB-1GB   |
| 1000   | 5 min  | ~8 hours         | 1-2 GB      |

*Times assume NVIDIA RTX 3080 or better*

### Storage Requirements

Per project: ~5x ZIP file size (ZIP + extracted + processing + output)

Example: 2GB ZIP → ~10GB total space needed

## Scaling

### Multiple Workers

Run multiple workers on different GPU machines:

```bash
# Worker 1 (RTX 3080)
cd PhotogrammetryWorker
dotnet run

# Worker 2 (RTX 4090) - different machine
cd PhotogrammetryWorker
dotnet run

# Worker 3 (A100) - another machine
cd PhotogrammetryWorker
dotnet run
```

All workers:
- Connect to same RabbitMQ
- Access same shared storage (NFS/SMB)
- Process different projects in parallel
- Automatically pick up failed jobs from crashed workers

### Shared Storage Options

**Local (Development)**:
- Same machine, relative path: `../Projects`

**NFS (Production)**:
```bash
# Mount on API and all workers
sudo mount nfs-server:/exports/photogrammetry /mnt/projects
```

**SMB/CIFS (Windows)**:
```bash
sudo mount -t cifs //server/photogrammetry /mnt/projects -o credentials=/etc/samba-creds
```

See `PhotogrammetryAPI/README.md` for detailed shared storage setup.

## Fault Tolerance

The system handles failures gracefully:

- **Worker Crash**: Job returns to queue, another worker picks it up
- **Network Issues**: Retriable errors are automatically requeued
- **Fatal Errors**: Sent to dead letter queue for manual review
- **Timeouts**: 24-hour consumer timeout, 48-hour message TTL
- **Manual Acknowledgment**: Jobs stay in queue until explicitly acknowledged

Monitor failed jobs at RabbitMQ Management UI: http://localhost:15672

## Monitoring

### Check System Stats

```bash
curl http://localhost:5273/api/stats
```

Returns:
- Number of active workers
- Messages in queue
- Messages being processed

### RabbitMQ Management

Access at http://localhost:15672 (guest/guest)

Monitor:
- Active workers
- Queue depth
- Processing rates
- Failed messages

### Logs

API logs: Console + `Logs/api-{date}.log`
Worker logs: Console + `Logs/worker-{date}.log`

## Testing

```bash
# Quick test (10 images)
cd PhotogrammetryAPI
./quick_test.sh

# Full dataset test (128 images)
./test_full_dataset.sh

# Monitor specific project
./monitor_project.sh PROJECT_ID TOKEN
```

## Troubleshooting

### Worker can't find project

**Problem**: "Project directory not found"

**Solution**: Ensure shared storage is mounted and `ProjectsPath` matches in both API and Worker configs.

### COLMAP fails

**Problem**: "COLMAP command failed with exit code"

**Solution**: 
- Verify COLMAP is installed: `colmap -h`
- Check GPU/CUDA: `nvidia-smi`
- Review worker logs for details

### Upload timeout

**Problem**: Large file upload fails

**Solution**: Increase client timeout:
```bash
curl --max-time 1800 ...  # 30 minutes
```

### RabbitMQ connection refused

**Problem**: Worker can't connect to RabbitMQ

**Solution**:
```bash
# Check RabbitMQ is running
sudo systemctl status rabbitmq-server

# Verify network connectivity
telnet rabbitmq-host 5672
```

## Security

**Production Checklist**:
- [ ] Change JWT secret key in `appsettings.json`
- [ ] Set strong MySQL password
- [ ] Enable HTTPS with SSL certificates
- [ ] Configure CORS properly
- [ ] Set up firewall rules
- [ ] Implement rate limiting
- [ ] Add per-user storage quotas
- [ ] Secure RabbitMQ with authentication
- [ ] Restrict shared storage permissions

## Documentation

- **PhotogrammetryAPI/README.md** - API setup, endpoints, configuration
- **PhotogrammetryWorker/README.md** - Worker setup, COLMAP configuration, GPU requirements

## Links

- **GitHub**: https://github.com/SpyderCode/DronePhotogrammetryBackend
- **COLMAP**: https://colmap.github.io/
- **RabbitMQ**: https://www.rabbitmq.com/

## License

This project uses:
- ASP.NET Core (MIT)
- COLMAP (BSD)
- RabbitMQ (MPL 2.0)

---

**Ready to transform images into 3D models at scale!** 🚀
