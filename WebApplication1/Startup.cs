using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Momiji.Interop;
using Momiji.Interop.Kernel32;
using Momiji.Test.Run;
using System;
using System.IO;
using System.Reflection;

namespace WebApplication1
{
    public class Startup
    {
        private ILogger Logger { get; }

        public Startup(IHostingEnvironment env, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            Logger = loggerFactory.CreateLogger<Startup>();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddSingleton<IRunner, Runner>();
            services.Configure<Param>(Configuration.GetSection("Param"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime appLifetime, IOptions<Param> param)
        {
            var dllPathBase =
                Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "lib",
                    Environment.Is64BitProcess ? "64" : "32"
                );
            Logger.LogInformation($"call SetDllDirectory({dllPathBase})");
            DLLMethod.SetDllDirectory(dllPathBase);

            if (env.IsDevelopment())
            {
                app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });

            appLifetime.ApplicationStarted.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Start(param.Value);
                Logger.LogInformation("ApplicationStarted");
            });
            appLifetime.ApplicationStopped.Register(() => {
                app.ApplicationServices.GetService<IRunner>().Stop();
                Logger.LogInformation("ApplicationStopped");
            });
        }
    }
}
