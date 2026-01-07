#!/bin/bash

# Test RabbitMQ Stats Endpoint

echo "🔍 Testing RabbitMQ Stats Endpoint"
echo "=================================="
echo ""

# Check if API is running
if ! curl -s http://localhost:5273/api/stats > /dev/null 2>&1; then
    echo "❌ API is not running. Please start it with: dotnet run"
    exit 1
fi

echo "📊 Fetching RabbitMQ Statistics..."
echo ""

response=$(curl -s http://localhost:5273/api/stats)
echo "$response" | jq '.' 2>/dev/null || echo "$response"

echo ""
echo "✅ Stats endpoint test complete"
echo ""
echo "Legend:"
echo "  - workers: Number of active workers listening to the queue"
echo "  - messagesReady: Projects waiting to be processed"
echo "  - messagesProcessing: Projects currently being processed"
echo "  - totalMessages: Total messages in queue"
