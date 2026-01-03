# 📦 Preparing Test Data

## Overview

Test ZIP files are **not included in the repository** to keep it lightweight. You need to create them from your image dataset before running tests.

## Prerequisites

You need a dataset of images for testing. We recommend the **COLMAP South Building dataset**:

- **Download**: https://colmap.github.io/datasets.html
- **Images**: 128 high-quality JPG photos
- **Size**: ~221MB compressed

Or use your own drone/photogrammetry images.

---

## Creating Test ZIPs

### Option 1: Automated Script

```bash
./create_test_zips.sh /path/to/your/images
```

This will create:
- `test_small.zip` (10 images for quick testing)
- `south_building_full.zip` (all images for full testing)

### Option 2: Manual Creation

#### Quick Test Dataset (10 images)

```bash
cd /path/to/your/images
zip test_small.zip image001.jpg image002.jpg image003.jpg image004.jpg image005.jpg \
                    image006.jpg image007.jpg image008.jpg image009.jpg image010.jpg
mv test_small.zip /path/to/PhotogrammetryAPI/
```

#### Full Dataset (All images)

```bash
cd /path/to/your/images
zip -r south_building_full.zip *.jpg *.JPG
mv south_building_full.zip /path/to/PhotogrammetryAPI/
```

---

## Using COLMAP South Building Dataset

### Download and Extract

```bash
# Download dataset (adjust URL based on COLMAP's current hosting)
wget https://demuc.de/colmap/datasets/south-building.zip

# Extract
unzip south-building.zip
cd south-building/images
```

### Create Test ZIPs

```bash
# Small test (first 10 images)
zip ../../PhotogrammetryAPI/test_small.zip P1180141.JPG P1180142.JPG P1180143.JPG \
    P1180144.JPG P1180145.JPG P1180146.JPG P1180147.JPG P1180148.JPG \
    P1180149.JPG P1180150.JPG

# Full dataset (all 128 images)
zip -r ../../PhotogrammetryAPI/south_building_full.zip *.JPG
```

---

## Verify Test Data

After creating the ZIPs:

```bash
cd PhotogrammetryAPI

# Check files exist
ls -lh test_small.zip south_building_full.zip

# Expected output:
# test_small.zip            ~18MB   (10 images)
# south_building_full.zip   ~221MB  (128 images)

# Verify contents
unzip -l test_small.zip | head -15
unzip -l south_building_full.zip | head -15
```

---

## Quick Setup Script

Create this helper script:

```bash
#!/bin/bash
# File: create_test_zips.sh

IMAGES_DIR=${1:-"../images"}

if [ ! -d "$IMAGES_DIR" ]; then
    echo "❌ Images directory not found: $IMAGES_DIR"
    exit 1
fi

cd "$IMAGES_DIR"

# Count images
IMAGE_COUNT=$(ls *.jpg *.JPG 2>/dev/null | wc -l)
echo "Found $IMAGE_COUNT images in $IMAGES_DIR"

if [ $IMAGE_COUNT -lt 10 ]; then
    echo "❌ Need at least 10 images for testing"
    exit 1
fi

# Create small test ZIP (first 10 images)
echo "Creating test_small.zip (10 images)..."
ls *.jpg *.JPG 2>/dev/null | head -10 | zip -@ ../PhotogrammetryAPI/test_small.zip

# Create full test ZIP (all images)
echo "Creating south_building_full.zip ($IMAGE_COUNT images)..."
zip -q -r ../PhotogrammetryAPI/south_building_full.zip *.jpg *.JPG 2>/dev/null

cd ../PhotogrammetryAPI

echo ""
echo "✅ Test ZIPs created:"
ls -lh test_small.zip south_building_full.zip

echo ""
echo "Ready to test!"
echo "  Quick test:  ./quick_test.sh"
echo "  Full test:   ./test_full_dataset.sh"
```

Make it executable:
```bash
chmod +x create_test_zips.sh
```

---

## Alternative: Use Your Own Images

### Requirements

- **Format**: JPG, JPEG, or PNG
- **Minimum**: 10 images (20+ recommended)
- **Quality**: Good overlap between images
- **Resolution**: Higher is better (3000x2000+ recommended)

### Tips for Good Results

1. **Overlap**: 60-80% overlap between consecutive images
2. **Coverage**: Capture object/scene from multiple angles
3. **Lighting**: Consistent lighting across all images
4. **Focus**: Sharp, in-focus images
5. **Avoid**: Motion blur, lens flare, extreme reflections

---

## File Sizes

| Dataset | Images | Compressed | Uncompressed | Processing Time |
|---------|--------|------------|--------------|-----------------|
| Quick Test | 10 | ~18MB | ~60MB | ~10 minutes |
| Full Test | 128 | ~221MB | ~800MB | ~2-4 hours |
| Custom (50) | 50 | ~90MB | ~300MB | ~30 minutes |
| Custom (500) | 500 | ~900MB | ~3GB | ~4-8 hours |

*Processing times assume GPU acceleration*

---

## Troubleshooting

### "No such file" when running tests

**Problem**: Test ZIPs don't exist

**Solution**: Create them using this guide

### "Not enough images"

**Problem**: ZIP has too few images

**Solution**: COLMAP needs at least 3 images, but 10+ is recommended for good results

### Images not recognized

**Problem**: Wrong file format or corrupt images

**Solution**: 
- Ensure JPG/JPEG/PNG format
- Check files aren't corrupted: `file *.jpg`
- Verify images open correctly

---

## Ready to Test!

Once you have the test ZIPs:

```bash
# Quick test (10 images, ~10 minutes)
./quick_test.sh

# Full test (128 images, ~2-4 hours)
./test_full_dataset.sh
```

---

**Note**: Test data is excluded from Git (via .gitignore) to keep the repository lightweight. You only need to create it once per development environment.
