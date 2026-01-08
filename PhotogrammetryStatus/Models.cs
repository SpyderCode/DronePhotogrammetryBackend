namespace PhotogrammetryStatus;

public class WorkerStatus
{
    public string WorkerId { get; set; } = "";
    public int? CurrentProjectId { get; set; }
    public string Status { get; set; } = "Idle";
    public string CurrentStep { get; set; } = "";
    public DateTime LastUpdate { get; set; }
    public DateTime? ProcessingStarted { get; set; }
    public int ImageCount { get; set; }
    public string? DetailedMessage { get; set; }
}

public class ProjectInfo
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? WorkerId { get; set; }
    public string? CurrentStep { get; set; }
    public DateTime? QueuedAt { get; set; }
    public DateTime? ProcessingStarted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ImageCount { get; set; }
    public string? Error { get; set; }
}

public class StatusMessage
{
    public int ProjectId { get; set; }
    public string Status { get; set; } = "";
    public string? WorkerId { get; set; }
    public string? CurrentStep { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
    public int? ImageCount { get; set; }
}
