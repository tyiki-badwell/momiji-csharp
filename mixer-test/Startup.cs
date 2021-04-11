using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core;
using System;

namespace mixerTest
{
    public class Startup
    {
        public Startup()
        {
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddSingleton<IDllManager, DllManager>();
            services.AddSingleton<IRunner, Runner>();

            services.AddTransient<WebSocketMiddleware>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime appLifetime, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<Startup>();

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
                subApp.UseMiddleware<WebSocketMiddleware>();
            });

            appLifetime?.ApplicationStarted.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Start();
                logger.LogInformation("ApplicationStarted");
            });
            appLifetime?.ApplicationStopped.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Cancel();
                logger.LogInformation("ApplicationStopped");
            });
        }
    }
}
