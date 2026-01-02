#!/bin/bash
set -e

BASE_URL="http://localhost:5273"
TEST_EMAIL="test_$(date +%s)@example.com"
TEST_USER="testuser_$(date +%s)"
TEST_PASSWORD="Test123!"
PROJECT_NAME="QuickTest"
ZIP_FILE="test_small.zip"

echo "🚀 Quick Test - Small Dataset (10 images, 18MB)"
echo "================================================"
echo "Estimated time: 7-10 minutes"
echo ""

# Register
echo "1️⃣ Registering user..."
REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$TEST_USER\",\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\"}")

TOKEN=$(echo $REGISTER_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin)['token'])" 2>/dev/null)

if [ -z "$TOKEN" ]; then
    echo "❌ Registration failed: $REGISTER_RESPONSE"
    exit 1
fi

echo "✅ User registered: $TEST_USER"
echo "   Token: ${TOKEN:0:30}..."
echo ""

# Upload
echo "2️⃣ Uploading project..."
UPLOAD_RESPONSE=$(curl -s -X POST "$BASE_URL/api/projects/upload" \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=$PROJECT_NAME" \
  -F "ZipFile=@$ZIP_FILE")

PROJECT_ID=$(echo $UPLOAD_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin)['projectId'])" 2>/dev/null)

if [ -z "$PROJECT_ID" ]; then
    echo "❌ Upload failed: $UPLOAD_RESPONSE"
    exit 1
fi

echo "✅ Project uploaded: ID $PROJECT_ID"
echo ""

# Check status
echo "3️⃣ Checking status..."
echo "   Polling every 5 seconds for up to 15 minutes..."
for i in {1..180}; do
    STATUS_RESPONSE=$(curl -s -X GET "$BASE_URL/api/projects/$PROJECT_ID/status" \
      -H "Authorization: Bearer $TOKEN")
    
    STATUS=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
    
    case $STATUS in
        0) STATUS_NAME="InQueue" ;;
        1) STATUS_NAME="Processing" ;;
        2) STATUS_NAME="Finished" ;;
        3) STATUS_NAME="Failed" ;;
        *) STATUS_NAME="Unknown" ;;
    esac
    
    echo "   [Check $i/180 - $((i*5/60))min] Status: $STATUS_NAME"
    
    if [ "$STATUS" == "2" ]; then
        echo ""
        echo "✅ Processing completed!"
        DOWNLOAD_URL=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin).get('downloadUrl', 'N/A'))" 2>/dev/null)
        echo "   Download URL: $DOWNLOAD_URL"
        
        # Download
        echo ""
        echo "4️⃣ Downloading model..."
        curl -s -X GET "$BASE_URL/api/projects/$PROJECT_ID/download" \
          -H "Authorization: Bearer $TOKEN" \
          --output "model_$PROJECT_ID.ply"
        
        if [ -f "model_$PROJECT_ID.ply" ]; then
            SIZE=$(stat -c%s "model_$PROJECT_ID.ply" 2>/dev/null || stat -f%z "model_$PROJECT_ID.ply" 2>/dev/null)
            echo "✅ Model downloaded: model_$PROJECT_ID.ply ($SIZE bytes)"
        fi
        
        echo ""
        echo "🎉 All tests passed!"
        exit 0
    elif [ "$STATUS" == "3" ]; then
        ERROR=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin).get('errorMessage', 'Unknown error'))" 2>/dev/null)
        echo ""
        echo "❌ Processing failed: $ERROR"
        exit 1
    fi
    
    sleep 5
done

echo ""
echo "⏱️ Timeout waiting for processing"
exit 1
