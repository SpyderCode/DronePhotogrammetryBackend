# Logging Configuration

This project uses **Serilog** for structured logging with configurable verbosity levels and file output.

## Features

- **Structured Logging**: JSON-formatted logs with context
- **Console Output**: Color-coded, human-readable format
- **File Output**: Daily rolling log files with 7-day retention
- **Configurable Levels**: Control verbosity per namespace
- **Separate Logs**: API and Worker have their own log files

## Log Files

### API Logs
- **Location**: `PhotogrammetryAPI/logs/api-YYYYMMDD.log`
- **Retention**: 7 days
- **Format**: Timestamped with log level and message

### Worker Logs
- **Location**: `PhotogrammetryWorker/logs/worker-YYYYMMDD.log`
- **Retention**: 7 days
- **Format**: Timestamped with log level and message

## Log Levels

From most to least verbose:
1. **Verbose** - Very detailed diagnostic information
2. **Debug** - Detailed information for debugging (COLMAP output)
3. **Information** - General informational messages (default)
4. **Warning** - Warning messages for potential issues
5. **Error** - Error messages for failures
6. **Fatal** - Critical failures that stop the application

## Configuration

### API Configuration (`PhotogrammetryAPI/appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PhotogrammetryAPI": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

### Worker Configuration (`PhotogrammetryWorker/appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PhotogrammetryWorker": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
```

## Adjusting Verbosity

### See All COLMAP Output (Debug Level)

Change Worker logging to Debug to see all COLMAP messages:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "PhotogrammetryWorker": "Debug"
    }
  }
}
```

### Minimal Output (Warning Level)

Only show warnings and errors:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "PhotogrammetryWorker": "Warning"
    }
  }
}
```

### Verbose Database Queries

Show Entity Framework SQL queries:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

## Console Output Format

The console uses a simplified format:
```
[HH:mm:ss INF] Message text
[HH:mm:ss WRN] Warning message
[HH:mm:ss ERR] Error message
```

## File Output Format

Log files include full timestamps:
```
2026-01-04 12:34:56.789 +00:00 [INF] Message text
2026-01-04 12:34:57.123 +00:00 [ERR] Error message with exception details
```

## COLMAP Output Handling

COLMAP messages are logged at different levels:
- **Debug**: All normal COLMAP output
- **Information**: Important milestones (e.g., "Elapsed time", "Writing output")
- **Warning**: COLMAP errors and warnings

## Best Practices

1. **Development**: Use `Information` level for general debugging
2. **Production**: Use `Warning` level to reduce log volume
3. **Troubleshooting**: Temporarily set to `Debug` to see detailed COLMAP output
4. **Performance**: Higher log levels (Warning/Error) have less overhead

## Log Rotation

- Logs automatically rotate daily at midnight
- Old logs are deleted after 7 days
- Manual cleanup not required

## Example: Viewing Recent Logs

```bash
# View today's API logs
tail -f PhotogrammetryAPI/logs/api-$(date +%Y%m%d).log

# View today's Worker logs
tail -f PhotogrammetryWorker/logs/worker-$(date +%Y%m%d).log

# Search for errors in last 7 days
grep -r "ERR" PhotogrammetryAPI/logs/
grep -r "ERR" PhotogrammetryWorker/logs/
```
