using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace mixerTest
{
    public class WebSocketMiddleware
        : IMiddleware
    {
        private IConfiguration Configuration { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger Logger { get; }
        private IRunner Runner { get; }

        public WebSocketMiddleware(IConfiguration configuration, ILoggerFactory loggerFactory, IRunner runner)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            LoggerFactory = loggerFactory;
            Logger = LoggerFactory.CreateLogger<WebSocketMiddleware>();
            Runner = runner;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context == default)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (next == default)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                try
                {
                    await Runner.Play(webSocket).ConfigureAwait(false);
                }
                catch (OperationCanceledException e)
                {
                    Logger.LogInformation("[middleware web socket] operation canceled.");
                }
                catch (Exception e)
                {
                    Logger.LogInformation(e, "[middleware web socket] exception");
                    throw;
                }

                if (webSocket.State != WebSocketState.Open)
                {
                    //openでないときにnextを動かすとエラーになるのでここで終わる
                    return;
                }
            }
            await next(context).ConfigureAwait(false);
        }
    }
}
