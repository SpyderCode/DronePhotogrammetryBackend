#!/bin/bash
set -e

BASE_URL="http://localhost:5273"
TEST_EMAIL="test_full_$(date +%s)@example.com"
TEST_USER="testuser_full_$(date +%s)"
TEST_PASSWORD="Test123!"
PROJECT_NAME="SouthBuilding_Full"
ZIP_FILE="south_building_full.zip"

echo "🏢 Testing Full South Building Dataset (128 images, 221MB)"
echo "================================================================"
echo ""
echo "⚠️  This will take a long time (estimated 2-4 hours)"
echo "    - Feature extraction: ~5 minutes"
echo "    - Feature matching: ~3 minutes"
echo "    - Sparse reconstruction: ~2 minutes"
echo "    - Image undistortion: ~2 minutes"
echo "    - Dense stereo (GPU): ~1.5-2 hours"
echo "    - Stereo fusion: ~5 minutes"
echo "    - Poisson meshing: ~3 minutes"
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."
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
echo "2️⃣ Uploading project (221MB - this may take a minute)..."
START_TIME=$(date +%s)

UPLOAD_RESPONSE=$(curl -s -X POST "$BASE_URL/api/projects/upload" \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=$PROJECT_NAME" \
  -F "ZipFile=@$ZIP_FILE" \
  --max-time 600)

UPLOAD_TIME=$(($(date +%s) - START_TIME))

PROJECT_ID=$(echo $UPLOAD_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin)['projectId'])" 2>/dev/null)

if [ -z "$PROJECT_ID" ]; then
    echo "❌ Upload failed: $UPLOAD_RESPONSE"
    exit 1
fi

echo "✅ Project uploaded: ID $PROJECT_ID (took ${UPLOAD_TIME}s)"
echo ""

# Check status with longer polling
echo "3️⃣ Monitoring processing status..."
echo "   This will check every 30 seconds for up to 4 hours"
echo "   You can safely Ctrl+C and check status later with:"
echo "   curl $BASE_URL/api/projects/$PROJECT_ID/status -H \"Authorization: Bearer $TOKEN\""
echo ""

MAX_CHECKS=480  # 4 hours / 30 seconds
CHECK_INTERVAL=30

for i in $(seq 1 $MAX_CHECKS); do
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
    
    ELAPSED=$((i * CHECK_INTERVAL))
    ELAPSED_MIN=$((ELAPSED / 60))
    echo "   [Check $i/$MAX_CHECKS - ${ELAPSED_MIN}min elapsed] Status: $STATUS_NAME"
    
    if [ "$STATUS" == "2" ]; then
        echo ""
        echo "✅ Processing completed!"
        TOTAL_TIME=$(($(date +%s) - START_TIME))
        TOTAL_MIN=$((TOTAL_TIME / 60))
        echo "   Total time: ${TOTAL_MIN} minutes (${TOTAL_TIME}s)"
        
        DOWNLOAD_URL=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin).get('downloadUrl', 'N/A'))" 2>/dev/null)
        echo "   Download URL: $DOWNLOAD_URL"
        
        # Download
        echo ""
        echo "4️⃣ Downloading model..."
        HTTP_CODE=$(curl -s -w "%{http_code}" -X GET "$BASE_URL/api/projects/$PROJECT_ID/download" \
          -H "Authorization: Bearer $TOKEN" \
          --output "model_full_$PROJECT_ID.ply")
        
        if [ "$HTTP_CODE" = "200" ] && [ -f "model_full_$PROJECT_ID.ply" ]; then
            SIZE=$(stat -c%s "model_full_$PROJECT_ID.ply" 2>/dev/null || stat -f%z "model_full_$PROJECT_ID.ply" 2>/dev/null)
            SIZE_MB=$((SIZE / 1024 / 1024))
            if [ "$SIZE" -gt 0 ]; then
                echo "✅ Model downloaded: model_full_$PROJECT_ID.ply (${SIZE_MB}MB)"
            else
                echo "❌ Download failed: File is empty"
                cat "model_full_$PROJECT_ID.ply"
                exit 1
            fi
        else
            echo "❌ Download failed with HTTP code: $HTTP_CODE"
            if [ -f "model_full_$PROJECT_ID.ply" ]; then
                cat "model_full_$PROJECT_ID.ply"
            fi
            exit 1
        fi
        
        echo ""
        echo "🎉 Full dataset test completed successfully!"
        echo ""
        echo "📊 Statistics:"
        echo "   - Images processed: 128"
        echo "   - Upload time: ${UPLOAD_TIME}s"
        echo "   - Processing time: ${TOTAL_MIN} minutes"
        echo "   - Output size: ${SIZE_MB}MB"
        exit 0
    elif [ "$STATUS" == "3" ]; then
        ERROR=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin).get('errorMessage', 'Unknown error'))" 2>/dev/null)
        echo ""
        echo "❌ Processing failed: $ERROR"
        exit 1
    fi
    
    sleep $CHECK_INTERVAL
done

echo ""
echo "⏱️ Timeout after 4 hours"
echo "   Project is still processing. Check status manually:"
echo "   curl $BASE_URL/api/projects/$PROJECT_ID/status -H \"Authorization: Bearer $TOKEN\""
exit 1
