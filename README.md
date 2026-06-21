# DotnetRoutix-Server
Migrated My RouteX From Nodejs to Dotnetcore

Authored By Krishna
# DotnetRoutix Full Project Migration and Runbook


## 1. Scope
This document explains the complete migration and run steps for the RouteX/Lunchbox solution using the .NET backend in DotnetRoutix-Server and Angular frontend in Frontend/lunchbox-app.

## 2. Final Architecture
- Frontend: Angular app
- Backend: ASP.NET Core (.NET 8) in DotnetRoutix-Server
- Database: MongoDB Atlas (or local MongoDB in Development)
- Auth mode: Direct login compatibility for existing frontend
- OTP mode: Implemented for register and verify-otp, with optional login OTP mode

## 3. Migration Summary
### Completed migration points
1. Consolidated authentication support in DotnetRoutix-Server.
2. Added repository + service abstractions and DI wiring.
3. Added controller endpoints for auth and compatibility stubs.
4. Added Swagger, FluentValidation, and CORS configuration.
5. Added unit tests and repository contract tests.
6. Added OTP generation, storage, and verification flow.
7. Added SMTP email sender for OTP delivery (Gmail SMTP style).
8. Added backward-compatible direct login response for current frontend.
9. Added seeded compatibility users so existing frontend login works.

## 4. Key Project Paths
- Backend root: DotnetRoutix-Server
- Frontend root: Frontend/lunchbox-app
- Backend startup file: DotnetRoutix-Server/Program.cs
- Backend auth logic: DotnetRoutix-Server/Application/Services/AuthService.cs
- Backend auth controller: DotnetRoutix-Server/Api/Controllers/AuthController.cs
- Mongo repository: DotnetRoutix-Server/Infrastructure/Repositories/MongoAuthRepository.cs
- Seeder: DotnetRoutix-Server/Infrastructure/Seeding/AuthSeeder.cs
- Backend configuration: DotnetRoutix-Server/appsettings.json, DotnetRoutix-Server/appsettings.Development.json
- Env template: DotnetRoutix-Server/.env.example
- Frontend environment: Frontend/lunchbox-app/src/environments/environment.ts

## 5. Prerequisites
- .NET SDK 8+
- Node.js and npm
- MongoDB Atlas connection string (or local MongoDB for Development)

## 6. Configuration
## 6.1 Backend config files
Production/default config:
- DotnetRoutix-Server/appsettings.json

Development config:
- DotnetRoutix-Server/appsettings.Development.json

Environment template:
- DotnetRoutix-Server/.env.example

## 6.2 Database selection behavior
- If ASPNETCORE_ENVIRONMENT is empty, app uses Production defaults.
- Production/default reads appsettings.json.
- Development reads appsettings.Development.json.
- You can override Mongo by environment variable MongoDb__ConnectionString.

## 6.3 OTP and email config
In appsettings or environment variables:
- Otp__DebugMode
- Otp__ExpiryMinutes
- Otp__RequireOtpOnLogin
- Email__GmailUser
- Email__GmailAppPassword
- Email__GmailFromEmail
- Email__SmtpHost
- Email__SmtpPort
- Email__EnableSsl

Node-style fallback env variables supported:
- GMAIL_USER
- GMAIL_APP_PASSWORD
- GMAIL_FROM_EMAIL

## 7. Build Steps
Open terminal at repository root and run:

```powershell
Set-Location "c:\Users\v-kbandoju\OneDrive - Microsoft\Desktop\Route-x Front-end\enterprise-lunchbox-lms-prod\DotnetRoutix-Server"
dotnet build "DotnetRoutix.Server.csproj"
```

Run tests:

```powershell
Set-Location "c:\Users\v-kbandoju\OneDrive - Microsoft\Desktop\Route-x Front-end\enterprise-lunchbox-lms-prod\DotnetRoutix-Server"
dotnet test "DotnetRoutix.Server.Tests\DotnetRoutix.Server.Tests.csproj"
```

Build frontend:

```powershell
Set-Location "c:\Users\v-kbandoju\OneDrive - Microsoft\Desktop\Route-x Front-end\enterprise-lunchbox-lms-prod\Frontend\lunchbox-app"
npm run build
```

## 8. Run Steps
## 8.1 Run backend on port 5008

```powershell
Set-Location "c:\Users\v-kbandoju\OneDrive - Microsoft\Desktop\Route-x Front-end\enterprise-lunchbox-lms-prod\DotnetRoutix-Server"
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ASPNETCORE_URLS="http://localhost:5008"
dotnet run --project "DotnetRoutix.Server.csproj"
```

Health check:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:5008/health
```

Swagger:
- http://localhost:5008/swagger

## 8.2 Run frontend

```powershell
Set-Location "c:\Users\v-kbandoju\OneDrive - Microsoft\Desktop\Route-x Front-end\enterprise-lunchbox-lms-prod\Frontend\lunchbox-app"
npm start
```

Frontend URL:
- http://localhost:4200

If 4200 is busy:

```powershell
npm start -- --port 4201
```

## 9. Authentication Modes
## 9.1 Current compatibility mode (default)
- Login endpoint returns session token and user directly.
- Existing frontend login page works without OTP login step.

## 9.2 OTP register and verify flow
- Register creates temp token + OTP.
- Verify-otp validates token, expiry, and code.
- OTP can be emailed via SMTP when credentials are configured.

## 9.3 Optional login OTP mode
Set:
- Otp__RequireOtpOnLogin=true

Then login endpoint issues temp token + OTP instead of direct session.

## 10. Known Test Credentials
Compatibility guest user:
- username: user
- password: user123
- role: customer

Additional seeded users:
- demo_user / LunchBox@123
- admin_user / Admin@123
- captain1 / Captain1@123

Legacy email login is also supported by entering email in username field.

## 11. API Validation Examples
Login:

```powershell
$body = @{ username = 'user'; password = 'user123'; role = 'customer' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5008/api/auth/login' -Method Post -ContentType 'application/json' -Body $body
```

Register:

```powershell
$body = @{ username = 'sample_user'; displayName = 'Sample'; email = 'sample@lunchbox.local'; mobile = '+91 90000 00001'; password = 'Sample@123'; role = 'customer' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5008/api/auth/register' -Method Post -ContentType 'application/json' -Body $body
```

Verify OTP:

```powershell
$body = @{ tempToken = 'TEMP_TOKEN_FROM_REGISTER'; emailOtp = 'OTP_FROM_EMAIL_OR_DEBUG'; mobileOtp = 'OTP_FROM_EMAIL_OR_DEBUG' } | ConvertTo-Json
Invoke-RestMethod -Uri 'http://localhost:5008/api/auth/verify-otp' -Method Post -ContentType 'application/json' -Body $body
```

## 12. Troubleshooting
## 12.1 401 Unauthorized on login
Check:
1. Backend is running on 5008.
2. Frontend environment points to 5008.
3. Username/email and password are exact (case-sensitive).
4. Only one backend process is active.

## 12.2 dotnet build fails with file lock (MSB3021/MSB3027)
Cause:
- DotnetRoutix.Server.exe is still running.

Fix:

```powershell
Stop-Process -Name "DotnetRoutix.Server" -Force -ErrorAction SilentlyContinue
dotnet build "DotnetRoutix.Server.csproj"
```

## 12.3 Port already in use
For backend 5008:

```powershell
netstat -ano | findstr :5008
```

For frontend 4200:

```powershell
netstat -ano | findstr :4200
```

## 12.4 OTP email not sent
If response says OTP generated but email delivery failed:
1. Configure Gmail SMTP variables.
2. Use app password, not normal Gmail password.
3. Restart backend.
4. Re-test register flow.

## 13. Recommended Daily Run Sequence
1. Stop stale backend processes.
2. Start backend at 5008.
3. Verify /health and /swagger.
4. Start frontend at 4200.
5. Login using user/user123 or your DB credentials.
6. For OTP tests, use register + verify-otp flow.

## 14. Optional Hardening Next Steps
1. Move connection strings and SMTP credentials to secure secret storage.
2. Hash passwords in .NET if any plaintext remains from migration era.
3. Add rate-limiting and lockout for repeated login failures.
4. Add dedicated OTP table/collection with attempt counters.
5. Add integration tests for register/verify OTP and email sender mocks.
