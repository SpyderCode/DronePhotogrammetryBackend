# 🐰 RabbitMQ Configuration for Long-Running Tasks

## Problem

RabbitMQ has a default **consumer timeout of 30 minutes**. If COLMAP processing takes longer (which it does for large datasets), RabbitMQ closes the channel and you get this error:

```
PRECONDITION_FAILED - delivery acknowledgement on channel 1 timed out. 
Timeout value used: 1800000 ms.
```

**Good news**: The processing still completes! Only the acknowledgment fails.

## Solution

Configure RabbitMQ to allow longer timeouts.

### Option 1: System RabbitMQ Configuration

1. **Create or edit RabbitMQ config**:
```bash
sudo nano /etc/rabbitmq/rabbitmq.conf
```

2. **Add this line**:
```conf
# Allow 24-hour consumer timeout for long-running photogrammetry tasks
consumer_timeout = 86400000
```

3. **Restart RabbitMQ**:
```bash
sudo systemctl restart rabbitmq-server
```

4. **Verify**:
```bash
sudo systemctl status rabbitmq-server
```

### Option 2: Per-Queue Configuration (Already Done in Code)

The worker now sets `x-consumer-timeout` when creating the queue:

```csharp
var args = new Dictionary<string, object>
{
    { "x-consumer-timeout", 86400000 } // 24 hours
};
```

**Note**: This only works for new queues. If the queue already exists, delete it first:

```bash
# Delete existing queue
sudo rabbitmqctl delete_queue photogrammetry-queue

# Restart API - queue will be recreated with new timeout
cd ~/DronePhoto/PhotogrammetryAPI
dotnet run
```

### Option 3: Docker RabbitMQ Configuration

If using Docker RabbitMQ in docker-compose.yml:

```yaml
rabbitmq:
  image: rabbitmq:3-management
  environment:
    RABBITMQ_CONSUMER_TIMEOUT: 86400000
  volumes:
    - ./rabbitmq.conf:/etc/rabbitmq/rabbitmq.conf:ro
```

## Current Setup

Since you're using **system RabbitMQ** (not Docker), apply **Option 1**.

## Quick Fix (Immediate)

1. **Delete existing queue** (has old 30-min timeout):
```bash
sudo rabbitmqctl delete_queue photogrammetry-queue
```

2. **Rebuild and restart API** (creates queue with 24-hour timeout):
```bash
cd ~/DronePhoto/PhotogrammetryAPI
dotnet build
dotnet run
```

3. **Also configure system RabbitMQ** for future:
```bash
echo "consumer_timeout = 86400000" | sudo tee -a /etc/rabbitmq/rabbitmq.conf
sudo systemctl restart rabbitmq-server
```

## Verify Configuration

### Check RabbitMQ is Running
```bash
sudo systemctl status rabbitmq-server
```

### Check Queue Exists
```bash
sudo rabbitmqctl list_queues name arguments
```

Should show:
```
photogrammetry-queue    [{<<"x-consumer-timeout">>,86400000}]
```

### Test with Management UI
```bash
# Enable management plugin if not already
sudo rabbitmq-plugins enable rabbitmq_management

# Access at: http://localhost:15672
# Username: guest
# Password: guest
```

## Alternative: Increase Timeout Further

For very large datasets (1000+ images):

```conf
# 7 days
consumer_timeout = 604800000
```

## Understanding the Error

The error occurs in this sequence:

1. ✅ Worker receives job from queue
2. ✅ COLMAP processes images (takes > 30 minutes)
3. ✅ Processing completes successfully
4. ✅ Model saved to database
5. ❌ **Worker tries to acknowledge** → RabbitMQ channel already closed (30 min timeout)
6. ❌ Exception thrown (but work is done!)

**Result**: Your model is successfully generated, but you see an error.

## Code Changes Made

The worker now:
1. ✅ Sets 24-hour timeout when creating queue
2. ✅ Checks if channel is open before acknowledging
3. ✅ Gracefully handles acknowledgment failures
4. ✅ Logs when acknowledgment fails (but processing succeeded)

## After Applying Fix

You'll see this instead of error:
```
Mesh generated successfully: models/project_5/output/dense/fused.ply
✅ Message acknowledged successfully
```

Or if channel closed:
```
Mesh generated successfully: models/project_5/output/dense/fused.ply
Channel closed, cannot acknowledge message. Project completed successfully anyway.
```

## Troubleshooting

### Still Getting Timeout?

1. **Check RabbitMQ config was applied**:
```bash
sudo rabbitmqctl environment | grep consumer_timeout
```

2. **Recreate queue**:
```bash
sudo rabbitmqctl delete_queue photogrammetry-queue
# Restart API
```

3. **Check RabbitMQ logs**:
```bash
sudo journalctl -u rabbitmq-server -f
```

### Queue Already Exists Error

If you get "queue already exists with different parameters":

```bash
# Delete it
sudo rabbitmqctl delete_queue photogrammetry-queue

# Restart API to recreate
```

## Best Practice

For production:
1. Set system-wide config in `/etc/rabbitmq/rabbitmq.conf`
2. Set per-queue config in code (as backup)
3. Monitor long-running jobs
4. Consider breaking very large jobs into chunks

## Summary

✅ **Quick fix**: Delete queue, restart API  
✅ **Permanent fix**: Configure system RabbitMQ  
✅ **Code updated**: Worker handles timeout gracefully  

Your processing works fine - this just prevents the acknowledgment error!
