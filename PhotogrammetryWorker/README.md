# 🖥️ Photogrammetry Worker

GPU-accelerated COLMAP worker for processing photogrammetry projects.

## Purpose

This standalone application:
- Connects to RabbitMQ queue
- Processes photogrammetry projects
- Runs COLMAP 7-step pipeline
- Uses GPU acceleration (CUDA)
- Operates independently from the API

## Requirements

### Essential
- .NET 8.0 Runtime
- **COLMAP 3.14+** with CUDA support
- **NVIDIA GPU** with CUDA (8GB+ VRAM recommended)
- RabbitMQ access (network connection to queue server)
- Shared storage with API (NFS, SMB, or same machine)

### System Recommendations
- 16GB+ RAM
- 8+ CPU cores
- 50GB+ free disk space per project

## Installation

### 1. Install COLMAP

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install colmap

# Or download prebuilt from:
# https://colmap.github.io/install.html
```

### 2. Verify GPU/CUDA

```bash
nvidia-smi
colmap -h  # Should show CUDA support
```

### 3. Configure

Edit `appsettings.json`:

```json
{
  "RabbitMQ": {
    "Host": "rabbitmq-server-ip",
    "Port": "5672",
    "Username": "guest",
    "Password": "guest"
  },
  "Storage": {
    "ModelsPath": "/shared/models"
  },
  "Colmap": {
    "ExecutablePath": "colmap"
  }
}
```

### 4. Run

```bash
dotnet run
```

Or build and run:

```bash
dotnet build
dotnet bin/Debug/net8.0/PhotogrammetryWorker.dll
```

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `RabbitMQ:Host` | RabbitMQ server address | localhost |
| `RabbitMQ:Port` | RabbitMQ port | 5672 |
| `RabbitMQ:Username` | Queue username | guest |
| `RabbitMQ:Password` | Queue password | guest |
| `Storage:ModelsPath` | Shared models directory | models |
| `Colmap:ExecutablePath` | COLMAP binary path | colmap |

## How It Works

1. **Connects** to RabbitMQ queue `photogrammetry-queue`
2. **Waits** for project messages
3. **Receives** project ID from queue
4. **Processes** images through COLMAP pipeline:
   - Feature extraction
   - Feature matching
   - Sparse reconstruction
   - Image undistortion
   - Dense stereo matching (GPU)
   - Stereo fusion
   - Poisson meshing
5. **Generates** PLY 3D model
6. **Acknowledges** completion to queue
7. **Repeats** for next project

## Storage Requirements

The worker needs access to the same storage as the API:

```
/shared/models/
├── project_1/
│   ├── images/          # Input (from API)
│   ├── flat_images/     # Flattened (worker creates)
│   ├── database.db      # COLMAP database (worker creates)
│   └── output/          # Results (worker creates)
│       ├── sparse/
│       └── dense/
│           └── meshed-poisson.ply  # Final model
├── project_2/
└── project_3/
```

### Shared Storage Options

**Same Machine**:
```json
"Storage": { "ModelsPath": "/home/user/models" }
```

**NFS Mount**:
```bash
sudo mount nfs-server:/exports/models /mnt/models
```
```json
"Storage": { "ModelsPath": "/mnt/models" }
```

**SMB/CIFS Mount**:
```bash
sudo mount -t cifs //smb-server/models /mnt/models -o username=user
```

## Running as Service

### systemd Service

Create `/etc/systemd/system/photogrammetry-worker.service`:

```ini
[Unit]
Description=Photogrammetry COLMAP Worker
After=network.target

[Service]
Type=simple
User=worker
WorkingDirectory=/opt/PhotogrammetryWorker
ExecStart=/usr/bin/dotnet /opt/PhotogrammetryWorker/PhotogrammetryWorker.dll
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable photogrammetry-worker
sudo systemctl start photogrammetry-worker
sudo systemctl status photogrammetry-worker
```

## Multiple Workers

You can run multiple workers on different machines for parallel processing:

1. Each worker connects to same RabbitMQ
2. Each worker accesses shared storage
3. RabbitMQ distributes projects automatically
4. Workers process different projects simultaneously

**Example Setup**:
- Worker 1: NVIDIA RTX 3080, 32GB RAM
- Worker 2: NVIDIA RTX 4090, 64GB RAM
- Worker 3: NVIDIA A100, 80GB RAM

All connect to:
- RabbitMQ: `rabbitmq.company.com`
- Storage: NFS `/shared/models`

## Monitoring

### Console Output

```
🚀 Photogrammetry Worker Starting...
   RabbitMQ: localhost:5672
   COLMAP: colmap
   Models Path: models
   Press Ctrl+C to stop

✅ Worker connected to RabbitMQ
   Queue: photogrammetry-queue
   Waiting for projects...

📸 Processing Project 1
   Started at: 2026-01-02 18:30:00
   Step 1/7: Feature extraction...
   Step 2/7: Feature matching...
   ...
✅ Project 1 completed!
   Output: models/project_1/output/dense/meshed-poisson.ply
   Finished at: 2026-01-02 18:37:23
```

### GPU Monitoring

```bash
watch -n 1 nvidia-smi
```

Monitor VRAM usage and GPU utilization during processing.

## Troubleshooting

### Worker Can't Connect to RabbitMQ

**Error**: Connection refused

**Solution**:
```bash
# Check RabbitMQ is running
systemctl status rabbitmq-server

# Check network connectivity
ping rabbitmq-server
telnet rabbitmq-server 5672
```

### COLMAP Not Found

**Error**: COLMAP command failed

**Solution**:
```bash
# Verify COLMAP installed
which colmap
colmap -h

# If not in PATH, specify full path in appsettings.json
"Colmap": { "ExecutablePath": "/opt/colmap/bin/colmap" }
```

### Storage Access Issues

**Error**: Project directory not found

**Solution**:
```bash
# Check mount
df -h
ls -la /mnt/models/

# Check permissions
sudo chown -R worker:worker /mnt/models/
```

### GPU Not Used

**Problem**: Processing very slow

**Solution**:
```bash
# Check CUDA
nvidia-smi

# Check COLMAP has CUDA
colmap -h | grep CUDA

# Reinstall COLMAP with CUDA support if needed
```

## Performance

### Processing Times (with GPU)

| Images | GPU | Time |
|--------|-----|------|
| 10 | RTX 3080 | ~7 min |
| 50 | RTX 3080 | ~25 min |
| 100 | RTX 3080 | ~50 min |
| 500 | RTX 4090 | ~2 hours |
| 1000 | A100 | ~4 hours |

### Resource Usage

- **CPU**: Moderate (feature extraction, matching)
- **GPU**: High during dense stereo (step 5)
- **RAM**: 4-8GB typical, up to 16GB for large projects
- **VRAM**: 4-8GB typical, up to 12GB for large projects
- **Disk**: 2-5x input size for temporary files

## Architecture

```
API Server (No GPU needed)
    ↓
RabbitMQ Queue
    ↓
Worker 1 (GPU) ← Shared Storage → Worker 2 (GPU) → Worker 3 (GPU)
```

## Security

- Worker runs read-only from queue
- No network exposure required
- Processes local files only
- No user authentication (trusts queue)

## Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Publish for deployment
dotnet publish -c Release -o /opt/PhotogrammetryWorker
```

## Testing

With API running, upload a project. Worker should automatically:
1. Detect new project
2. Process images
3. Generate 3D model
4. Complete and wait for next project

Monitor worker console for output.

---

**This worker is designed to run on GPU-enabled machines separate from the API server.**
