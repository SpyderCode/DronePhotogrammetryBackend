# PhotogrammetryStatus Dashboard Implementation

## Overview

A new real-time monitoring console application has been added to the Drone Photogrammetry Backend system. This dashboard provides detailed visibility into worker activities and project processing without interfering with the main workflow.

## Key Features

### 1. **Real-Time Worker Monitoring**
- Displays all connected workers with unique IDs (e.g., `worker-hostname-abc12345`)
- Shows current status: Idle or Processing
- Tracks which project each worker is processing
- Displays detailed step information (e.g., "Step 3/7: Sparse reconstruction")
- Shows elapsed time for current tasks
- Displays image count for active projects

### 2. **Project Tracking**
- Monitors up to 15 most recent projects
- Shows project status: InQueue, Processing, Completed, or Failed
- Links projects to their assigned workers
- Displays current processing step with detailed descriptions
- Tracks total processing time
- Shows when projects entered the queue

### 3. **System Statistics**
- Active vs. total workers
- Queued projects count
- Currently processing projects
- Completed projects count
- Failed projects count

### 4. **Beautiful UI**
- Uses Spectre.Console for elegant terminal output
- Color-coded status indicators
- Auto-refreshing dashboard (1-second intervals)
- Organized tables with clear headers
- Responsive layout

## Architecture Changes

### New Message Queue
A new RabbitMQ queue `photogrammetry-status-verbose` has been added:
- **Purpose**: Carries detailed status updates from workers
- **Scope**: Only used by PhotogrammetryStatus dashboard
- **Non-persistent**: Messages are not stored (reduces overhead)
- **Independent**: Does not affect API or database

### Worker Updates
The `PhotogrammetryWorker` now publishes verbose status updates at each processing step:
1. Starting
2. Preparing images
3. Images counted
4. Step 1/7: Feature extraction
5. Step 2/7: Feature matching
6. Step 3/7: Sparse reconstruction
7. Step 4/7: Image undistortion
8. Step 5/7: Dense stereo matching
9. Step 6/7: Stereo fusion
10. Step 7/7: Delaunay meshing
11. Finished

Each update includes:
- Project ID
- Worker ID
- Status
- Current step description
- Detailed message
- Image count
- Timestamp

### Separation of Concerns
- **API**: Still only shows high-level status (InQueue, Processing, Completed, Failed)
- **Database**: Unchanged - still stores only high-level project status
- **Dashboard**: Gets detailed step-by-step information via separate queue

## Technical Implementation

### Technologies
- **.NET 8.0**: Console application
- **RabbitMQ.Client 7.2.0**: Message queue consumer
- **Spectre.Console 0.54.0**: Terminal UI framework
- **Newtonsoft.Json 13.0.4**: JSON deserialization
- **Microsoft.Extensions.Configuration**: Configuration management

### Data Models
```csharp
WorkerStatus {
    WorkerId, CurrentProjectId, Status, CurrentStep,
    LastUpdate, ProcessingStarted, ImageCount, DetailedMessage
}

ProjectInfo {
    ProjectId, ProjectName, Status, WorkerId, CurrentStep,
    QueuedAt, ProcessingStarted, CompletedAt, ImageCount, Error
}

StatusMessage {
    ProjectId, Status, WorkerId, CurrentStep, Message,
    Timestamp, ImageCount
}
```

### Configuration
```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "StatusQueue": "photogrammetry-status-verbose",
    "WorkQueue": "photogrammetry-queue"
  }
}
```

## Usage

### Starting the Dashboard
```bash
cd PhotogrammetryStatus
dotnet run
```

### What You'll See
```
═══════════════════ Photogrammetry Status Dashboard ═══════════════════

╭──────────────┬──────────┬─────────────────┬───────────────┬──────────┬────────┬─────────╮
│  Worker ID   │  Status  │ Current Project │ Current Step  │ Elapsed  │ Images │ Last Up │
├──────────────┼──────────┼─────────────────┼───────────────┼──────────┼────────┼─────────┤
│ worker-pc-1  │ Processing│       23        │ Step 5/7: ... │  45m 12s │  328   │ 14:32:01│
╰──────────────┴──────────┴─────────────────┴───────────────┴──────────┴────────┴─────────╯

╭────────────┬──────────┬─────────┬──────────────────────┬────────┬─────────────┬───────────╮
│ Project ID │  Status  │ Worker  │    Current Step      │ Images │  Time       │ Queued At │
├────────────┼──────────┼─────────┼──────────────────────┼────────┼─────────────┼───────────┤
│     23     │Processing│worker-1 │ Dense stereo match...│  328   │    45m 12s  │ 13:46:49  │
│     22     │Completed │worker-1 │ Finished             │  150   │    1h 23m   │ 12:23:37  │
╰────────────┴──────────┴─────────┴──────────────────────┴────────┴─────────────┴───────────╯

Active Workers:     1/1
Queued Projects:    0
Processing:         1
Completed:          15
Failed:             2

Last updated: 2026-01-07 14:32:15 | Press Ctrl+C to exit
```

## Benefits

1. **Operations Monitoring**: See exactly what each worker is doing at any moment
2. **Performance Tracking**: Identify slow steps or bottlenecks
3. **Debugging**: Quickly spot where failures occur
4. **Capacity Planning**: Monitor worker utilization and queue depth
5. **No Impact**: Read-only monitoring doesn't affect processing

## Future Enhancements

Possible additions:
- Historical charts (processing time trends)
- Alert notifications for failures
- Worker health checks
- Estimated time remaining
- Resource usage (CPU, GPU, memory)
- Multiple queue monitoring
- Export logs/statistics

## Notes

- The dashboard is completely optional - the system works without it
- It's designed for administrators/operators, not end users
- Multiple dashboard instances can run simultaneously
- The worker IDs include hostname for multi-machine deployments
- Status messages are non-persistent to reduce RabbitMQ overhead
