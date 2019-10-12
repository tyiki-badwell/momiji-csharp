using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Interop.Kernel32;
using Momiji.Test.Run;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace mixerTest
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

//            services.AddMvc()
//                .SetCompatibilityVersion(CompatibilityVersion.Latest);

            services.AddSingleton<IRunner, Runner>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime, ILogger<Startup> logger)
        {
            var dllPathBase =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    Environment.Is64BitProcess ? "64" : "32"
                );
            logger.LogInformation($"call SetDllDirectory({dllPathBase})");
            DLLMethod.SetDllDirectory(dllPathBase);

            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (libraryName, assembly, searchPath) => {
                logger.LogInformation($"call DllImportResolver({libraryName}, {assembly}, {searchPath})");
                return default;
            });


            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseRouting();

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
            });

            app.Map("/ws", subApp => {
                subApp.UseWebSockets();
                subApp.Use(async (context, next) =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await app.ApplicationServices.GetService<IRunner>().Play(webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });
            });

            appLifetime.ApplicationStarted.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Start();
                logger.LogInformation("ApplicationStarted");
            });
            appLifetime.ApplicationStopped.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Stop();
                logger.LogInformation("ApplicationStopped");
            });
        }
    }
}
