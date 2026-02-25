using System.CommandLine;
using DevProxy.Client.Commands;

var rootCommand = new RootCommand("DevProxy Client - Development proxy tunnel client");

rootCommand.AddCommand(ConnectCommand.Create());
rootCommand.AddCommand(StatusCommand.Create());

return await rootCommand.InvokeAsync(args);
