#!/bin/bash

if [ "$#" -lt 2 ]; then
    echo "Usage: ./monitor_project.sh PROJECT_ID TOKEN"
    echo "Example: ./monitor_project.sh 5 eyJhbGci..."
    exit 1
fi

PROJECT_ID=$1
TOKEN=$2
BASE_URL="http://localhost:5273"
CHECK_INTERVAL=30

echo "🔍 Monitoring Project $PROJECT_ID"
echo "================================"
echo "Checking every ${CHECK_INTERVAL} seconds"
echo "Press Ctrl+C to stop monitoring"
echo ""

START_TIME=$(date +%s)

while true; do
    STATUS_RESPONSE=$(curl -s -X GET "$BASE_URL/api/projects/$PROJECT_ID/status" \
      -H "Authorization: Bearer $TOKEN")
    
    if [ $? -ne 0 ]; then
        echo "❌ Failed to connect to API"
        sleep $CHECK_INTERVAL
        continue
    fi
    
    STATUS=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin)['status'])" 2>/dev/null)
    
    case $STATUS in
        0) STATUS_NAME="InQueue ⏳" ;;
        1) STATUS_NAME="Processing 🔄" ;;
        2) STATUS_NAME="Finished ✅" ;;
        3) STATUS_NAME="Failed ❌" ;;
        *) STATUS_NAME="Unknown ❓" ;;
    esac
    
    ELAPSED=$(($(date +%s) - START_TIME))
    ELAPSED_MIN=$((ELAPSED / 60))
    ELAPSED_HR=$((ELAPSED_MIN / 60))
    ELAPSED_MIN_REMAINDER=$((ELAPSED_MIN % 60))
    
    TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S')
    
    if [ $ELAPSED_HR -gt 0 ]; then
        TIME_STR="${ELAPSED_HR}h ${ELAPSED_MIN_REMAINDER}m"
    else
        TIME_STR="${ELAPSED_MIN}m"
    fi
    
    echo "[$TIMESTAMP] Status: $STATUS_NAME (elapsed: $TIME_STR)"
    
    if [ "$STATUS" == "2" ]; then
        echo ""
        echo "✅ Processing completed!"
        echo "   Total time: $TIME_STR"
        echo ""
        echo "Download with:"
        echo "  curl $BASE_URL/api/projects/$PROJECT_ID/download \\"
        echo "    -H \"Authorization: Bearer $TOKEN\" \\"
        echo "    --output model_$PROJECT_ID.ply"
        exit 0
    elif [ "$STATUS" == "3" ]; then
        ERROR=$(echo $STATUS_RESPONSE | python3 -c "import sys, json; print(json.load(sys.stdin).get('errorMessage', 'Unknown error'))" 2>/dev/null)
        echo ""
        echo "❌ Processing failed!"
        echo "   Error: $ERROR"
        exit 1
    fi
    
    sleep $CHECK_INTERVAL
done
