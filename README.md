# DevProxy - Development Proxy Server

A .NET solution providing a development proxy server (similar to localtunnel/ngrok) that enables:
- **Inbound tunneling**: External HTTP requests → Proxy Server → Dev machines
- **Outbound proxying**: Dev machines → Proxy Server → External services

## Architecture

```
┌─────────────────┐     HTTPS      ┌─────────────────┐    WebSocket    ┌─────────────────┐
│ External Service│ ──────────────►│  Proxy Server   │◄──────────────► │   Dev Machine   │
│                 │                │   (IIS/Kestrel) │                 │    (Client)     │
└─────────────────┘                └─────────────────┘                 └─────────────────┘
                                          │                                    │
                                          │                                    ▼
                                          │                            ┌─────────────────┐
                                          │                            │  Local Service  │
                                          │                            │ (localhost:5000)│
                                          └────────────────────────────└─────────────────┘
```

## Solution Structure

```
DevProxy/
├── DevProxy.sln
└── src/
    ├── DevProxy.Server/           # ASP.NET Core server (IIS/Windows Service compatible)
    │   ├── Program.cs
    │   ├── web.config             # IIS configuration
    │   ├── Controllers/
    │   │   └── TunnelController.cs
    │   ├── Middleware/
    │   │   └── TunnelMiddleware.cs
    │   ├── Models/
    │   │   └── ClientConnection.cs
    │   └── Services/
    │       ├── ClientConnectionManager.cs
    │       ├── RequestForwarder.cs
    │       └── OutboundProxyService.cs
    │
    ├── DevProxy.Client/           # CLI application for dev machines
    │   ├── Program.cs
    │   ├── Commands/
    │   │   ├── ConnectCommand.cs
    │   │   └── StatusCommand.cs
    │   └── Services/
    │       ├── TunnelClient.cs
    │       ├── LocalRequestHandler.cs
    │       └── OutboundRequestService.cs
    │
    └── DevProxy.Shared/           # Shared models and protocols
        ├── Messages/
        │   ├── TunnelMessage.cs
        │   ├── TunnelHttpRequestMessage.cs
        │   ├── TunnelHttpResponseMessage.cs
        │   └── ControlMessage.cs
        └── Protocol/
            └── MessageSerializer.cs
```

## Prerequisites

- .NET 9.0 SDK or later
- For IIS deployment: ASP.NET Core Hosting Bundle for .NET 9
- For Docker deployment: Docker and Docker Compose

## Building

```bash
# Build the entire solution
dotnet build

# Build in Release mode
dotnet build -c Release
```

## Running Locally (Development)

### Start the Server

```bash
dotnet run --project src/DevProxy.Server
```

The server will start on `http://localhost:5000` by default.

### Connect a Client

```bash
dotnet run --project src/DevProxy.Client -- connect \
  --server http://localhost:5000 \
  --id mydev \
  --local http://localhost:8080
```

### Test the Tunnel

Once connected, requests to `http://localhost:5000/mydev/api/endpoint` will be forwarded to `http://localhost:8080/api/endpoint`.

```bash
# Check server health
curl http://localhost:5000/health

# Test tunnel (assuming local service is running on port 8080)
curl http://localhost:5000/mydev/api/your-endpoint
```

## Running with Docker

### Quick Start

1. Copy the example environment file:
   ```bash
   cp .env.example .env
   ```

2. Edit `.env` with your configuration:
   ```bash
   DEVPROXY_SERVER=https://proxy.example.com
   DEVPROXY_CLIENT_ID=my-dev-machine
   DEVPROXY_LOCAL_URL=http://host.docker.internal:5000
   ```

3. Start the client:
   ```bash
   docker compose up -d
   ```

### Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `DEVPROXY_SERVER` | Proxy server URL | `https://proxy.example.com` |
| `DEVPROXY_CLIENT_ID` | Unique client ID for this tunnel | `my-dev-machine` |
| `DEVPROXY_LOCAL_URL` | Local service URL to forward requests to | `http://host.docker.internal:5000` |

### Accessing Host Services

To forward requests to a service running on your host machine, use `host.docker.internal`:

```bash
DEVPROXY_LOCAL_URL=http://host.docker.internal:3000
```

### Docker Commands

```bash
# Start the client
docker compose up -d

# View logs
docker compose logs -f

# Stop the client
docker compose down

# Rebuild after code changes
docker compose up -d --build
```

### Running with Inline Parameters

You can also run without a `.env` file:

```bash
DEVPROXY_SERVER=https://proxy.example.com \
DEVPROXY_CLIENT_ID=mydev \
DEVPROXY_LOCAL_URL=http://host.docker.internal:8080 \
docker compose up -d
```

## CLI Reference

### Connect Command

Establishes a tunnel connection to the proxy server.

```bash
devproxy connect --server <url> --id <client-id> --local <local-url>
```

| Option | Alias | Required | Description |
|--------|-------|----------|-------------|
| `--server` | `-s` | Yes | Proxy server URL (e.g., `https://proxy.example.com`) |
| `--id` | `-i` | Yes | Unique client ID for this tunnel |
| `--local` | `-l` | Yes | Local service URL to forward requests to |

**Example:**
```bash
dotnet run --project src/DevProxy.Client -- connect \
  -s https://proxy.example.com \
  -i dev-machine-1 \
  -l http://localhost:3000
```

### Status Command

Checks the health status of the proxy server.

```bash
devproxy status --server <url>
```

| Option | Alias | Required | Description |
|--------|-------|----------|-------------|
| `--server` | `-s` | Yes | Proxy server URL |

**Example:**
```bash
dotnet run --project src/DevProxy.Client -- status -s https://proxy.example.com
```

## IIS Deployment

### 1. Install Prerequisites

On your Windows Server:

1. Install the [.NET 9.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0)
2. Enable IIS WebSocket Protocol:
   - Open **Server Manager** → **Add Roles and Features**
   - Navigate to **Web Server (IIS)** → **Web Server** → **Application Development**
   - Check **WebSocket Protocol**
   - Complete the installation

### 2. Publish the Application

```bash
dotnet publish src/DevProxy.Server -c Release -o publish/server
```

### 3. Configure IIS

1. **Create Application Pool:**
   - Open IIS Manager
   - Right-click **Application Pools** → **Add Application Pool**
   - Name: `DevProxyPool`
   - .NET CLR Version: **No Managed Code**
   - Managed Pipeline Mode: **Integrated**

2. **Create Website:**
   - Right-click **Sites** → **Add Website**
   - Site name: `DevProxy`
   - Application pool: `DevProxyPool`
   - Physical path: Point to your published folder
   - Binding: Configure your hostname and port (e.g., `proxy.example.com:443`)

3. **Configure HTTPS (recommended):**
   - Select your site → **Bindings**
   - Add HTTPS binding with your SSL certificate

4. **Set Permissions:**
   - Ensure the Application Pool identity (`IIS AppPool\DevProxyPool`) has read/execute permissions on the published folder

### 4. Verify Deployment

```bash
# Check health endpoint
curl https://proxy.example.com/health

# Expected response
{"status":"healthy"}
```

## Request Flow

### Inbound (External → Dev Machine)

1. External client sends: `POST https://proxy.example.com/dev1/api/users`
2. Server extracts client ID `dev1` from path
3. Server finds WebSocket connection for `dev1`
4. Server sends request message through WebSocket
5. Client receives request, forwards to `http://localhost:5000/api/users`
6. Client sends response back through WebSocket
7. Server returns HTTP response to external caller

### Outbound (Dev Machine → External)

1. Client sends outbound request message through WebSocket
2. Server receives and executes HTTP request to external URL
3. Server sends response back through WebSocket
4. Client receives response

## Message Protocol

Messages are JSON-serialized and sent over WebSocket:

| Type | Description |
|------|-------------|
| `REQUEST` | HTTP request to forward (inbound or outbound) |
| `RESPONSE` | HTTP response |
| `REGISTER` | Client registration |
| `HEARTBEAT` | Keep-alive ping/pong |

## Configuration

### Server Configuration (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` or `Production` |
| `ASPNETCORE_URLS` | Override default URLs (e.g., `http://+:5080`) |

## Troubleshooting

### WebSocket Connection Fails

- Ensure WebSocket Protocol is enabled in IIS
- Check that no firewall is blocking WebSocket connections
- Verify the client can reach the server URL

### Client Registration Fails

- Ensure the client ID is unique (not already in use)
- Check server logs for registration errors

### Requests Not Being Forwarded

- Verify the local service is running on the specified port
- Check that the path matches the expected format: `/{clientId}/...`
- Review client logs for forwarding errors

### IIS 502 Bad Gateway

- Verify the .NET Hosting Bundle is installed
- Check the Application Pool is running
- Review stdout logs in `.\logs\stdout` (enable in web.config)

## Enabling Stdout Logging (IIS)

Edit `web.config` to enable logging:

```xml
<aspNetCore processPath="dotnet"
            arguments=".\DevProxy.Server.dll"
            stdoutLogEnabled="true"
            stdoutLogFile=".\logs\stdout"
            hostingModel="inprocess">
```

Create the `logs` folder and ensure the Application Pool identity has write permissions.

## License

MIT
