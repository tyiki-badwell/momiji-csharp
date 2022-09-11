using Microsoft.Extensions.Hosting;
using Momiji.Core.Vst.Worker;
using MomijiDriver.Helpers;

namespace MomijiDriver;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
    }


    private readonly List<IHost> list = new();

    public async Task RunAsync()
    {
        var host = VstBridgeWorker.CreateHost(Array.Empty<string>());
        list.Add(host);

        await host.StartAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        var taskSet = new HashSet<Task>();

        foreach (var host in list)
        {
            taskSet.Add(host.StopAsync());
        }
        await Task.WhenAll(taskSet).ConfigureAwait(false);
        foreach (var host in list)
        {
            host.Dispose();
        }
        list.Clear();
    }

    public void OpenEditor()
    {
        foreach (var host in list)
        {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                continue;
            }
            worker.OpenEditor();
        }
    }

    public void CloseEditor()
    {
        foreach (var host in list)
        {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                continue;
            }
            worker.CloseEditor();
        }
    }

    private void WindowEx_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {

    }

    private async void WindowEx_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        await StopAsync().ConfigureAwait(false);
    }

}
