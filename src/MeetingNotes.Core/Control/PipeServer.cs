using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MeetingNotes.Control;

/// <summary>Lado servidor do canal nomeado: roda dentro do cliente de bandeja e atende o servidor MCP.</summary>
public sealed class PipeServer : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly IMeetingController _controller;
    private readonly ILogger _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public PipeServer(IMeetingController controller, ILogger log)
    {
        _controller = controller;
        _log = log;
    }

    public void Start() => _loop ??= Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        _log.LogInformation("Canal nomeado '{Pipe}' aguardando conexões", PipeProtocol.PipeName);
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(PipeProtocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Não foi possível criar o canal nomeado");
                try { await Task.Delay(2000, _cts.Token); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha ao aceitar conexão no canal nomeado");
                server.Dispose();
                continue;
            }

            _ = Task.Run(() => HandleAsync(server));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, Utf8))
            using (var writer = new StreamWriter(pipe, Utf8) { AutoFlush = true })
            {
                string? line;
                while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
                {
                    PipeResponse response;
                    try
                    {
                        var request = JsonSerializer.Deserialize<PipeRequest>(line, PipeProtocol.Json)
                                      ?? throw new InvalidOperationException("requisição vazia");
                        response = new PipeResponse(true, await DispatchAsync(request));
                    }
                    catch (Exception ex)
                    {
                        response = new PipeResponse(false, ex.Message);
                    }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, PipeProtocol.Json));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            _log.LogDebug(ex, "Conexão do canal encerrada");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha atendendo o canal nomeado");
        }
    }

    private async Task<string> DispatchAsync(PipeRequest r) => r.Cmd.ToLowerInvariant() switch
    {
        "ping" => "pong",
        "start" => await _controller.StartAsync(r.Title, _cts.Token),
        "stop" => await _controller.StopAsync(_cts.Token),
        "status" => await _controller.StatusAsync(_cts.Token),
        "transcript" => await _controller.TranscriptAsync(r.SinceSeq, r.MaxEntries, _cts.Token),
        "marker" => await _controller.AddMarkerAsync(r.Kind ?? "nota", r.Text ?? "", _cts.Token),
        "minutes" => await _controller.SaveMinutesAsync(r.Markdown ?? "", _cts.Token),
        "list" => await _controller.ListMeetingsAsync(_cts.Token),
        "load" => await _controller.LoadMeetingAsync(r.MeetingId ?? "", _cts.Token),
        "devices" => await _controller.ListDevicesAsync(_cts.Token),
        _ => throw new InvalidOperationException($"comando desconhecido: {r.Cmd}")
    };

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { /* encerrando */ }
        _cts.Dispose();
    }
}
