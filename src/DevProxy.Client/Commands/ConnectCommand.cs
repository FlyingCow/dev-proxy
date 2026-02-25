using System.CommandLine;
using DevProxy.Client.Services;
using Microsoft.Extensions.Logging;

namespace DevProxy.Client.Commands;

public static class ConnectCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string>("--server", "The proxy server URL (e.g., https://proxy.example.com)");
        serverOption.AddAlias("-s");
        serverOption.IsRequired = true;

        var idOption = new Option<string>("--id", "Your unique client ID for this tunnel");
        idOption.AddAlias("-i");
        idOption.IsRequired = true;

        var localOption = new Option<string>("--local", "The local service URL to forward requests to (e.g., http://localhost:5000)");
        localOption.AddAlias("-l");
        localOption.IsRequired = true;

        var command = new Command("connect", "Connect to the proxy server and create a tunnel");
        command.AddOption(serverOption);
        command.AddOption(idOption);
        command.AddOption(localOption);

        command.SetHandler(async (server, id, local) =>
        {
            await ExecuteAsync(server!, id!, local!);
        }, serverOption, idOption, localOption);

        return command;
    }

    private static async Task ExecuteAsync(string server, string id, string local)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var tunnelClientLogger = loggerFactory.CreateLogger<TunnelClient>();
        var localHandlerLogger = loggerFactory.CreateLogger<LocalRequestHandler>();

        Console.WriteLine($"DevProxy Client");
        Console.WriteLine($"===============");
        Console.WriteLine($"Server: {server}");
        Console.WriteLine($"Client ID: {id}");
        Console.WriteLine($"Local service: {local}");
        Console.WriteLine();

        var localHandler = new LocalRequestHandler(local, localHandlerLogger);
        var tunnelClient = new TunnelClient(server, id, localHandler, tunnelClientLogger);

        tunnelClient.OnConnected += clientId =>
        {
            Console.WriteLine($"[Connected] Tunnel established for '{clientId}'");
            Console.WriteLine($"[Info] External URL: {server.TrimEnd('/')}/{clientId}/");
            Console.WriteLine($"[Info] Press Ctrl+C to disconnect");
            Console.WriteLine();
        };

        tunnelClient.OnDisconnected += clientId =>
        {
            Console.WriteLine($"[Disconnected] Tunnel for '{clientId}' closed");
        };

        tunnelClient.OnError += error =>
        {
            Console.WriteLine($"[Error] {error}");
        };

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[Info] Shutting down...");
            cts.Cancel();
        };

        try
        {
            await tunnelClient.ConnectAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            await tunnelClient.DisposeAsync();
            Console.WriteLine("[Info] Disconnected");
        }
    }
}
