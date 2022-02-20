using mixerTest;
using Momiji.Core.Dll;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => true;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});

builder.Services.AddSingleton<IDllManager, DllManager>();
builder.Services.AddSingleton<IRunner, Runner>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCookiePolicy();

app.UseRouting();

//app.UseAuthorization();

app.MapRazorPages();

app.Map("/ws", subApp =>
{
    subApp.UseWebSockets();
    subApp.UseWebSocketToRunner();
});

var logger = app.Services.GetService<ILogger<Program>>();

app.Lifetime.ApplicationStarted.Register(() =>
{
    //app.ApplicationServices.GetService<IRunner>().Start();
    logger?.LogInformation("ApplicationStarted");
});
app.Lifetime.ApplicationStopping.Register(() =>
{

    logger?.LogInformation("ApplicationStopping");
});
app.Lifetime.ApplicationStopped.Register(() =>
{
    app.Services.GetService<IRunner>()?.Cancel();
    logger?.LogInformation("ApplicationStopped");
});

app.Run();
