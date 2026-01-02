#!/bin/bash

# Setup script for Photogrammetry API

echo "🚀 Photogrammetry API Setup"
echo "============================"
echo ""

# Check for .NET
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET SDK not found"
    echo "   Please install .NET 8.0 SDK from: https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "✅ .NET SDK found: $DOTNET_VERSION"
echo ""

# Check for MySQL
if command -v mysql &> /dev/null; then
    echo "✅ MySQL found"
else
    echo "⚠️  MySQL not found"
    echo "   Install with: sudo apt install mysql-server"
    echo ""
fi

# Check for RabbitMQ
if command -v rabbitmq-server &> /dev/null || systemctl is-active --quiet rabbitmq-server; then
    echo "✅ RabbitMQ found"
else
    echo "⚠️  RabbitMQ not found"
    echo "   Install with: sudo apt install rabbitmq-server"
    echo ""
fi

# Check for Meshroom
if command -v meshroom_batch &> /dev/null; then
    echo "✅ Meshroom found"
else
    echo "ℹ️  Meshroom not found (optional)"
    echo "   The API will create dummy models for testing"
    echo "   For actual 3D processing, install Meshroom:"
    echo "   - Snap: sudo snap install meshroom"
    echo "   - Manual: https://github.com/alicevision/Meshroom/releases"
    echo ""
fi

echo "============================"
echo ""

# Offer to start services with Docker
read -p "🐳 Do you want to start MySQL and RabbitMQ with Docker Compose? (y/n) " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
    if command -v docker-compose &> /dev/null || command -v docker &> /dev/null; then
        echo "Starting services..."
        cd ..
        docker-compose up -d
        echo ""
        echo "✅ Services started!"
        echo "   MySQL: localhost:3306 (root/rootpassword)"
        echo "   RabbitMQ: localhost:5672 (guest/guest)"
        echo "   RabbitMQ Management: http://localhost:15672"
        echo ""
        echo "⚠️  Update appsettings.json with MySQL password: rootpassword"
        cd PhotogrammetryAPI
    else
        echo "❌ Docker not found. Please install Docker or start services manually."
    fi
fi

echo ""
echo "============================"
echo "📝 Next steps:"
echo ""
echo "1. Ensure MySQL is running and database is created:"
echo "   mysql -u root -p < database_setup.sql"
echo ""
echo "2. Update appsettings.json with your MySQL password"
echo ""
echo "3. Run the application:"
echo "   dotnet run"
echo ""
echo "4. Test the API:"
echo "   ./test_api.sh"
echo ""
echo "5. Access Swagger UI:"
echo "   http://localhost:5000/swagger"
echo ""
echo "============================"
