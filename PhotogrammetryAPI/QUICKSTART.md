# Quick Start Guide

Get the Photogrammetry API running in 5 minutes!

## Prerequisites Check

```bash
# Check if you have everything installed
dotnet --version          # Should be 8.0 or higher
mysql --version           # MySQL server
rabbitmqctl status        # RabbitMQ server
```

## Option 1: Docker Setup (Recommended for Testing)

```bash
# 1. Start MySQL and RabbitMQ
cd /home/echaniz/DronePhoto
docker-compose up -d

# 2. Wait for services to start (about 30 seconds)
docker-compose ps

# 3. Update appsettings.json
# Change the password in ConnectionStrings to "rootpassword"
```

## Option 2: Manual Setup

```bash
# 1. Start MySQL
sudo systemctl start mysql

# 2. Create database
mysql -u root -p < PhotogrammetryAPI/database_setup.sql

# 3. Start RabbitMQ
sudo systemctl start rabbitmq-server

# 4. Update appsettings.json with your MySQL password
```

## Run the API

```bash
cd PhotogrammetryAPI
dotnet run
```

The API will start at: **http://localhost:5000**

## Test the API

### Using the Test Script
```bash
./test_api.sh
```

### Using Swagger UI
1. Open browser: http://localhost:5000/swagger
2. Try the `/api/auth/register` endpoint
3. Copy the token from the response
4. Click "Authorize" button and paste token
5. Try other endpoints

### Manual Testing with cURL

```bash
# 1. Register
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"demo","email":"demo@test.com","password":"Demo123!"}'

# Save the token from response, then:

# 2. Create a test zip file
cd ../images
zip -r ../test.zip .
cd ../PhotogrammetryAPI

# 3. Upload project
curl -X POST http://localhost:5000/api/projects/upload \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -F "ProjectName=TestScan" \
  -F "ZipFile=@../test.zip"

# 4. Check status (use project ID from response)
curl -X GET http://localhost:5000/api/projects/1/status \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"

# 5. Download model (when finished)
curl -X GET http://localhost:5000/api/projects/1/download \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --output model.obj
```

## Expected Behavior

### Without COLMAP (Testing Mode)
- Upload will succeed ✅
- Status will change: InQueue → Processing → Finished
- Processing takes ~3-5 seconds
- Downloads a dummy .obj file for testing

### With COLMAP (Production Mode)
- Upload will succeed ✅
- Status will change: InQueue → Processing → Finished
- Processing takes 5-30 minutes (depending on image count)
- Downloads actual 3D model (.obj, .ply, etc.)

## Troubleshooting

### "Connection refused" error
```bash
# Check if services are running
docker-compose ps                    # For Docker setup
sudo systemctl status mysql          # For manual setup
sudo systemctl status rabbitmq-server
```

### "Access denied for user 'root'"
```bash
# Update the password in appsettings.json:
"ConnectionStrings": {
  "DefaultConnection": "server=localhost;port=3306;database=photogrammetry;user=root;password=YOUR_MYSQL_PASSWORD"
}
```

### Port already in use
```bash
# Check what's using port 5000
lsof -i :5000

# Kill the process or change port in appsettings.json:
"urls": "http://localhost:5001"
```

### Images not processing
```bash
# Check API logs for errors
dotnet run

# Look for:
# - "Worker error" messages
# - "Error processing message" messages
# - RabbitMQ connection issues
```

## API Endpoints Summary

| Method | Endpoint | Auth Required | Description |
|--------|----------|---------------|-------------|
| POST | `/api/auth/register` | No | Register new user |
| POST | `/api/auth/login` | No | Login user |
| POST | `/api/projects/upload` | Yes | Upload images |
| GET | `/api/projects` | Yes | List all projects |
| GET | `/api/projects/{id}/status` | Yes | Check status |
| GET | `/api/projects/{id}/download` | Yes | Download model |

## Next Steps

1. **Read the full README.md** for detailed documentation
2. **Prepare your images** following best practices
3. **Install COLMAP** for actual 3D processing
4. **Configure for production** (HTTPS, secure secrets, etc.)

## Support Resources

- Project README: `README.md`
- COLMAP: https://github.com/alicevision/COLMAP
- RabbitMQ: https://www.rabbitmq.com/documentation.html
- Entity Framework: https://docs.microsoft.com/ef/core/

---

🎉 **You're all set!** Start uploading your indoor scene images and get 3D models!
