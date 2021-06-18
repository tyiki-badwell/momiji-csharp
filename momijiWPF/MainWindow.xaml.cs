using Microsoft.Extensions.Hosting;
using Momiji.Core.Vst.Worker;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace momijiWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private List<IHost> list = new();

        private async void Button_Click_Run(object sender, RoutedEventArgs e)
        {
            var host = VstBridgeWorker.CreateHostBuilder(null).Build();
            list.Add(host);

            await host.StartAsync().ConfigureAwait(false);
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            await StopAsync().ConfigureAwait(false);
        }

        private void Button_Click_Open(object sender, RoutedEventArgs e)
        {
            foreach (var host in list)
            {
                var worker = (IRunner)host.Services.GetService(typeof(IRunner));
                worker.OpenEditor();
            }
        }

        private async void Button_Click_Close(object sender, RoutedEventArgs e)
        {
            var taskSet = new HashSet<Task>();
            foreach (var host in list)
            {
                var worker = (IRunner)host.Services.GetService(typeof(IRunner));
                taskSet.Add(worker.CloseEditor());
            }
            await Task.WhenAll(taskSet).ConfigureAwait(false);
        }

        private async void Button_Click_Stop(object sender, RoutedEventArgs e)
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
    }

}

