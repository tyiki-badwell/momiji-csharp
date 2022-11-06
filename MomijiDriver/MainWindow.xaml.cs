using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Momiji.Core.Vst.Worker;
using Momiji.Core.Window;

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

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host =>
        {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                return;
            }
            var window = worker.OpenEditor();
            
            window.SetWindowStyle(
                0x00800000 // WS_BORDER
                | 0x00C00000 //WS_CAPTION
                | 0x00010000 //WS_MAXIMIZEBOX
                | 0x00020000 //WS_MINIMIZEBOX
                | 0x00000000 //WS_OVERLAPPED
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
            );

            window.Show(1);
        });
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host => {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                return;
            }
            worker.CloseEditor();
        });
    }

    private void Window_Click(object sender, RoutedEventArgs e)
    {
        var window = AppWindow.Create();
        window.Show();
    }

    private void Window2_Click(object sender, RoutedEventArgs e)
    {
        list.AsParallel().ForAll(host => {
            var manager = (IWindowManager?)host.Services.GetService(typeof(IWindowManager));
            if (manager == null)
            {
                return;
            }
            var window = manager.CreateWindow();
            window.Move(0, 0, 200, 200, true);

            window.SetWindowStyle(
                0x00800000 // WS_BORDER
                | 0x00C00000 //WS_CAPTION
                | 0x00010000 //WS_MAXIMIZEBOX
                | 0x00020000 //WS_MINIMIZEBOX
                | 0x00000000 //WS_OVERLAPPED
                | 0x00080000 //WS_SYSMENU
                | 0x00040000 //WS_THICKFRAME
            );

            window.Show(1);
        });
    }
}
