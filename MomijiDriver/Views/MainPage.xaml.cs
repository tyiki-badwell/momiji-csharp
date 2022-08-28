using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml.Controls;

using MomijiDriver.ViewModels;

using Momiji.Core.Timer;
using Momiji.Core.Vst.Worker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;

namespace MomijiDriver.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
    }
    private readonly List<IHost> list = new();

    private async void Run_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var host = VstBridgeWorker.CreateHost(Array.Empty<string>());
        list.Add(host);

        await host.StartAsync().ConfigureAwait(false);
    }

    private async void WindowEx_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        await StopAsync().ConfigureAwait(false);
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

    private async void Close_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var taskSet = new HashSet<Task>();
        foreach (var host in list)
        {
            var worker = (IRunner?)host.Services.GetService(typeof(IRunner));
            if (worker == null)
            {
                continue;
            }

            taskSet.Add(worker.CloseEditorAsync());
        }
        await Task.WhenAll(taskSet).ConfigureAwait(false);
    }

    private void Open_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
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

    private async void Stop_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await StopAsync().ConfigureAwait(false);
    }
}
