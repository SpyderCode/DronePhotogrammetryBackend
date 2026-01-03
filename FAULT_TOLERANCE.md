# 🛡️ Fault Tolerance & Error Handling

## Overview

The system implements comprehensive fault tolerance to handle worker failures, crashes, and long-running jobs without losing work.

---

## How It Works

### RabbitMQ Message Flow

```
User uploads → API publishes to Queue → Worker picks up message
                                              ↓
                                    ┌─────────┴─────────┐
                                    │   Processing      │
                                    └─────────┬─────────┘
                                              ↓
                            Success           OR          Failure
                               ↓                            ↓
                      Acknowledge (ACK)              Analyze Error
                      Remove from queue                     ↓
                                                  Retriable? / Fatal?
                                                      ↓         ↓
                                              Requeue    Dead Letter Queue
                                         (Try again)    (Manual review)
```

### Key Features

#### 1. **Manual Acknowledgment**
- Messages stay in queue until **explicitly acknowledged**
- If worker crashes before ACK, message returns to queue
- Another worker can pick it up

#### 2. **Consumer Timeout (24 hours)**
- If worker doesn't respond for 24 hours, connection drops
- Message automatically returns to queue
- Prevents stuck messages

#### 3. **Message TTL (48 hours)**
- Messages expire after 48 hours in queue
- Prevents infinite retry loops
- Expired messages go to dead letter queue

#### 4. **Dead Letter Queue**
- Failed messages route to `photogrammetry-failed` queue
- Allows manual inspection and retry
- Prevents message loss

#### 5. **Smart Retry Logic**
- Retriable errors → Requeue for another worker
- Fatal errors → Send to dead letter queue immediately
- Prevents wasted processing on unrecoverable failures

---

## Error Types

### Retriable Errors (Requeue)

These errors may succeed on a different worker or retry:

| Error | Why Retriable |
|-------|---------------|
| Project directory not found | Network storage might be temporarily unavailable |
| Connection refused | Database/service might be restarting |
| Network timeout | Temporary network issue |
| No space left on device | Other worker might have more space |
| Temporarily unavailable | Transient issue |

**Action**: Message requeued → Another worker tries

### Fatal Errors (Dead Letter Queue)

These errors won't resolve with retries:

| Error | Why Fatal |
|-------|-----------|
| COLMAP command failed | Invalid images or corrupt data |
| Invalid message format | Malformed request |
| Corrupt ZIP file | Cannot be processed |
| Permission denied | Configuration issue |

**Action**: Message rejected → Sent to dead letter queue

---

## Failure Scenarios

### Scenario 1: Worker Crashes Mid-Processing

**What happens**:
1. Worker picks up project 5 from queue
2. Worker starts COLMAP processing
3. Worker **crashes** (power loss, OOM, etc.)
4. RabbitMQ detects connection drop
5. Message **not acknowledged** → Returns to queue
6. Another worker picks up project 5
7. Processing continues

**Result**: ✅ Work is not lost

**Timeline**:
```
00:00 - Worker 1 picks up project 5
00:05 - Worker 1 crashes
00:05 - Message returns to queue
00:06 - Worker 2 picks up project 5
00:45 - Worker 2 completes project 5
```

### Scenario 2: Worker Takes Too Long (>24 hours)

**What happens**:
1. Worker starts processing massive project (10,000 images)
2. Processing takes 30 hours
3. After 24 hours, consumer timeout fires
4. RabbitMQ closes connection
5. Message returns to queue
6. Worker realizes connection closed
7. Continues processing anyway (already started)
8. Cannot acknowledge at end (connection dead)

**Result**: ⚠️ Work completed but can't ACK

**Solution**: Increase timeout or split large projects

### Scenario 3: Storage Temporarily Unavailable

**What happens**:
1. Worker picks up project 10
2. Tries to read from `/mnt/projects/project_10/`
3. NFS mount is temporarily down
4. Error: "Project directory not found"
5. Worker marks as **retriable error**
6. Message **requeued**
7. 30 seconds later, another worker tries
8. NFS is back up
9. Processing succeeds

**Result**: ✅ Temporary issue resolved by retry

### Scenario 4: Corrupt/Invalid Project

**What happens**:
1. Worker picks up project 7
2. COLMAP feature extraction fails
3. Error: "COLMAP command failed - no features found"
4. Worker marks as **fatal error**
5. Message **rejected** → Sent to dead letter queue
6. Admin inspects dead letter queue
7. Admin sees project 7 has corrupt images

**Result**: ✅ No infinite retry loop, manual review possible

### Scenario 5: Multiple Workers Available

**What happens**:
1. 5 projects in queue: [1, 2, 3, 4, 5]
2. 3 workers available
3. Worker A takes project 1
4. Worker B takes project 2
5. Worker C takes project 3
6. Projects 4 and 5 wait in queue
7. Worker A finishes → Takes project 4
8. Worker B crashes processing project 2
9. Project 2 returns to queue
10. Worker C finishes → Takes project 2
11. Worker B restarts → Takes project 5

**Result**: ✅ Fair distribution, crash doesn't block queue

---

## Configuration

### Queue Settings (RabbitMQ)

```json
{
  "x-consumer-timeout": 86400000,        // 24 hours
  "x-message-ttl": 172800000,            // 48 hours
  "x-dead-letter-exchange": "photogrammetry-dlx",
  "x-dead-letter-routing-key": "failed"
}
```

### Adjust Timeouts

Edit `PhotogrammetryWorker/ColmapWorkerService.cs`:

```csharp
{ "x-consumer-timeout", 86400000 }  // Change to 172800000 for 48 hours
{ "x-message-ttl", 172800000 }      // Change to 604800000 for 7 days
```

---

## Monitoring

### Check Queue Status

```bash
# Using RabbitMQ Management CLI
sudo rabbitmqctl list_queues name messages consumers

# Expected output:
# photogrammetry-queue    3    2    (3 messages, 2 workers)
# photogrammetry-failed   0    0    (0 failed messages)
```

### RabbitMQ Management UI

Access at `http://localhost:15672` (guest/guest)

**Queues Tab**:
- `photogrammetry-queue` - Active jobs
  - Ready: Jobs waiting for workers
  - Unacked: Jobs being processed
  - Total: All pending jobs

- `photogrammetry-failed` - Failed jobs
  - Check here for projects that need attention

### Monitor Dead Letter Queue

```bash
# View failed messages
sudo rabbitmqctl list_queue_messages photogrammetry-failed
```

### Worker Console Output

```
✅ Worker connected to RabbitMQ
   Queue: photogrammetry-queue
   Dead Letter Queue: photogrammetry-failed
   Waiting for projects...

📸 Processing Project 5
   Started at: 2026-01-03 00:30:00
   ...
✅ Project 5 acknowledged

📸 Processing Project 6
   Started at: 2026-01-03 00:45:00
❌ Error processing project 6: Project directory not found
   ♻️  Requeuing project 6 for retry
```

---

## Recovery Procedures

### Retry Failed Messages

**Option 1: Manual Retry via Management UI**
1. Go to `photogrammetry-failed` queue
2. Click "Get Messages"
3. Copy message content
4. Go to `photogrammetry-queue`
5. Click "Publish Message"
6. Paste content and publish

**Option 2: Create Retry Script**

```bash
#!/bin/bash
# retry_failed_project.sh
PROJECT_ID=$1

rabbitmqadmin get queue=photogrammetry-failed count=1 | \
  grep payload | \
  sed 's/.*payload=//' | \
  rabbitmqadmin publish routing_key=photogrammetry-queue payload=-

# Usage: ./retry_failed_project.sh 5
```

### Purge Failed Queue

```bash
# Remove all failed messages (careful!)
sudo rabbitmqctl purge_queue photogrammetry-failed
```

---

## Testing Fault Tolerance

### Test 1: Worker Crash

```bash
# Terminal 1: Start worker
cd PhotogrammetryWorker
dotnet run

# Terminal 2: Start another worker
cd PhotogrammetryWorker
dotnet run

# Terminal 3: Upload project
cd PhotogrammetryAPI
./quick_test.sh

# Terminal 1: Kill worker (Ctrl+C) during processing
^C

# Check: Terminal 2 should pick up the project
```

### Test 2: Network Storage Failure

```bash
# Simulate NFS failure
sudo umount -l /mnt/projects

# Upload project - worker will get "directory not found"
# Message will requeue

# Remount storage
sudo mount /mnt/projects

# Worker should succeed on retry
```

### Test 3: Invalid Project

```bash
# Create corrupt ZIP
echo "corrupted data" > corrupt.zip

# Upload corrupt ZIP
curl -X POST http://localhost:5273/api/projects/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "zipFile=@corrupt.zip" \
  -F "projectName=BadProject"

# Check dead letter queue
sudo rabbitmqctl list_queues photogrammetry-failed
# Should show 1 message
```

---

## Performance Impact

### Overhead

- **Manual ACK**: Negligible (<1ms per message)
- **Dead Letter Queue**: No runtime overhead
- **Retry Logic**: Only on failures (adds ~100ms)

### Trade-offs

**Durability vs Speed**:
- Persistent messages → Slower publish (~5ms vs 1ms)
- But: Survive RabbitMQ restart

**Fair Dispatch**:
- prefetchCount=1 → One message per worker
- Prevents one worker from hogging all messages
- Slight throughput decrease (negligible)

---

## Best Practices

### 1. Monitor Dead Letter Queue Daily

```bash
# Daily cron job
0 9 * * * /usr/local/bin/check_failed_projects.sh
```

### 2. Set Alerts

Use RabbitMQ monitoring plugins or:

```bash
# Alert if >10 failed messages
FAILED=$(rabbitmqctl list_queues -q photogrammetry-failed | awk '{print $2}')
if [ $FAILED -gt 10 ]; then
  echo "High failure rate!" | mail -s "Alert" admin@company.com
fi
```

### 3. Log Everything

Workers already log to console. Capture with:

```bash
# systemd service
dotnet run 2>&1 | tee -a /var/log/photogrammetry-worker.log
```

### 4. Test Failure Recovery

Periodically test worker crashes and recovery to ensure system behaves correctly.

### 5. Keep Workers Updated

Ensure all workers run same code version to avoid inconsistent behavior.

---

## Advanced: Message Priority

Future enhancement for urgent projects:

```csharp
// In API
var properties = _channel.CreateBasicProperties();
properties.Priority = 10; // 0-10, higher = more important

_channel.BasicPublish(exchange, routingKey, properties, body);
```

Requires queue declaration:

```csharp
args["x-max-priority"] = 10;
```

---

## Summary

✅ **Worker crashes** → Message returns to queue → Another worker picks up  
✅ **Worker timeout** → Message returns to queue after 24 hours  
✅ **Retriable errors** → Message requeued for retry  
✅ **Fatal errors** → Message sent to dead letter queue  
✅ **Message expiry** → Old messages cleaned up after 48 hours  
✅ **Fair dispatch** → One message per worker prevents hogging  
✅ **Manual monitoring** → Dead letter queue shows failed projects  

**Your system is now fault-tolerant and can handle failures gracefully!**
