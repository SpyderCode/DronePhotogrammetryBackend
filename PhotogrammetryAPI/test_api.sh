#!/bin/bash

# Test script for Photogrammetry API
# This script tests the complete workflow: Register → Upload → Status Check → Download

set -e

BASE_URL="http://localhost:5000"
TEST_EMAIL="test_$(date +%s)@example.com"
TEST_USER="testuser_$(date +%s)"
TEST_PASSWORD="SecurePass123!"
PROJECT_NAME="TestIndoorScan"

echo "🚀 Testing Photogrammetry API"
echo "================================"
echo ""

# Check if API is running
echo "📡 Checking API availability..."
if ! curl -s -f "$BASE_URL/swagger/index.html" > /dev/null 2>&1; then
    echo "❌ API is not running at $BASE_URL"
    echo "   Please start the API with: dotnet run"
    exit 1
fi
echo "✅ API is running"
echo ""

# Register user
echo "👤 Registering new user..."
REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$TEST_USER\",\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\"}")

TOKEN=$(echo $REGISTER_RESPONSE | grep -o '"token":"[^"]*' | sed 's/"token":"//')

if [ -z "$TOKEN" ]; then
    echo "❌ Registration failed"
    echo "   Response: $REGISTER_RESPONSE"
    exit 1
fi

echo "✅ User registered successfully"
echo "   Token: ${TOKEN:0:20}..."
echo ""

# Create test images zip if it doesn't exist
if [ ! -f "test_images.zip" ]; then
    echo "📦 Creating test images archive..."
    
    # Check if images directory exists
    if [ -d "../images" ]; then
        cd ../images
        zip -r ../PhotogrammetryAPI/test_images.zip . > /dev/null 2>&1
        cd ../PhotogrammetryAPI
        echo "✅ Test images archive created"
    else
        echo "⚠️  No images directory found"
        echo "   Creating dummy test archive..."
        mkdir -p test_imgs
        echo "Dummy test image" > test_imgs/test.jpg
        zip -r test_images.zip test_imgs > /dev/null 2>&1
        rm -rf test_imgs
        echo "✅ Dummy archive created (for API testing only)"
    fi
    echo ""
fi

# Upload project
echo "📤 Uploading project..."
UPLOAD_RESPONSE=$(curl -s -X POST "$BASE_URL/api/projects/upload" \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=$PROJECT_NAME" \
  -F "ZipFile=@test_images.zip")

PROJECT_ID=$(echo $UPLOAD_RESPONSE | grep -o '"projectId":[0-9]*' | sed 's/"projectId"://')

if [ -z "$PROJECT_ID" ]; then
    echo "❌ Upload failed"
    echo "   Response: $UPLOAD_RESPONSE"
    exit 1
fi

echo "✅ Project uploaded successfully"
echo "   Project ID: $PROJECT_ID"
echo ""

# Check status
echo "🔍 Checking project status..."
for i in {1..10}; do
    STATUS_RESPONSE=$(curl -s -X GET "$BASE_URL/api/projects/$PROJECT_ID/status" \
      -H "Authorization: Bearer $TOKEN")
    
    STATUS=$(echo $STATUS_RESPONSE | grep -o '"status":[0-9]*' | sed 's/"status"://')
    STATUS_NAME=""
    
    case $STATUS in
        0) STATUS_NAME="InQueue" ;;
        1) STATUS_NAME="Processing" ;;
        2) STATUS_NAME="Finished" ;;
        3) STATUS_NAME="Failed" ;;
        *) STATUS_NAME="Unknown" ;;
    esac
    
    echo "   [Attempt $i/10] Status: $STATUS_NAME ($STATUS)"
    
    if [ "$STATUS" == "2" ]; then
        echo ""
        echo "✅ Processing completed successfully!"
        
        # Get download URL
        DOWNLOAD_URL=$(echo $STATUS_RESPONSE | grep -o '"downloadUrl":"[^"]*' | sed 's/"downloadUrl":"//')
        echo "   Download URL: $DOWNLOAD_URL"
        echo ""
        
        # Download model
        echo "📥 Downloading 3D model..."
        curl -s -X GET "$BASE_URL/api/projects/$PROJECT_ID/download" \
          -H "Authorization: Bearer $TOKEN" \
          --output "downloaded_model_$PROJECT_ID.obj"
        
        if [ -f "downloaded_model_$PROJECT_ID.obj" ]; then
            FILE_SIZE=$(stat -f%z "downloaded_model_$PROJECT_ID.obj" 2>/dev/null || stat -c%s "downloaded_model_$PROJECT_ID.obj" 2>/dev/null)
            echo "✅ Model downloaded successfully"
            echo "   File: downloaded_model_$PROJECT_ID.obj"
            echo "   Size: $FILE_SIZE bytes"
        else
            echo "❌ Download failed"
        fi
        
        break
    elif [ "$STATUS" == "3" ]; then
        echo ""
        echo "❌ Processing failed"
        ERROR_MSG=$(echo $STATUS_RESPONSE | grep -o '"errorMessage":"[^"]*' | sed 's/"errorMessage":"//')
        echo "   Error: $ERROR_MSG"
        exit 1
    fi
    
    sleep 2
done

if [ "$STATUS" != "2" ]; then
    echo ""
    echo "⏱️  Processing is still in progress"
    echo "   Check status manually with:"
    echo "   curl -X GET \"$BASE_URL/api/projects/$PROJECT_ID/status\" \\"
    echo "     -H \"Authorization: Bearer $TOKEN\""
fi

echo ""
echo "================================"
echo "🎉 Test completed!"
echo ""
echo "Test credentials:"
echo "  Email: $TEST_EMAIL"
echo "  Password: $TEST_PASSWORD"
echo "  Project ID: $PROJECT_ID"
echo ""
