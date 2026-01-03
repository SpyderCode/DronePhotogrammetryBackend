# 📁 Shared Storage Configuration

## Overview

Both the **API** and **Worker(s)** share a common `Projects` folder for storing and processing images. This folder contains all project data:
- Uploaded ZIP files
- Extracted images
- COLMAP processing files
- Generated 3D models

---

## Directory Structure

```
Projects/
├── project_1/
│   ├── images/              # Extracted from uploaded ZIP (API creates)
│   ├── flat_images/         # Flattened for COLMAP (Worker creates)
│   ├── database.db          # COLMAP database (Worker creates)
│   └── output/              # Processing results (Worker creates)
│       ├── sparse/          # Sparse reconstruction
│       └── dense/
│           ├── fused.ply    # Point cloud
│           └── meshed-poisson.ply  # Final mesh
├── project_2/
├── project_3/
└── user_1_guid123/          # Temporary ZIP storage (API creates)
    └── images_timestamp.zip
```

---

## Configuration

### API Configuration

Edit `PhotogrammetryAPI/appsettings.json`:

```json
{
  "Storage": {
    "ProjectsPath": "../Projects"
  }
}
```

**What the API does**:
1. User uploads ZIP → Saves to `Projects/user_{userId}_{guid}/`
2. Creates project → Creates `Projects/project_{projectId}/`
3. Extracts ZIP → Extracts to `Projects/project_{projectId}/images/`
4. Publishes to queue → Worker picks it up
5. Serves downloads → Reads from `Projects/project_{projectId}/output/dense/`

### Worker Configuration

Edit `PhotogrammetryWorker/appsettings.json`:

```json
{
  "Storage": {
    "ProjectsPath": "../Projects"
  }
}
```

**What the Worker does**:
1. Receives project ID from queue
2. Reads from `Projects/project_{projectId}/images/`
3. Processes with COLMAP
4. Writes to `Projects/project_{projectId}/output/`
5. Acknowledges completion

---

## Deployment Scenarios

### Scenario 1: Same Machine (Development)

```
Single Machine:
├── PhotogrammetryAPI/
├── PhotogrammetryWorker/
└── Projects/  ← Shared local directory
```

**Configuration**:
```json
// Both API and Worker
{
  "Storage": {
    "ProjectsPath": "../Projects"
  }
}
```

**Setup**:
```bash
# Create shared folder
mkdir -p /home/user/DronePhoto/Projects

# Both already configured correctly!
```

### Scenario 2: Network File System (NFS)

```
┌──────────────────┐
│   API Server     │
│  /mnt/projects   │ ← NFS Mount
└────────┬─────────┘
         │
    ┌────┴────┐
    │   NFS   │
    │ Server  │
    └────┬────┘
         │
┌────────┴─────────┐
│  Worker Server   │
│  /mnt/projects   │ ← NFS Mount
└──────────────────┘
```

**NFS Server Setup**:
```bash
# On NFS server
sudo apt install nfs-kernel-server
sudo mkdir -p /exports/photogrammetry
sudo chown -R nobody:nogroup /exports/photogrammetry
sudo chmod 777 /exports/photogrammetry

# Edit /etc/exports
echo "/exports/photogrammetry *(rw,sync,no_subtree_check,no_root_squash)" | sudo tee -a /etc/exports

sudo exportfs -ra
sudo systemctl restart nfs-kernel-server
```

**API Server**:
```bash
# Mount NFS
sudo apt install nfs-common
sudo mkdir -p /mnt/projects
sudo mount nfs-server:/exports/photogrammetry /mnt/projects

# Auto-mount on boot (add to /etc/fstab)
echo "nfs-server:/exports/photogrammetry /mnt/projects nfs defaults 0 0" | sudo tee -a /etc/fstab
```

**Worker Servers** (repeat on each):
```bash
# Same as API server
sudo apt install nfs-common
sudo mkdir -p /mnt/projects
sudo mount nfs-server:/exports/photogrammetry /mnt/projects
echo "nfs-server:/exports/photogrammetry /mnt/projects nfs defaults 0 0" | sudo tee -a /etc/fstab
```

**Configuration**:
```json
// API appsettings.json
{
  "Storage": {
    "ProjectsPath": "/mnt/projects"
  }
}

// Worker appsettings.json
{
  "Storage": {
    "ProjectsPath": "/mnt/projects"
  }
}
```

### Scenario 3: SMB/CIFS Share (Windows Network)

**Windows Server**: Share folder `\\server\photogrammetry`

**Linux API/Worker Servers**:
```bash
# Install CIFS utils
sudo apt install cifs-utils

# Create mount point
sudo mkdir -p /mnt/projects

# Mount SMB share
sudo mount -t cifs //server/photogrammetry /mnt/projects -o username=user,password=pass

# Auto-mount (create credentials file first)
sudo nano /etc/samba-credentials
# Add:
# username=user
# password=pass

sudo chmod 600 /etc/samba-credentials

# Add to /etc/fstab
echo "//server/photogrammetry /mnt/projects cifs credentials=/etc/samba-credentials 0 0" | sudo tee -a /etc/fstab
```

**Configuration**: Same as NFS scenario

### Scenario 4: Cloud Storage (Future)

For AWS S3, Azure Blob, or Google Cloud Storage:
- Modify `FileStorageService` to use cloud SDK
- Use S3 bucket/Blob container as shared storage
- Workers download/upload directly to cloud

---

## Path Formats

### Relative Paths (Development)

When API and Worker are in sibling directories:
```
DronePhoto/
├── PhotogrammetryAPI/
├── PhotogrammetryWorker/
└── Projects/
```

**Config**: `"ProjectsPath": "../Projects"`

### Absolute Paths (Production)

When API and Workers are on different machines:

**Config**: `"ProjectsPath": "/mnt/projects"` or `"ProjectsPath": "/shared/photogrammetry"`

### Windows Paths

**Config**: `"ProjectsPath": "C:\\SharedProjects"` or `"ProjectsPath": "\\\\server\\photogrammetry"`

---

## Permissions

### Linux/Unix

```bash
# Give read/write access to API and Worker users
sudo chown -R api-user:workers /mnt/projects
sudo chmod -R 775 /mnt/projects

# Or if users are different
sudo chmod -R 777 /mnt/projects  # Less secure but works
```

### NFS Exports

Ensure `no_root_squash` if services run as root, or set proper UID mapping.

### SELinux/AppArmor

May need to configure policies to allow NFS/CIFS access.

---

## Storage Requirements

### Per Project

| Images | Input Size | Processing Space | Output Size | Total |
|--------|-----------|------------------|-------------|-------|
| 10 | 18 MB | 50 MB | 5-10 MB | ~75 MB |
| 50 | 90 MB | 300 MB | 50-100 MB | ~500 MB |
| 100 | 200 MB | 800 MB | 100-200 MB | ~1.2 GB |
| 500 | 900 MB | 3 GB | 500MB-1GB | ~5 GB |
| 1000 | 2 GB | 8 GB | 1-2 GB | ~12 GB |

### Recommended Storage

- **Small deployments** (10-50 projects/month): 50-100 GB
- **Medium deployments** (100-500 projects/month): 500 GB - 1 TB
- **Large deployments** (1000+ projects/month): 2-10 TB

### Cleanup Strategy

Implement periodic cleanup of old projects:
```bash
# Delete projects older than 30 days
find /mnt/projects/project_* -mtime +30 -exec rm -rf {} \;
```

---

## Testing Configuration

### Test Shared Access

**From API server**:
```bash
cd Projects
touch test_from_api.txt
ls -la
```

**From Worker server**:
```bash
cd /mnt/projects  # or wherever mounted
ls -la  # Should see test_from_api.txt
touch test_from_worker.txt
```

**Back on API server**:
```bash
cd Projects
ls -la  # Should see test_from_worker.txt
```

If both files are visible, shared storage is working!

### Test Performance

```bash
# Write speed test
dd if=/dev/zero of=/mnt/projects/testfile bs=1M count=1024
# Should be >50 MB/s for good performance

# Read speed test
dd if=/mnt/projects/testfile of=/dev/null bs=1M
```

---

## Troubleshooting

### "Project directory not found"

**Problem**: Worker can't find `Projects/project_X`

**Solutions**:
1. Check paths match in both `appsettings.json`
2. Verify mount is active: `df -h | grep projects`
3. Check permissions: `ls -la /mnt/projects`
4. Test cross-visibility (see Testing section)

### "Permission denied"

**Problem**: Can't write to shared folder

**Solutions**:
```bash
# Check ownership
ls -ld /mnt/projects

# Fix permissions
sudo chown -R user:group /mnt/projects
sudo chmod -R 775 /mnt/projects
```

### NFS Mount Stale

**Problem**: Mount becomes unresponsive

**Solution**:
```bash
# Unmount and remount
sudo umount -f /mnt/projects
sudo mount /mnt/projects

# Or reboot
sudo reboot
```

### Performance Issues

**Problem**: Processing is slow due to network storage

**Solutions**:
1. Use faster network (10 Gigabit Ethernet)
2. Use NFS v4 instead of v3
3. Consider local SSD for worker temporary files
4. Use caching NFS options: `mount -o nfs4,nolock,async`

---

## Security

### Network Isolation

- Keep NFS/SMB traffic on private network
- Don't expose NFS to internet
- Use VPN for remote workers

### Encryption

**NFS with Kerberos**:
```bash
# More secure but complex setup
mount -t nfs4 -o sec=krb5p server:/export /mnt/projects
```

**SMBFS with encryption**:
```bash
mount -t cifs //server/share /mnt/projects -o seal,username=user
```

### Access Control

- Limit NFS exports to specific IP ranges
- Use strong SMB passwords
- Implement firewall rules

---

## Monitoring

### Disk Space

```bash
# Check available space
df -h /mnt/projects

# Check project sizes
du -sh /mnt/projects/project_*
```

### Mount Status

```bash
# Check if mounted
mount | grep projects

# Monitor mount points
watch -n 5 'df -h | grep projects'
```

---

## Summary

✅ **Both API and Worker use same `ProjectsPath` configuration**  
✅ **API extracts images to `Projects/project_{id}/images/`**  
✅ **Worker reads from there and writes output**  
✅ **Can be local directory or network mount (NFS/SMB)**  
✅ **Proper permissions and connectivity are crucial**  

---

**With shared storage configured, your system can scale across multiple machines!**
