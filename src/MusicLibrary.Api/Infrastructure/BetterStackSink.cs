using System.Net.Http.Headers;
using System.Text;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MusicLibrary.Api.Infrastructure;

internal sealed class BetterStackSink : ILogEventSink, IDisposable
{
    private static readonly CompactJsonFormatter Formatter = new();
    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<LogEvent> _events = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _deliveryTask;

    public BetterStackSink(Uri endpoint, string sourceToken)
    {
        _endpoint = endpoint;
        _httpClient = CreateHttpClient(sourceToken);
        _deliveryTask = DeliverAsync();
    }

    public void Emit(LogEvent logEvent)
    {
        _events.Writer.TryWrite(logEvent);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _events.Writer.TryComplete();
        _deliveryTask.GetAwaiter().GetResult();
        _shutdown.Dispose();
        _httpClient.Dispose();
    }

    private async Task DeliverAsync()
    {
        await foreach (var logEvent in _events.Reader.ReadAllAsync())
        {
            try
            {
                using var payload = new StringWriter();
                Formatter.Format(logEvent, payload);
                using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_endpoint, content, _shutdown.Token);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
            }
        }
    }

    private static HttpClient CreateHttpClient(string sourceToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);
        return client;
    }
}