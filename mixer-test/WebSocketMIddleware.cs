using System.Net.WebSockets;

namespace mixerTest;

public class WebSocketToRunnerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly IRunner _runner;

    public WebSocketToRunnerMiddleware(IConfiguration configuration, ILoggerFactory loggerFactory, IRunner runner, RequestDelegate next)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = loggerFactory.CreateLogger<WebSocketToRunnerMiddleware>();
        _runner = runner;
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context == default)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogInformation("[middleware web socket] start.");
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            try
            {
                await _runner.AcceptWebSocket(webSocket).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[middleware web socket] operation canceled.");
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, "[middleware web socket] exception");
                throw;
            }
            _logger.LogInformation("[middleware web socket] end.");

            if (webSocket.State != WebSocketState.Open)
            {
                //openでないときにnextを動かすとエラーになるのでここで終わる
                return;
            }
        }
        //TODO 正しい終わらせ方
        await _next(context).ConfigureAwait(false);
    }
}

public static class WebSocketToRunnerMiddlewareExtensions
{
    public static IApplicationBuilder UseWebSocketToRunner(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<WebSocketToRunnerMiddleware>();
    }
}
