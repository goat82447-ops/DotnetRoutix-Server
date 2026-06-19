# Render Hosting Document - DotnetRoutix Server

## Service Target
Deploy this backend service:
- DotnetRoutix-Server

## Recommended Render Service Type
- Type: Web Service
- Runtime: Native .NET (recommended)

This project can also be deployed using Docker. Both options are documented below.

---

## Option A: Native .NET Deployment (Recommended)

### Render UI Fields
- Source: Your GitHub repository
- Branch: main (or your deployment branch)
- Root Directory (Optional): DotnetRoutix-Server
- Runtime: .NET
- Build Command: dotnet restore && dotnet build DotnetRoutix.Server.csproj -c Release
- Start Command: dotnet run --project DotnetRoutix.Server.csproj --urls http://0.0.0.0:$PORT
- Health Check Path: /health

### Pre-Deploy Command
Use only if you want checks before deployment:
- dotnet test DotnetRoutix.Server.Tests/DotnetRoutix.Server.Tests.csproj -c Release

### Environment Variables
#### Environment Variables
Set these in Render -> Service -> Environment:

- ASPNETCORE_ENVIRONMENT=Production
- MongoDb__ConnectionString=<your_atlas_connection_string>
- MongoDb__DatabaseName=lunchbox
- Otp__DebugMode=false
- Otp__ExpiryMinutes=10
- Otp__RequireOtpOnLogin=false
- Email__Provider=gmail
- Email__GmailUser=<your_gmail_address>
- Email__GmailAppPassword=<your_gmail_app_password>
- Email__GmailFromEmail=<your_gmail_address>
- Email__SmtpHost=smtp.gmail.com
- Email__SmtpPort=587
- Email__EnableSsl=true

Optional fallback env variables (also supported):
- GMAIL_USER
- GMAIL_APP_PASSWORD
- GMAIL_FROM_EMAIL

---

## Option B: Docker Deployment

Use this if you want Render Docker mode.

### Dockerfile Location Plan
- Dockerfile Path: DotnetRoutix-Server/Dockerfile
- Docker Build Context Directory: .

### Render UI Fields (Docker)
- Source: Your GitHub repository
- Root Directory (Optional): leave empty (if build context is repository root)
- Environment: Docker
- Dockerfile Path: DotnetRoutix-Server/Dockerfile
- Docker Build Context Directory: .

### Pre-Deploy Command
- dotnet test DotnetRoutix-Server/DotnetRoutix.Server.Tests/DotnetRoutix.Server.Tests.csproj -c Release

### Docker Command
If Render asks for Docker Start Command override, use:
- dotnet DotnetRoutix.Server.dll

If Render asks for local docker run equivalent:
- docker build -f DotnetRoutix-Server/Dockerfile -t dotnetroutix-server .
- docker run -p 5008:5008 --env ASPNETCORE_URLS=http://0.0.0.0:5008 dotnetroutix-server

### Environment Variables
#### Environment Variables
Use the same environment variable set listed in Option A.

---

## API Base URL Update
After deployment, update frontend API base URL to your Render service URL.

Example:
- authApiBase=https://your-dotnet-service.onrender.com

Then rebuild frontend.

---

## Verification Checklist
1. Open /health endpoint and confirm status ok.
2. Open /swagger endpoint and confirm API docs load.
3. Test login endpoint:
   - POST /api/auth/login
4. Test register/verify OTP flow:
   - POST /api/auth/register
   - POST /api/auth/verify-otp
5. Check Render logs for SMTP delivery errors if OTP emails fail.

---

## Notes
- Current repository has no Dockerfile inside DotnetRoutix-Server yet.
- If you choose Docker deployment, add DotnetRoutix-Server/Dockerfile first.
- Native .NET deployment is simpler and recommended for this project.
