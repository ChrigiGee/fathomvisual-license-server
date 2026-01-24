# FathomVisual License Server

A .NET 9.0 license server for FathomVisual product licensing and activation management.

## Features

- License generation and validation
- Hardware-based activation management
- Multi-tier license support (Free, Professional, Enterprise)
- Health check endpoint at `/health`
- Swagger/OpenAPI documentation (development mode)

## Local Development

### Prerequisites

- .NET 9.0 SDK
- SQLite (included, no setup required)

### Running Locally

```bash
cd src/FathomVisual.LicenseServer
dotnet restore
dotnet run
```

The server will start at `https://localhost:5001` (or `http://localhost:5000`).

- Swagger UI: `https://localhost:5001/swagger`
- Health check: `https://localhost:5001/health`

### Local Database

By default, the server uses SQLite with a local `licenses.db` file. No additional database setup is required for development.

## Deployment to Render.com

### Option 1: Using Blueprint (Recommended)

1. Push this repository to GitHub
2. Go to [Render Dashboard](https://dashboard.render.com)
3. Click "New" > "Blueprint"
4. Connect your GitHub repository
5. Select the repository and branch
6. Render will automatically detect `render.yaml` and create:
   - A PostgreSQL database (`fathomvisual-license-db`)
   - A web service (`fathomvisual-license-server`)

### Option 2: Manual Deployment

1. **Create PostgreSQL Database:**
   - Go to Render Dashboard > New > PostgreSQL
   - Name: `fathomvisual-license-db`
   - Plan: Free (or paid for production)
   - Note the Internal Database URL

2. **Create Web Service:**
   - Go to Render Dashboard > New > Web Service
   - Connect your GitHub repository
   - Name: `fathomvisual-license-server`
   - Runtime: Docker
   - Plan: Free (or paid for production)

3. **Configure Environment Variables:**
   - `DATABASE_URL`: Set to the PostgreSQL connection string from step 1
   - `ASPNETCORE_ENVIRONMENT`: `Production`
   - `CORS_ORIGINS`: `*` (or specific origins for security)
   - `LICENSE_KEYS_PATH`: `/app/keys`

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DATABASE_URL` | PostgreSQL connection string (Render format: `postgres://user:pass@host:port/db`) | SQLite used if not set |
| `ASPNETCORE_ENVIRONMENT` | Environment name (`Development` or `Production`) | `Production` |
| `CORS_ORIGINS` | Comma-separated CORS origins, or `*` for all | `*` |
| `LICENSE_KEYS_PATH` | Path to store license key files | `keys` |

## API Endpoints

### Health Check

```
GET /health
```

Returns `Healthy` if the server and database are operational.

### License Management (requires admin authentication)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/licenses` | List all licenses |
| POST | `/api/admin/licenses` | Create a new license |
| GET | `/api/admin/licenses/{id}` | Get license by ID |
| DELETE | `/api/admin/licenses/{id}` | Delete a license |

### License Validation

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/licenses/validate` | Validate a license key |
| POST | `/api/licenses/activate` | Activate a license on a device |
| POST | `/api/licenses/deactivate` | Deactivate a license from a device |

## Database Migrations

For production deployments, the server automatically applies pending migrations on startup.

To create a new migration locally:

```bash
dotnet ef migrations add MigrationName --context LicenseDbContext
```

## Docker

### Build Locally

```bash
docker build -t fathomvisual-license-server .
```

### Run Locally with Docker

```bash
docker run -p 10000:10000 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  fathomvisual-license-server
```

### Run with PostgreSQL

```bash
docker run -p 10000:10000 \
  -e DATABASE_URL="postgres://user:password@host:5432/licensedb" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  fathomvisual-license-server
```

## Security Notes

- In production, configure `CORS_ORIGINS` to specific allowed origins
- Use HTTPS (Render.com handles this automatically)
- Store sensitive keys in environment variables, not in code
- The `/api/admin/*` endpoints should be protected with authentication

## License

Copyright FathomOS. All rights reserved.
