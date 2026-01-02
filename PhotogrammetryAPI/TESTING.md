# 🧪 Test Scripts Guide

## Available Test Scripts

### 1. Quick Test (Small Dataset) - `quick_test.sh`

**Purpose**: Fast test with 10 images to verify system is working

**Dataset**: 
- 10 images (P1180141-P1180150)
- 18MB compressed
- ZIP: `test_small.zip`

**Expected Time**: 7-10 minutes

**Usage**:
```bash
./quick_test.sh
```

**What it does**:
1. Registers a test user
2. Uploads 10 images
3. Polls status every 5 seconds (up to 15 minutes)
4. Downloads model when complete

---

### 2. Full Dataset Test - `test_full_dataset.sh`

**Purpose**: Test with complete South Building dataset

**Dataset**:
- 128 images (all South Building JPGs)
- 221MB compressed
- ZIP: `south_building_full.zip`

**Expected Time**: 2-4 hours (with GPU)
- Feature extraction: ~5 minutes
- Feature matching: ~3 minutes
- Sparse reconstruction: ~2 minutes
- Image undistortion: ~2 minutes
- **Dense stereo (GPU)**: ~1.5-2 hours ⭐
- Stereo fusion: ~5 minutes
- Poisson meshing: ~3 minutes

**Usage**:
```bash
./test_full_dataset.sh
```

**What it does**:
1. Shows estimated time and confirms you want to proceed
2. Registers a test user
3. Uploads 221MB of images
4. Polls status every 30 seconds (up to 4 hours)
5. Downloads model when complete
6. Shows statistics

**Tips**:
- You can press Ctrl+C and monitor separately
- Script will show command to check status manually
- Use `monitor_project.sh` if you cancel

---

### 3. Monitor Project - `monitor_project.sh`

**Purpose**: Monitor an already-uploaded project

**Usage**:
```bash
./monitor_project.sh PROJECT_ID TOKEN

# Example:
./monitor_project.sh 5 eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**What it does**:
- Checks status every 30 seconds
- Shows elapsed time
- Alerts when finished or failed
- Provides download command

**How to get TOKEN**:
1. From test script output (shown after registration)
2. Or login manually:
```bash
curl -X POST http://localhost:5273/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"your@email.com","password":"YourPass123!"}' \
  | jq -r '.token'
```

---

## Test Data Files

### Current Test Files

| File | Size | Images | Description |
|------|------|--------|-------------|
| `test_small.zip` | 18MB | 10 | Quick test (symlink) |
| `south_building_full.zip` | 221MB | 128 | Complete dataset |
| `test_images.zip` | - | - | Symlink (points to test_small.zip) |

### Switching Test Data

```bash
# Use small dataset (default)
ln -sf test_small.zip test_images.zip

# Use full dataset
ln -sf south_building_full.zip test_images.zip

# Create custom subset
cd ../images
zip custom.zip P1180141.JPG P1180142.JPG P1180143.JPG
mv custom.zip ../PhotogrammetryAPI/
```

---

## Manual Testing

### 1. Register User
```bash
BASE_URL="http://localhost:5273"

RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"username":"myuser","email":"my@email.com","password":"Pass123!"}')

TOKEN=$(echo $RESPONSE | jq -r '.token')
echo "Token: $TOKEN"
```

### 2. Upload Images
```bash
curl -X POST "$BASE_URL/api/projects/upload" \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=MyProject" \
  -F "ZipFile=@south_building_full.zip" \
  --max-time 600
```

### 3. Check Status
```bash
PROJECT_ID=5  # Use your project ID

curl "$BASE_URL/api/projects/$PROJECT_ID/status" \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.'
```

### 4. Download Model (when finished)
```bash
curl "$BASE_URL/api/projects/$PROJECT_ID/download" \
  -H "Authorization: Bearer $TOKEN" \
  --output model.ply
```

---

## Expected Results

### Quick Test (10 images)
```
Processing time: ~7-10 minutes
Output size: ~5-10 MB PLY file
Point count: ~500K-1M points
Status checks: ~80-120 (5 sec intervals)
```

### Full Test (128 images)
```
Processing time: ~2-4 hours
Output size: ~100-500 MB PLY file
Point count: ~10-50M points
Status checks: ~240-480 (30 sec intervals)
```

---

## Monitoring API Logs

While tests are running, monitor the API output:

```bash
# API runs in foreground, watch console output

# Or if running as service:
tail -f /var/log/photogrammetry-api.log

# Watch for COLMAP progress:
# - "Feature extraction complete"
# - "Mapper: Registered X images"
# - "Writing output: fused.ply"
# - "Mesh generated successfully"
```

---

## Troubleshooting

### Test Script Fails to Connect

**Problem**: `curl: (7) Failed to connect`

**Solution**:
```bash
# Check API is running
curl http://localhost:5273/swagger

# Start if needed
cd PhotogrammetryAPI && dotnet run
```

### Upload Times Out

**Problem**: Upload fails after several minutes

**Solution**:
```bash
# Increase timeout in curl
curl --max-time 1200 ...  # 20 minutes

# Or check network
ping localhost
```

### Processing Stuck

**Problem**: Status stays "Processing" for hours beyond expected time

**Solution**:
```bash
# Check API logs for errors
# Look for COLMAP error messages

# Check if COLMAP is running
ps aux | grep colmap

# Check disk space
df -h

# Restart API if needed
# (project will auto-resume from queue)
```

### Model Download is Small

**Problem**: Downloaded PLY file is only a few KB

**Solution**:
- Small file likely means processing failed
- Check project status for error message
- Review API logs for COLMAP errors
- Ensure GPU is available: `nvidia-smi`

---

## Performance Tips

### Speed Up Testing

1. **Use Quick Test First**: Always test with 10 images before full dataset
2. **GPU is Essential**: Dense reconstruction needs GPU (5-10x faster)
3. **Monitor Separately**: Use `monitor_project.sh` instead of waiting
4. **Parallel Processing**: Upload multiple projects (queue handles them)

### Optimize Full Test

1. **Check GPU Usage**: `watch nvidia-smi` while processing
2. **Use SSD Storage**: Faster disk I/O helps
3. **Close Other Apps**: Free GPU memory
4. **Run Overnight**: For very large datasets

---

## Comparing Results

### View PLY Files

**MeshLab** (recommended):
```bash
sudo apt install meshlab
meshlab model.ply
```

**CloudCompare**:
```bash
sudo snap install cloudcompare
cloudcompare.CloudCompare model.ply
```

**Blender**:
```bash
sudo snap install blender --classic
blender
# File > Import > Stanford (.ply)
```

### Check Point Count
```bash
# Count vertices in PLY file
grep "element vertex" model.ply
```

### Compare Sizes
```bash
ls -lh model_*.ply
```

---

## Cleanup

### Remove Test Files
```bash
# Remove test models
rm -f model_*.ply model_*.obj

# Remove test uploads (keep ZIPs)
# Check database for project IDs first
curl http://localhost:5273/api/projects \
  -H "Authorization: Bearer $TOKEN"

# Clean old model directories
rm -rf models/project_*/

# Keep ZIPs for future tests
```

### Reset Database (if needed)
```bash
# WARNING: Deletes all projects and users
docker exec photogrammetry_mysql mysql -uroot -prootpassword \
  -e "DROP DATABASE photogrammetry; CREATE DATABASE photogrammetry;"

# Re-run setup
docker exec -i photogrammetry_mysql mysql -uroot -prootpassword photogrammetry \
  < database_setup.sql
```

---

## Summary

**Quick Test**: Use `quick_test.sh` for fast verification (10 min)

**Full Test**: Use `test_full_dataset.sh` for complete dataset (2-4 hours)

**Monitor**: Use `monitor_project.sh` to watch any project

All scripts are production-ready and handle errors gracefully!

---

**Happy Testing!** 🚀
