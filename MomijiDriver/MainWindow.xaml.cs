using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Momiji.Core.Vst.Worker;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MomijiDriver;
/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private readonly List<IHost> list = new();

    private async Task RunAsync()
    {
        var host = VstBridgeWorker.CreateHost(Array.Empty<string>());
        list.Add(host);

        await host.StartAsync().ConfigureAwait(false);
    }

    private async Task StopAsync()
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

    private void OpenEditor()
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

    private void CloseEditor()
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

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {

    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync().ConfigureAwait(false);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenEditor();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseEditor();

        Microsoft.UI.Windowing.AppWindow.Create();
    }
}
