using MeetingNotesMcp.Audio;
using MeetingNotesMcp.Meeting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using NAudio.CoreAudioApi;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";
if (mode != "serve")
{
    // Modos de linha de comando: acentos corretos no console. (Em "serve" o stdout é do protocolo MCP.)
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
}

switch (mode)
{
    case "devices":
        foreach (var d in AudioDevices.List())
            Console.WriteLine($"{(d.Flow == DataFlow.Render ? "SAÍDA " : "ENTRADA")} | {d.Name}{(d.IsDefault ? " (padrão)" : "")}");
        return 0;

    case "test":
        return await RunTestAsync(args.Length > 1 && int.TryParse(args[1], out var secs) ? secs : 20);

    case "serve":
        await RunServerAsync(args);
        return 0;

    default:
        Console.Error.WriteLine("Uso: MeetingNotesMcp [serve | devices | test <segundos>]");
        return 1;
}

static async Task RunServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Em stdio, o stdout é o canal do protocolo MCP: todo log vai para stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    builder.Services.AddSingleton(MeetingOptions.FromEnvironment());
    builder.Services.AddSingleton<MeetingService>();
    builder.Services
        .AddMcpServer(o => o.ServerInfo = new Implementation { Name = "meeting-notes", Version = "0.1.0" })
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithPromptsFromAssembly();

    var host = builder.Build();

    // Se o cliente MCP fechar no meio de uma reunião, encerra e grava o que já existe.
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        var svc = host.Services.GetRequiredService<MeetingService>();
        if (svc.Current is not null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { svc.StopAsync(cts.Token).GetAwaiter().GetResult(); } catch { /* melhor esforço */ }
        }
    });

    await host.RunAsync();
}

static async Task<int> RunTestAsync(int seconds)
{
    using var loggerFactory = LoggerFactory.Create(b => b
        .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
        .SetMinimumLevel(LogLevel.Information));

    using var service = new MeetingService(MeetingOptions.FromEnvironment(), loggerFactory);
    MeetingSession session;
    try
    {
        session = await service.StartAsync("teste", CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Falha ao iniciar: {ex.Message}");
        return 2;
    }

    Console.WriteLine($"Gravando por {seconds}s. Fale no microfone ou toque um áudio nas caixas...");
    var lastSeq = 0;
    var end = DateTime.Now.AddSeconds(seconds);
    while (DateTime.Now < end)
    {
        await Task.Delay(500);
        foreach (var e in session.GetSince(lastSeq, 100))
        {
            Console.WriteLine(e.Render());
            lastSeq = e.Seq;
        }
    }

    Console.WriteLine("Encerrando e transcrevendo o restante da fila...");
    var done = await service.StopAsync(CancellationToken.None);
    foreach (var e in done!.GetSince(lastSeq, 10_000)) Console.WriteLine(e.Render());
    Console.WriteLine($"Arquivos em: {done.Folder}");
    return 0;
}
