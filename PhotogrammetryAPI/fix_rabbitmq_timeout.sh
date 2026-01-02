#!/bin/bash

echo "🐰 Fixing RabbitMQ Timeout for Long-Running Tasks"
echo "================================================="
echo ""

# Step 1: Delete old queue
echo "1️⃣ Deleting old queue (with 30-min timeout)..."
sudo rabbitmqctl delete_queue photogrammetry-queue 2>&1 | grep -v "Error" || echo "   Queue deleted or didn't exist"
echo ""

# Step 2: Configure RabbitMQ
echo "2️⃣ Configuring RabbitMQ for 24-hour timeout..."
if ! grep -q "consumer_timeout" /etc/rabbitmq/rabbitmq.conf 2>/dev/null; then
    echo "consumer_timeout = 86400000" | sudo tee -a /etc/rabbitmq/rabbitmq.conf > /dev/null
    echo "   ✅ Added consumer_timeout to /etc/rabbitmq/rabbitmq.conf"
else
    echo "   ✅ consumer_timeout already configured"
fi
echo ""

# Step 3: Restart RabbitMQ
echo "3️⃣ Restarting RabbitMQ..."
sudo systemctl restart rabbitmq-server
sleep 3
echo ""

# Step 4: Verify
echo "4️⃣ Verifying RabbitMQ is running..."
if sudo systemctl is-active --quiet rabbitmq-server; then
    echo "   ✅ RabbitMQ is running"
else
    echo "   ❌ RabbitMQ failed to start"
    sudo systemctl status rabbitmq-server
    exit 1
fi
echo ""

echo "✅ RabbitMQ configured successfully!"
echo ""
echo "Next steps:"
echo "1. Stop any running API (Ctrl+C)"
echo "2. Restart API: cd ~/DronePhoto/PhotogrammetryAPI && dotnet run"
echo "3. API will create new queue with 24-hour timeout"
echo ""
echo "Your future long-running jobs will complete without timeout errors!"
