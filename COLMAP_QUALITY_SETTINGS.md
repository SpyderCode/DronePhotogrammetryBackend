# COLMAP Quality Settings Guide

This document explains the quality settings used in the photogrammetry worker and how to adjust them for better results.

## Overview

The worker now uses high-quality settings for COLMAP reconstruction. These settings are configurable in `PhotogrammetryWorker/appsettings.json`.

## Current Settings

### Feature Extraction
- **SiftMaxNumFeatures: 16384** (default: 8192)
  - More features = better matching and reconstruction
  - Higher values increase processing time and memory
  - Recommended: 8192-32768

- **SiftFirstOctave: -1** (default: 0)
  - Detects features at higher resolution
  - -1 doubles image resolution for feature detection
  - Better for high-resolution images

### Feature Matching
- **SiftMatchingMaxDistance: 0.7** (default: 0.7)
  - Maximum descriptor distance for matching
  - Lower = stricter matching (fewer false matches)
  - Range: 0.5-0.9

- **SiftMatchingMaxRatio: 0.8** (default: 0.8)
  - Lowe's ratio test threshold
  - Lower = more reliable matches
  - Range: 0.6-0.9

### Dense Reconstruction
- **StereoMaxImageSize: 3200** (default: 2000)
  - Maximum image dimension for dense reconstruction
  - Higher = more detail but slower processing
  - Recommended: 2000-4000

- **StereoWindowRadius: 5** (default: 5)
  - Matching window size (affects smoothness)
  - Larger = smoother but less detail
  - Range: 3-7

- **StereoWindowStep: 1** (default: 2)
  - Pixel step size for depth estimation
  - Smaller = more points but slower
  - 1 = maximum quality, 2 = good balance

- **FusionMinNumPixels: 3** (default: 5)
  - Minimum pixels for point fusion
  - Lower = more points kept (denser cloud)
  - Range: 2-10

- **FusionMaxReprojError: 2.0** (default: 2.0)
  - Maximum reprojection error in pixels
  - Lower = stricter filtering (cleaner but fewer points)
  - Range: 1.0-4.0

- **FusionMaxDepthError: 0.005** (default: 0.01)
  - Maximum relative depth error
  - Lower = stricter filtering
  - Range: 0.001-0.02

## Quality Presets

### Maximum Quality (Slow)
```json
"SiftMaxNumFeatures": 32768,
"SiftFirstOctave": -1,
"StereoMaxImageSize": 4000,
"StereoWindowRadius": 5,
"StereoWindowStep": 1,
"FusionMinNumPixels": 2,
"FusionMaxReprojError": 1.5,
"FusionMaxDepthError": 0.003
```

### High Quality (Current - Balanced)
```json
"SiftMaxNumFeatures": 16384,
"SiftFirstOctave": -1,
"StereoMaxImageSize": 3200,
"StereoWindowRadius": 5,
"StereoWindowStep": 1,
"FusionMinNumPixels": 3,
"FusionMaxReprojError": 2.0,
"FusionMaxDepthError": 0.005
```

### Medium Quality (Fast)
```json
"SiftMaxNumFeatures": 8192,
"SiftFirstOctave": 0,
"StereoMaxImageSize": 2000,
"StereoWindowRadius": 5,
"StereoWindowStep": 2,
"FusionMinNumPixels": 5,
"FusionMaxReprojError": 2.5,
"FusionMaxDepthError": 0.01
```

### Low Quality (Very Fast)
```json
"SiftMaxNumFeatures": 4096,
"SiftFirstOctave": 0,
"StereoMaxImageSize": 1600,
"StereoWindowRadius": 5,
"StereoWindowStep": 2,
"FusionMinNumPixels": 8,
"FusionMaxReprojError": 3.0,
"FusionMaxDepthError": 0.02
```

## Troubleshooting

### Model is too bumpy/noisy
- Increase `FusionMaxReprojError` (filter more aggressively)
- Increase `FusionMaxDepthError`
- Increase `FusionMinNumPixels`
- Increase `StereoWindowRadius` for smoother results

### Model has too few points/detail
- Decrease `FusionMinNumPixels`
- Increase `SiftMaxNumFeatures`
- Decrease `StereoWindowStep` to 1
- Increase `StereoMaxImageSize`

### Processing is too slow
- Reduce `SiftMaxNumFeatures`
- Reduce `StereoMaxImageSize`
- Increase `StereoWindowStep` to 2
- Set `SiftFirstOctave` to 0

### GPU memory issues
- Reduce `StereoMaxImageSize`
- Reduce `StereoWindowRadius`

## Performance Impact

| Setting | Quality Impact | Speed Impact | Memory Impact |
|---------|---------------|--------------|---------------|
| SiftMaxNumFeatures | High | Medium | Low |
| SiftFirstOctave | Medium | High | Medium |
| StereoMaxImageSize | High | Very High | Very High |
| StereoWindowStep | High | Very High | Low |
| FusionMinNumPixels | Medium | Low | Low |

## Recommended Workflow

1. Start with **High Quality** settings (current default)
2. If too slow, try **Medium Quality**
3. If results are noisy, adjust fusion parameters
4. For critical projects, use **Maximum Quality**

## Additional Resources

- [COLMAP Documentation](https://colmap.github.io/)
- [COLMAP Tutorial](https://colmap.github.io/tutorial.html)
- [COLMAP CLI Reference](https://colmap.github.io/cli.html)
