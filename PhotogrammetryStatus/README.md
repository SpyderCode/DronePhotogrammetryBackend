# Photogrammetry Status Dashboard

A real-time monitoring console application that displays the status of photogrammetry workers and projects.

## Features

- **Real-time Worker Monitoring**: See which workers are active and what they're processing
- **Project Tracking**: Monitor all projects from queue to completion
- **Detailed Step Information**: View current processing step for each worker
- **Statistics**: Get quick overview of active workers, queued projects, and completion rates
- **Beautiful Console UI**: Uses Spectre.Console for an elegant terminal dashboard

## Requirements

- .NET 8.0 or higher
- RabbitMQ server (for message queue)
- Access to the same RabbitMQ instance as the API and Workers

## Installation

```bash
cd PhotogrammetryStatus
dotnet restore
```

## Configuration

Edit `appsettings.json`:

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

## Running

```bash
dotnet run
```

The dashboard will automatically refresh every second and display:
- Active workers and their current tasks
- Recent projects (up to 15 most recent)
- Overall statistics
- Processing times and image counts

## Dashboard Information

### Workers Table
- **Worker ID**: Unique identifier for each worker
- **Status**: Idle or Processing
- **Current Project**: Project ID being processed
- **Current Step**: Detailed step information (e.g., "Step 3/7: Sparse reconstruction")
- **Elapsed Time**: How long the current task has been running
- **Images**: Number of images in the current project
- **Last Update**: When the worker last sent a status update

### Projects Table
- **Project ID**: Unique project identifier
- **Status**: InQueue, Processing, Completed, or Failed
- **Worker**: Which worker is processing this project
- **Current Step**: Current processing step
- **Images**: Number of images in project
- **Processing Time**: Total time spent processing
- **Queued At**: When the project entered the queue

### Statistics Panel
- Active Workers: Number of workers currently processing
- Queued Projects: Projects waiting to be processed
- Processing: Projects currently being processed
- Completed: Successfully completed projects
- Failed: Projects that encountered errors

## Notes

- This is a read-only monitoring tool - it does not interfere with the processing pipeline
- The dashboard only shows verbose status information, which is separate from the main API status
- Press Ctrl+C to exit the dashboard
- The dashboard maintains history of all projects seen since startup
