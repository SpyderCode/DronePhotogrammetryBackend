# Photogrammetry Worker

GPU-accelerated COLMAP worker for processing photogrammetry projects. Runs independently on GPU-enabled machines.

## Purpose

This standalone application:
- Connects to RabbitMQ queue
- Processes photogrammetry projects with COLMAP
- Uses GPU acceleration (CUDA) for fast reconstruction
- Sends status updates back to API
- Operates independently from API server

## Requirements

### Essential
- .NET 8.0 SDK
- **COLMAP 3.14+** (https://colmap.github.io/)
- **NVIDIA GPU** with CUDA 11.0+ (8GB+ VRAM recommended)
- RabbitMQ access (network connection)
- Shared storage with API (NFS, SMB, or same machine)

### System Recommendations
- 16GB+ RAM
- 8+ CPU cores  
- 50GB+ free disk space per project
- 10 Gigabit network (for remote storage)

## Installation

### 1. Install COLMAP

**Ubuntu/Debian**:
```bash
sudo apt update
sudo apt install colmap
```

**Or download prebuilt**:
```bash
# From https://colmap.github.io/install.html
wget https://github.com/colmap/colmap/releases/download/3.14/COLMAP-3.14-linux-cuda.tar.gz
tar -xzf COLMAP-3.14-linux-cuda.tar.gz
sudo mv COLMAP-3.14 /opt/colmap

# Add to PATH
echo 'export PATH="/opt/colmap/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

### 2. Verify GPU/CUDA

```bash
# Check GPU
nvidia-smi

# Verify COLMAP has CUDA support
colmap -h | grep CUDA
```

### 3. Configure

Edit `appsettings.json`:

```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": "5672",
    "Username": "guest",
    "Password": "guest"
  },
  "Storage": {
    "ProjectsPath": "../Projects",
    "LocalCachePath": "/tmp/colmap_cache"
  },
  "Colmap": {
    "ExecutablePath": "colmap",
    "Quality": {
      "SiftMaxNumFeatures": 16384,
      "SiftFirstOctave": -1,
      "StereoMaxImageSize": 3200,
      "StereoWindowRadius": 5,
      "StereoWindowStep": 1,
      "FusionMinNumPixels": 3,
      "FusionMaxReprojError": 2.0,
      "FusionMaxDepthError": 0.005
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    }
  }
}
```

### 4. Run

```bash
dotnet run
```

Worker connects to RabbitMQ and waits for projects.

## Configuration

### RabbitMQ Connection

**Same machine as API**:
```json
"RabbitMQ": {
  "Host": "localhost",
  "Port": "5672"
}
```

**Remote RabbitMQ server**:
```json
"RabbitMQ": {
  "Host": "rabbitmq.company.com",
  "Port": "5672",
  "Username": "worker",
  "Password": "secure_password"
}
```

### Shared Storage

**Local development** (same machine as API):
```json
"Storage": {
  "ProjectsPath": "../Projects"
}
```

**NFS mount** (production):
```json
"Storage": {
  "ProjectsPath": "/mnt/shared-projects"
}
```

Mount NFS:
```bash
sudo apt install nfs-common
sudo mkdir -p /mnt/shared-projects
sudo mount nfs-server:/exports/photogrammetry /mnt/shared-projects

# Auto-mount on boot
echo "nfs-server:/exports/photogrammetry /mnt/shared-projects nfs defaults 0 0" | sudo tee -a /etc/fstab
```

**SMB/CIFS mount** (Windows network):
```bash
sudo apt install cifs-utils
sudo mkdir -p /mnt/shared-projects
sudo mount -t cifs //server/photogrammetry /mnt/shared-projects -o credentials=/etc/samba-creds

# Create /etc/samba-creds with:
# username=user
# password=pass
```

### COLMAP Path

**System-wide installation**:
```json
"Colmap": {
  "ExecutablePath": "colmap"
}
```

**Custom path**:
```json
"Colmap": {
  "ExecutablePath": "/opt/colmap/bin/colmap"
}
```

**Verify**:
```bash
which colmap
# or
/opt/colmap/bin/colmap -h
```

## COLMAP Quality Settings

The worker uses high-quality settings by default. Adjust for speed vs quality trade-off.

### Current Settings (High Quality)

```json
"Quality": {
  "SiftMaxNumFeatures": 16384,      // More features = better matching
  "SiftFirstOctave": -1,             // Higher resolution feature detection
  "StereoMaxImageSize": 3200,        // Larger = more detail
  "StereoWindowRadius": 5,           // Window size for depth estimation
  "StereoWindowStep": 1,             // Smaller = denser point cloud
  "FusionMinNumPixels": 3,           // Min pixels for point inclusion
  "FusionMaxReprojError": 2.0,       // Max reprojection error
  "FusionMaxDepthError": 0.005       // Max relative depth error
}
```

### Quality Presets

**Maximum Quality (Very Slow)**:
```json
"SiftMaxNumFeatures": 32768,
"SiftFirstOctave": -1,
"StereoMaxImageSize": 4000,
"StereoWindowStep": 1,
"FusionMinNumPixels": 2,
"FusionMaxReprojError": 1.5
```

**Medium Quality (Faster)**:
```json
"SiftMaxNumFeatures": 8192,
"SiftFirstOctave": 0,
"StereoMaxImageSize": 2000,
"StereoWindowStep": 2,
"FusionMinNumPixels": 5,
"FusionMaxReprojError": 2.5
```

**Low Quality (Fast)**:
```json
"SiftMaxNumFeatures": 4096,
"SiftFirstOctave": 0,
"StereoMaxImageSize": 1600,
"StereoWindowStep": 2,
"FusionMinNumPixels": 8,
"FusionMaxReprojError": 3.0
```

### Troubleshooting Quality

**Model too noisy/bumpy**:
- Increase `FusionMaxReprojError` (filter more aggressively)
- Increase `FusionMinNumPixels`
- Increase `StereoWindowRadius`

**Model has too few points**:
- Decrease `FusionMinNumPixels`
- Increase `SiftMaxNumFeatures`
- Decrease `StereoWindowStep` to 1
- Increase `StereoMaxImageSize`

**Processing too slow**:
- Reduce `SiftMaxNumFeatures`
- Reduce `StereoMaxImageSize`
- Increase `StereoWindowStep` to 2
- Set `SiftFirstOctave` to 0

**GPU memory issues**:
- Reduce `StereoMaxImageSize`
- Reduce `StereoWindowRadius`

## How It Works

### Processing Pipeline

1. **Receive Project**: Gets project ID from RabbitMQ queue
2. **Flatten Images**: Copies images to flat directory for COLMAP
3. **Feature Extraction**: Detects SIFT features in images
4. **Feature Matching**: Finds correspondences between images
5. **Sparse Reconstruction**: Structure-from-Motion (creates camera poses)
6. **Image Undistortion**: Prepares images for dense reconstruction
7. **Dense Stereo**: GPU-accelerated depth estimation
8. **Stereo Fusion**: Fuses depth maps into point cloud
9. **Meshing**: Creates 3D mesh (Delaunay or Poisson)
10. **Send Status**: Updates API with completion/failure
11. **Acknowledge**: Removes message from queue

### Directory Structure

```
Projects/project_{id}/
├── images/              # Input (created by API)
│   ├── IMG_001.jpg
│   ├── IMG_002.jpg
│   └── ...
├── flat_images/         # Flattened for COLMAP (worker creates)
│   ├── IMG_001.jpg
│   ├── IMG_002.jpg
│   └── ...
├── database.db          # COLMAP database (worker creates)
└── output/              # Results (worker creates)
    ├── sparse/          # Sparse reconstruction
    │   └── 0/
    └── dense/           # Dense reconstruction
        ├── images/      # Undistorted images
        ├── stereo/      # Depth maps
        ├── fused.ply    # Point cloud
        └── meshed-delaunay.ply  # Final mesh
```

### Status Updates

Worker sends status updates to API via RabbitMQ `photogrammetry-status` queue:

- **InQueue → Processing**: When worker starts
- **Processing → Completed**: When mesh generated successfully
- **Processing → Failed**: When error occurs

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
ExecStart=/usr/bin/dotnet PhotogrammetryWorker.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production

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

View logs:
```bash
sudo journalctl -u photogrammetry-worker -f
```

## Multiple Workers

Run multiple workers on different GPU machines for parallel processing:

### Setup

**Worker 1** (RTX 3080, 32GB RAM):
```bash
cd PhotogrammetryWorker
dotnet run
```

**Worker 2** (RTX 4090, 64GB RAM):
```bash
cd PhotogrammetryWorker
dotnet run
```

**Worker 3** (A100, 80GB RAM):
```bash
cd PhotogrammetryWorker
dotnet run
```

### Load Distribution

RabbitMQ automatically distributes work:
- Each worker gets one project at a time (`prefetchCount=1`)
- Fair dispatch prevents any worker from hogging jobs
- If worker crashes, project returns to queue for another worker

### Example

Queue: [Project1, Project2, Project3, Project4, Project5]

```
Time    Worker1         Worker2         Worker3         Queue
00:00   Project1        Project2        Project3        [4, 5]
00:30   Processing...   Processing...   Processing...   [4, 5]
01:00   Completed       Processing...   Processing...   [4, 5]
01:01   Project4        Processing...   Processing...   [5]
01:30   Processing...   Completed       Processing...   [5]
01:31   Processing...   Project5        Processing...   []
02:00   Completed       Processing...   Completed       []
02:30   Idle            Completed       Idle            []
```

## Monitoring

### Console Output

```
[09:00:00 INF] Worker starting...
[09:00:00 INF] Connected to RabbitMQ: localhost:5672
[09:00:00 INF] Shared storage: /mnt/projects
[09:00:00 INF] Waiting for projects...

[09:05:23 INF] Processing project 5 (128 images)
[09:05:23 INF] Status: Processing
[09:06:15 INF] Feature extraction completed
[09:12:45 INF] Feature matching completed (15234 matches)
[09:18:32 INF] Sparse reconstruction completed (125 cameras)
[09:45:21 INF] Dense stereo completed (1.2M points)
[10:02:15 INF] Mesh generated: meshed-delaunay.ply
[10:02:16 INF] Status: Completed
[10:02:16 INF] Project 5 completed successfully

[10:02:17 INF] Waiting for projects...
```

### Log Files

Logs written to:
- Console (structured, color-coded)
- `Logs/worker-{date}.log` (rolling daily)

Configure verbosity in `appsettings.json`:
```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information"  // Debug, Information, Warning, Error
  }
}
```

### GPU Monitoring

```bash
# Real-time GPU usage
watch -n 1 nvidia-smi

# Log GPU usage
nvidia-smi dmon -s ucm -i 0 -f gpu_usage.log
```

Monitor during dense stereo step (highest GPU usage).

### Performance Metrics

Check logs for timing:
```
Feature extraction: 00:00:52
Feature matching: 00:06:30
Sparse reconstruction: 00:05:47
Dense stereo: 00:26:49
Mesh generation: 00:02:43
Total: 00:42:41
```

## Troubleshooting

### Worker Can't Connect to RabbitMQ

**Error**: "Connection refused"

**Solutions**:
```bash
# Check RabbitMQ is accessible
telnet rabbitmq-host 5672

# Ping server
ping rabbitmq-host

# Check firewall
sudo ufw status
sudo ufw allow from worker-ip to any port 5672

# Verify credentials
curl -u guest:guest http://rabbitmq-host:15672/api/overview
```

### Project Directory Not Found

**Error**: "Project directory not found: /mnt/projects/project_5"

**Solutions**:
```bash
# Check mount
df -h | grep projects
mount | grep projects

# Verify path in config matches API
cat appsettings.json | grep ProjectsPath

# Check permissions
ls -la /mnt/projects/
sudo chown -R worker:worker /mnt/projects/
```

### COLMAP Command Failed

**Error**: "COLMAP command failed with exit code 1"

**Solutions**:
```bash
# Verify COLMAP installed
which colmap
colmap -h

# Check CUDA support
colmap -h | grep CUDA
nvidia-smi

# Test COLMAP manually
colmap feature_extractor \
  --database_path test.db \
  --image_path images/

# Check worker logs for detailed error
cat Logs/worker-$(date +%Y%m%d).log
```

### GPU Not Being Used

**Problem**: Processing very slow, GPU idle

**Solutions**:
```bash
# Check CUDA available
nvidia-smi

# Verify COLMAP has CUDA
colmap -h | grep CUDA
# Should show "CUDA enabled: Yes"

# If not, reinstall COLMAP with CUDA support
```

### Out of Memory (GPU)

**Error**: "CUDA out of memory"

**Solutions**:
1. Reduce `StereoMaxImageSize`:
   ```json
   "StereoMaxImageSize": 1600
   ```
2. Process fewer images at once
3. Use GPU with more VRAM
4. Close other GPU applications

### Out of Memory (CPU)

**Error**: "System.OutOfMemoryException"

**Solutions**:
```bash
# Check RAM usage
free -h

# Add swap space
sudo fallocate -l 16G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
```

### Out of Disk Space

**Error**: "No space left on device"

**Solutions**:
```bash
# Check disk usage
df -h

# Clean old projects
rm -rf /mnt/projects/project_*/

# Move to larger volume
mv /mnt/projects /data/projects
# Update appsettings.json
```

## Performance

### Processing Times

| Images | GPU | Feature Extraction | Dense Stereo | Total |
|--------|-----|-------------------|--------------|-------|
| 10 | RTX 3080 | 30s | 5 min | ~7 min |
| 50 | RTX 3080 | 2 min | 20 min | ~25 min |
| 100 | RTX 3080 | 4 min | 40 min | ~50 min |
| 500 | RTX 4090 | 15 min | 1.5 hours | ~2 hours |
| 1000 | A100 | 30 min | 3 hours | ~4 hours |

*Times with high-quality settings*

### Resource Usage

**CPU**: Moderate during feature extraction/matching, low during GPU steps
**GPU**: High during dense stereo (steps 5-6)
**RAM**: 4-16GB depending on image count
**VRAM**: 4-12GB depending on image size and quality settings
**Disk**: 2-5x input size for temporary files

### Optimization Tips

1. **Faster Processing**:
   - Use medium quality preset
   - Reduce `StereoMaxImageSize`
   - Increase `StereoWindowStep`

2. **Better Quality**:
   - Use maximum quality preset
   - Increase `SiftMaxNumFeatures`
   - Set `StereoWindowStep` to 1

3. **Less Memory**:
   - Reduce `StereoMaxImageSize`
   - Process in batches (split large image sets)

4. **Network Performance**:
   - Use 10 Gigabit Ethernet for NFS
   - Enable NFS caching
   - Consider local SSD for temporary files

## Deployment

### Production Build

```bash
dotnet publish -c Release -o /opt/PhotogrammetryWorker

cd /opt/PhotogrammetryWorker
dotnet PhotogrammetryWorker.dll
```

### Docker (Advanced)

```dockerfile
FROM nvidia/cuda:11.8.0-cudnn8-runtime-ubuntu22.04

# Install dependencies
RUN apt-get update && apt-get install -y \
    dotnet-sdk-8.0 \
    colmap \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY publish/ .

ENTRYPOINT ["dotnet", "PhotogrammetryWorker.dll"]
```

Requires:
- NVIDIA Docker runtime
- GPU passthrough
- Shared storage volume

## Security

- Worker runs read-only from queue (no direct user access)
- No network exposure required (outbound only)
- Processes files from trusted API
- No authentication (trusts RabbitMQ queue)

**Production recommendations**:
- Run as dedicated user (not root)
- Use RabbitMQ authentication
- Secure shared storage with proper permissions
- Isolate on private network

## Project Structure

```
PhotogrammetryWorker/
├── ColmapWorkerService.cs      # Main processing logic
├── StatusUpdateService.cs      # Status messaging
├── Program.cs                  # Application startup
├── appsettings.json           # Configuration
├── Logs/                      # Log files
└── README.md                  # This file
```

---

**Worker is ready to process photogrammetry projects with GPU acceleration!**

For API setup, see `../PhotogrammetryAPI/README.md`
