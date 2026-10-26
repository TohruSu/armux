using System.IO.Pipes;
using System.Text;
using Armux.Core;
using Microsoft.UI.Dispatching;

namespace Armux.App;

/// <summary>
/// Serves the control API (<see cref="Ipc"/>) for the <c>armux</c> CLI. Accepts one connection at a time; each
/// request runs on the UI thread. <see cref="PipeOptions.CurrentUserOnly"/> keeps other users out.
/// </summary>
public sealed class PipeServer(DispatcherQueue ui, Func<Request, Response> handle) : IDisposable
{
    public static string PipeName { get; } = Ipc.PipeNameFor(Environment.ProcessId);

    readonly CancellationTokenSource stop = new();

    public void Start() => _ = Task.Run(LoopAsync);

    async Task LoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(stop.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                var response = Ipc.DecodeRequest(line ?? "") is { } request
                    ? await OnUiAsync(request)
                    : new Response { Error = "bad request" };
                await writer.WriteLineAsync(Ipc.Encode(response));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
            {
                App.Log(nameof(PipeServer), e);
                await Task.Delay(200);
            }
        }
    }

    Task<Response> OnUiAsync(Request request)
    {
        var done = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!ui.TryEnqueue(() =>
        {
            try { done.SetResult(handle(request)); }
            catch (Exception e) { App.Log(nameof(PipeServer), e); done.SetResult(new Response { Error = e.Message }); }
        }))
            done.SetResult(new Response { Error = "armux is closing" });
        return done.Task;
    }

    public void Dispose() => stop.Cancel();
}
