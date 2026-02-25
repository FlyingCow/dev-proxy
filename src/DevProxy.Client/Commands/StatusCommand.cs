using System.CommandLine;

namespace DevProxy.Client.Commands;

public static class StatusCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string>("--server", "The proxy server URL");
        serverOption.AddAlias("-s");
        serverOption.IsRequired = true;

        var command = new Command("status", "Check the status of the proxy server");
        command.AddOption(serverOption);

        command.SetHandler(async (server) =>
        {
            Console.WriteLine($"Checking server status: {server}");

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                var healthUrl = server!.TrimEnd('/') + "/health";
                var response = await httpClient.GetAsync(healthUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Server is healthy: {content}");
                }
                else
                {
                    Console.WriteLine($"Server returned status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to server: {ex.Message}");
            }
        }, serverOption);

        return command;
    }
}
