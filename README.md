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

### Quick Start - Server Only

1. Copy the example environment file:
   ```bash
   cp .env.example .env
   ```

2. Start the server:
   ```bash
   docker compose up -d
   ```

3. Verify the server is running:
   ```bash
   curl http://localhost:8080/health
   ```

### Running Server and Client Together

To run both server and client in Docker:

```bash
# Configure client settings in .env
DEVPROXY_SERVER=http://devproxy-server:8080
DEVPROXY_CLIENT_ID=my-dev-machine
DEVPROXY_LOCAL_URL=http://host.docker.internal:5000

# Start both server and client
docker compose --profile client up -d
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DEVPROXY_SERVER_PORT` | Port to expose the server | `8080` |
| `DEVPROXY_SERVER` | Proxy server URL (for client) | `http://devproxy-server:8080` |
| `DEVPROXY_CLIENT_ID` | Unique client ID for tunnel | - |
| `DEVPROXY_LOCAL_URL` | Local service URL to forward to | - |

### Accessing Host Services

To forward requests to a service running on your host machine, use `host.docker.internal`:

```bash
DEVPROXY_LOCAL_URL=http://host.docker.internal:3000
```

### Docker Commands

```bash
# Start server only
docker compose up -d

# Start server and client
docker compose --profile client up -d

# View logs
docker compose logs -f

# View specific service logs
docker compose logs -f devproxy-server
docker compose logs -f devproxy-client

# Stop all services
docker compose --profile client down

# Rebuild after code changes
docker compose up -d --build

# Rebuild with client profile
docker compose --profile client up -d --build
```

### Connecting External Client to Dockerized Server

If you want to run the server in Docker but the client natively:

```bash
# Start only the server
docker compose up -d

# Connect with native client
dotnet run --project src/DevProxy.Client -- connect \
  --server http://localhost:8080 \
  --id mydev \
  --local http://localhost:5000
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

## Horizontal Scaling

The server supports horizontal scaling with multiple instances behind a load balancer. Two backplane options are available:

| Option | Pros | Cons |
|--------|------|------|
| **Redis** | Efficient pub/sub, scales well | Requires Redis infrastructure |
| **Peer-to-Peer** | No external dependencies | Each server queries all peers |

### Architecture

```
                                    ┌─────────────────┐
                                    │  Load Balancer  │
                                    │     (nginx)     │
                                    └────────┬────────┘
                           ┌─────────────────┼─────────────────┐
                           ▼                 ▼                 ▼
                    ┌─────────────┐   ┌─────────────┐   ┌─────────────┐
                    │  Server 1   │◄─►│  Server 2   │◄─►│  Server N   │
                    └──────┬──────┘   └──────┬──────┘   └──────┬──────┘
                           │                 │                 │
                           └─────────────────┴─────────────────┘
                                             │
                              ┌──────────────┴──────────────┐
                              ▼                             ▼
                    ┌─────────────────┐           ┌─────────────────┐
                    │      Redis      │    OR     │  Direct HTTP    │
                    │   (optional)    │           │  (peer-to-peer) │
                    └─────────────────┘           └─────────────────┘
```

### Option 1: Redis Backplane

Best for larger deployments with many server instances.

**Quick Start:**
```bash
docker compose -f docker-compose.yml -f docker-compose.scaled.yml up -d
```

**Configuration:**
```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

**Or via environment variable:**
```bash
ConnectionStrings__Redis=redis-server:6379
```

### Option 2: Peer-to-Peer (No Redis)

Servers communicate directly via HTTP. Best for smaller clusters (2-5 servers).

**Quick Start:**
```bash
docker compose -f docker-compose.yml -f docker-compose.peers.yml up -d
```

**Configuration:**
```json
{
  "Peers": [
    "http://server2:8080",
    "http://server3:8080"
  ]
}
```

**Or via environment variables:**
```bash
Peers__0=http://server2:8080
Peers__1=http://server3:8080
```

### How It Works

1. Request arrives at any server instance via load balancer
2. Server checks if the client is connected locally
3. If not local:
   - **Redis mode**: Queries Redis for client location, forwards via pub/sub
   - **Peer mode**: Queries all peers in parallel via HTTP
4. The owning server forwards to the client and returns the response

### Load Balancer Requirements

When using a load balancer (nginx, HAProxy, etc.):

1. **WebSocket Support**: Must support WebSocket connections with proper upgrade headers
2. **Health Checks**: Use `/health` endpoint for backend health checks
3. **Sticky Sessions (Optional)**: Not required with Redis backplane, but can reduce cross-instance traffic

**Example nginx configuration:**
```nginx
upstream devproxy_servers {
    server server1:8080;
    server server2:8080;
}

server {
    listen 80;

    location / {
        proxy_pass http://devproxy_servers;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400;
    }
}
```

### IIS with ARR (Application Request Routing)

For IIS-based load balancing:

1. Install ARR on your IIS server
2. Create a Server Farm with your backend servers
3. Configure health checks to use `/health`
4. Enable WebSocket support in ARR settings
5. Set the Redis connection string on all backend servers

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
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Redis": ""
  },
  "Peers": []
}
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` or `Production` |
| `ASPNETCORE_URLS` | Override default URLs (e.g., `http://+:5080`) |
| `ConnectionStrings__Redis` | Redis connection string for horizontal scaling |
| `Peers__0`, `Peers__1`, ... | Peer server URLs for direct communication |

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
