# Logging Implementation Summary

## What Changed

Replaced all `Console.WriteLine()` calls with **Serilog** structured logging throughout the application.

## Benefits

1. **Professional Logging**: Industry-standard logging framework
2. **Configurable Verbosity**: Control log levels via configuration files
3. **File Output**: Automatic daily log files with 7-day retention
4. **Structured Logs**: Better for parsing and analysis
5. **Better Performance**: Efficient async logging
6. **Filtering**: Control verbosity by namespace/component

## Files Modified

### PhotogrammetryWorker
- `Program.cs` - Added Serilog configuration
- `ColmapWorkerService.cs` - Replaced Console.WriteLine with ILogger
- `appsettings.json` - Added logging configuration
- **New**: `logs/worker-YYYYMMDD.log` files

### PhotogrammetryAPI
- `Program.cs` - Added Serilog configuration
- `appsettings.json` - Updated logging configuration
- **New**: `logs/api-YYYYMMDD.log` files

### Root
- `.gitignore` - Added `logs/` directory
- **New**: `LOGGING.md` - Documentation

## NuGet Packages Added

Both projects now include:
- `Serilog.AspNetCore` (10.0.0)
- `Serilog.Sinks.Console` (6.1.1)
- `Serilog.Sinks.File` (7.0.0)

## Default Configuration

**Console Output**: Clean, color-coded format
```
[12:34:56 INF] Worker connected to RabbitMQ
[12:34:57 INF] Processing Project 12 started at 2026-01-04 12:34:57
[12:35:00 WRN] COLMAP: Warning message
```

**File Output**: Full timestamps with date
```
2026-01-04 12:34:56.789 +00:00 [INF] Worker connected to RabbitMQ
2026-01-04 12:34:57.123 +00:00 [INF] Processing Project 12 started at 2026-01-04 12:34:57
```

## Log Levels by Component

### Worker
- **Information**: Project processing, status updates, milestones
- **Warning**: COLMAP warnings, non-fatal issues
- **Error**: Processing failures, exceptions
- **Debug**: Detailed COLMAP output (disabled by default)

### API
- **Information**: HTTP requests, project uploads, status changes
- **Warning**: Validation failures, minor issues
- **Error**: Database errors, exceptions

## Quick Changes

### See More Detail (Debug Level)
Edit `appsettings.json`:
```json
"Default": "Debug"
```

### See Less (Warnings Only)
Edit `appsettings.json`:
```json
"Default": "Warning"
```

## Testing

Both projects build successfully with the new logging system:
```bash
cd PhotogrammetryWorker && dotnet build  # ✓ Success
cd PhotogrammetryAPI && dotnet build     # ✓ Success
```

## Next Steps

1. Run the worker: `cd PhotogrammetryWorker && dotnet run`
2. Run the API: `cd PhotogrammetryAPI && dotnet run`
3. Check logs in `logs/` directories
4. Adjust verbosity in `appsettings.json` as needed

## Documentation

See `LOGGING.md` for:
- Detailed configuration options
- Log level explanations
- Examples of adjusting verbosity
- Log file locations and rotation
- Best practices
