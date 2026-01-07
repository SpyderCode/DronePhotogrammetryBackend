# Photogrammetry API

REST API for photogrammetry project management. Handles user authentication, image uploads, and status tracking.

## Features

- JWT authentication with BCrypt password hashing
- Large file uploads (up to 50GB ZIP files)
- Project management and status tracking
- RabbitMQ integration for distributed processing
- Real-time status updates from worker nodes
- Model download endpoints
- System statistics monitoring

## Prerequisites

- .NET 8.0 SDK
- MySQL 8.0+
- RabbitMQ 3.x
- Access to shared storage (local or network)

## Installation

### 1. Install Dependencies

**MySQL**:
```bash
# Ubuntu/Debian
sudo apt install mysql-server
sudo systemctl start mysql

# Or use Docker
docker run -d --name mysql \
  -e MYSQL_ROOT_PASSWORD=rootpassword \
  -e MYSQL_DATABASE=photogrammetry \
  -p 3307:3306 mysql:8.0
```

**RabbitMQ**:
```bash
# Ubuntu/Debian
sudo apt install rabbitmq-server
sudo systemctl start rabbitmq-server
sudo rabbitmq-plugins enable rabbitmq_management

# Or use Docker
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  rabbitmq:3-management
```

**Or use Docker Compose** (from project root):
```bash
docker-compose up -d
```

### 2. Setup Database

```bash
mysql -u root -p < database_setup.sql
```

This creates:
- Database: `photogrammetry`
- Tables: `Users`, `Projects`
- User: `photogrammetry_user` (if needed)

### 3. Configure

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3307;database=photogrammetry;user=root;password=rootpassword"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": "5672",
    "Username": "guest",
    "Password": "guest",
    "ManagementPort": "15672"
  },
  "Storage": {
    "ProjectsPath": "../Projects",
    "MaxUploadSizeGB": 50
  },
  "Jwt": {
    "Key": "ChangeThisSecretKeyInProduction_AtLeast32Characters!",
    "Issuer": "PhotogrammetryAPI",
    "Audience": "PhotogrammetryUsers",
    "ExpiryDays": 7
  },
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

**Important**: Change `Jwt:Key` in production!

### 4. Run

```bash
dotnet run
```

API available at: http://localhost:5273

Swagger UI: http://localhost:5273/swagger

## API Endpoints

### Authentication

#### Register
```http
POST /api/auth/register
Content-Type: application/json

{
  "username": "johndoe",
  "email": "john@example.com",
  "password": "SecurePass123!"
}
```

**Response**:
```json
{
  "token": "eyJhbGc...",
  "username": "johndoe",
  "email": "john@example.com"
}
```

Password requirements:
- Minimum 6 characters
- At least one uppercase letter
- At least one lowercase letter
- At least one digit
- At least one special character

#### Login
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "john@example.com",
  "password": "SecurePass123!"
}
```

Returns same JWT token format.

### Projects (Authenticated)

All project endpoints require `Authorization: Bearer {token}` header.

#### Upload Project
```http
POST /api/projects/upload
Authorization: Bearer {token}
Content-Type: multipart/form-data

ProjectName: MyBuilding
ZipFile: @images.zip
```

**Response**:
```json
{
  "projectId": 1,
  "message": "Project uploaded successfully and queued for processing"
}
```

**Limits**:
- Maximum file size: 50GB
- Supported format: ZIP only
- Image formats: JPG, JPEG, PNG
- Timeout: 30 minutes

#### Get Project Status
```http
GET /api/projects/{id}/status
Authorization: Bearer {token}
```

**Response**:
```json
{
  "id": 1,
  "name": "MyBuilding",
  "status": 1,
  "createdAt": "2026-01-01T10:00:00Z",
  "processingStartedAt": "2026-01-01T10:05:00Z",
  "completedAt": null,
  "errorMessage": null,
  "downloadUrl": null
}
```

**Status Values**:
- `0`: InQueue - Waiting for worker
- `1`: Processing - Being processed by worker
- `2`: Completed - Model ready for download
- `3`: Failed - Error occurred (see `errorMessage`)

#### List User Projects
```http
GET /api/projects
Authorization: Bearer {token}
```

Returns array of all user's projects.

#### Download Model
```http
GET /api/projects/{id}/download
Authorization: Bearer {token}
```

Returns PLY file (3D model). Only available when status is `Completed`.

### System Statistics

#### Get Stats
```http
GET /api/stats
```

**Response**:
```json
{
  "workers": 2,
  "queue": {
    "name": "photogrammetry-queue",
    "totalMessages": 5,
    "messagesReady": 3,
    "messagesProcessing": 2
  },
  "status": "active"
}
```

**Fields**:
- `workers`: Number of active workers connected to queue
- `messagesReady`: Projects waiting to be processed
- `messagesProcessing`: Projects currently being processed
- `status`: `"active"` if workers available, `"no_workers"` otherwise

## Configuration Details

### Shared Storage

The API and workers share a common `ProjectsPath` directory:

```
Projects/
├── project_1/
│   ├── images/           # Extracted from ZIP (API creates)
│   └── output/           # Generated models (Worker creates)
│       └── dense/
│           └── meshed-delaunay.ply
├── project_2/
└── user_1_guid/         # Temporary ZIP storage
    └── images.zip
```

**Local setup** (development):
```json
"ProjectsPath": "../Projects"
```

**Network setup** (production with NFS):
```json
"ProjectsPath": "/mnt/shared-projects"
```

**Mount NFS**:
```bash
sudo apt install nfs-common
sudo mkdir -p /mnt/shared-projects
sudo mount nfs-server:/exports/photogrammetry /mnt/shared-projects

# Auto-mount on boot
echo "nfs-server:/exports/photogrammetry /mnt/shared-projects nfs defaults 0 0" | sudo tee -a /etc/fstab
```

### Large File Uploads

Configured to handle files up to 50GB:

**Server limits** (`Program.cs`):
- `MaxRequestBodySize`: 50GB
- `MultipartBodyLengthLimit`: 50GB
- `RequestHeadersTimeout`: 30 minutes

**Upload performance**:
- Files are streamed to disk (no memory buffering)
- Memory usage stays constant regardless of file size
- Extraction happens after upload completes

**Client timeout recommendations**:
```bash
# cURL
curl --max-time 1800 ...  # 30 minutes

# Python requests
requests.post(..., timeout=1800)
```

### RabbitMQ Integration

**Queues**:
- `photogrammetry-queue`: Work queue for projects
- `photogrammetry-status`: Status updates from workers
- `photogrammetry-failed`: Dead letter queue for failures

**Features**:
- Manual acknowledgment (prevents message loss)
- Consumer timeout: 24 hours
- Message TTL: 48 hours
- Automatic retry on retriable errors
- Dead letter queue for fatal errors

### Database Schema

**Users Table**:
```sql
CREATE TABLE Users (
    Id INT PRIMARY KEY AUTO_INCREMENT,
    Username VARCHAR(50) UNIQUE NOT NULL,
    Email VARCHAR(100) UNIQUE NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL
);
```

**Projects Table**:
```sql
CREATE TABLE Projects (
    Id INT PRIMARY KEY AUTO_INCREMENT,
    Name VARCHAR(200) NOT NULL,
    UserId INT NOT NULL,
    ZipFilePath VARCHAR(500) NOT NULL,
    Status INT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    ProcessingStartedAt DATETIME NULL,
    CompletedAt DATETIME NULL,
    OutputModelPath VARCHAR(500) NULL,
    ErrorMessage VARCHAR(500) NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);
```

## Testing

### Quick Test (10 images)
```bash
./quick_test.sh
```

Runs complete workflow:
1. Register user
2. Upload 10 test images
3. Monitor processing
4. Download model

Expected time: ~10 minutes with GPU worker.

### Full Dataset Test (128 images)
```bash
./test_full_dataset.sh
```

Processes full dataset. Expected time: ~1-2 hours with GPU worker.

### Monitor Specific Project
```bash
./monitor_project.sh PROJECT_ID TOKEN
```

Polls status every 30 seconds until completion.

### Manual Testing

```bash
# Register
TOKEN=$(curl -s -X POST http://localhost:5273/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"test","email":"test@test.com","password":"Test123!"}' \
  | jq -r '.token')

# Upload
PROJECT_ID=$(curl -s -X POST http://localhost:5273/api/projects/upload \
  -H "Authorization: Bearer $TOKEN" \
  -F "ProjectName=TestProject" \
  -F "ZipFile=@images.zip" \
  | jq -r '.projectId')

# Check status
curl http://localhost:5273/api/projects/$PROJECT_ID/status \
  -H "Authorization: Bearer $TOKEN" | jq

# Download when complete
curl http://localhost:5273/api/projects/$PROJECT_ID/download \
  -H "Authorization: Bearer $TOKEN" \
  --output model.ply
```

## Monitoring

### Application Logs

Logs written to:
- Console (color-coded by level)
- `Logs/api-{date}.log` (rolling daily)

**Log levels**:
- `Information`: Normal operations
- `Warning`: Unexpected but handled situations
- `Error`: Failures and exceptions

**Configure verbosity** in `appsettings.json`:
```json
"Serilog": {
  "MinimumLevel": "Information"  // Debug, Information, Warning, Error
}
```

### RabbitMQ Management

Access at: http://localhost:15672 (guest/guest)

Monitor:
- Queue depth
- Message rates
- Active workers (consumers)
- Failed messages

### Database Queries

```sql
-- Active projects
SELECT * FROM Projects WHERE Status IN (0, 1);

-- Failed projects
SELECT Id, Name, ErrorMessage FROM Projects WHERE Status = 3;

-- Processing times
SELECT 
    AVG(TIMESTAMPDIFF(SECOND, ProcessingStartedAt, CompletedAt)) / 60 as AvgMinutes
FROM Projects 
WHERE Status = 2;

-- User statistics
SELECT 
    u.Username,
    COUNT(p.Id) as TotalProjects,
    SUM(CASE WHEN p.Status = 2 THEN 1 ELSE 0 END) as Completed
FROM Users u
LEFT JOIN Projects p ON u.Id = p.UserId
GROUP BY u.Id;
```

## Troubleshooting

### Database Connection Failed

**Error**: "Unable to connect to MySQL server"

**Solutions**:
```bash
# Check MySQL is running
sudo systemctl status mysql

# Test connection
mysql -u root -p -e "SHOW DATABASES;"

# Verify port
netstat -an | grep 3307

# Check credentials in appsettings.json
```

### RabbitMQ Connection Failed

**Error**: "Connection refused (localhost:5672)"

**Solutions**:
```bash
# Check RabbitMQ is running
sudo systemctl status rabbitmq-server

# Test connection
telnet localhost 5672

# Check firewall
sudo ufw allow 5672
sudo ufw allow 15672
```

### Upload Fails

**Error**: "Request body too large"

**Solutions**:
1. Check file size < 50GB
2. Increase client timeout
3. Verify disk space: `df -h`
4. Check `MaxUploadSizeGB` in config

### Project Stuck in InQueue

**Problem**: Status never changes from InQueue

**Solutions**:
1. Check worker is running
2. Verify shared storage accessible
3. Check RabbitMQ queue: http://localhost:15672
4. Review worker logs for errors

### Download Returns 404

**Problem**: "Model file not found"

**Solutions**:
1. Verify project status is Completed
2. Check file exists: `ls Projects/project_{id}/output/dense/`
3. Ensure shared storage mounted correctly
4. Check file permissions

## Security

### JWT Configuration

**Production recommendations**:
```json
"Jwt": {
  "Key": "Generate-A-Strong-Random-32+Character-Secret-Key-Here!",
  "ExpiryDays": 7
}
```

Generate strong key:
```bash
openssl rand -base64 32
```

### Password Security

- BCrypt with work factor 10
- Minimum complexity requirements enforced
- Passwords never logged or exposed

### API Security

**Implemented**:
- ✅ JWT authentication
- ✅ User-specific project isolation
- ✅ File type validation (ZIP only)
- ✅ SQL injection protection (EF Core)
- ✅ Size limits (50GB max)

**Recommended for production**:
- [ ] HTTPS with SSL certificates
- [ ] CORS configuration
- [ ] Rate limiting
- [ ] API key authentication (optional)
- [ ] Per-user storage quotas
- [ ] Virus scanning on uploads

### HTTPS Setup

```bash
# Generate certificate
dotnet dev-certs https --trust

# Or use Let's Encrypt for production
sudo certbot --nginx -d api.yourdomain.com
```

Update `Program.cs`:
```csharp
app.UseHttpsRedirection();
```

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish

cd publish
dotnet PhotogrammetryAPI.dll
```

### Systemd Service

Create `/etc/systemd/system/photogrammetry-api.service`:

```ini
[Unit]
Description=Photogrammetry API
After=network.target mysql.service rabbitmq-server.service

[Service]
Type=simple
User=www-data
WorkingDirectory=/opt/photogrammetry-api
ExecStart=/usr/bin/dotnet PhotogrammetryAPI.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable photogrammetry-api
sudo systemctl start photogrammetry-api
sudo systemctl status photogrammetry-api
```

### Nginx Reverse Proxy

```nginx
server {
    listen 80;
    server_name api.yourdomain.com;
    
    location / {
        proxy_pass http://localhost:5273;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # Large file upload support
        client_max_body_size 50G;
        proxy_request_buffering off;
    }
}
```

## Performance

### Upload Performance

| File Size | Local | 1 Gbps Network | WiFi |
|-----------|-------|----------------|------|
| 1GB       | 10s   | 10s            | 2 min |
| 5GB       | 50s   | 50s            | 10 min |
| 10GB      | 2 min | 2 min          | 20 min |
| 50GB      | 10 min| 10 min         | 2 hours |

### Database Performance

- Connection pooling enabled
- Indexes on UserId, Status
- Typical query time: <10ms

### Storage Requirements

Per project: ~5x ZIP size
- ZIP file: 1x
- Extracted images: 1x
- Processing files: 2x
- Output models: 1x

Recommend: 100GB minimum, 1TB+ for production

## Project Structure

```
PhotogrammetryAPI/
├── Controllers/
│   ├── AuthController.cs       # Authentication endpoints
│   └── ProjectsController.cs   # Project management
├── Services/
│   ├── AuthService.cs          # User authentication
│   ├── TokenService.cs         # JWT generation
│   ├── ProjectService.cs       # Project management
│   ├── FileStorageService.cs   # File handling
│   ├── RabbitMQService.cs      # Queue publisher
│   └── StatusUpdateService.cs  # Status consumer
├── Models/
│   ├── User.cs                 # User entity
│   ├── Project.cs              # Project entity
│   └── DTOs.cs                 # Data transfer objects
├── Data/
│   └── ApplicationDbContext.cs # EF Core context
├── Logs/                       # Application logs
├── Program.cs                  # Application startup
├── appsettings.json           # Configuration
├── database_setup.sql         # Database schema
└── README.md                  # This file
```

---

**API is ready to handle photogrammetry projects at scale!**

For worker setup, see `../PhotogrammetryWorker/README.md`
