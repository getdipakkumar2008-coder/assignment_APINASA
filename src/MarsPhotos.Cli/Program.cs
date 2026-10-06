using MarsPhotos.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // finish writing the partial summary instead of killing the process
    cts.Cancel();
};

return await CliApp.RunAsync(args, Console.Out, Console.Error, cancellationToken: cts.Token);
