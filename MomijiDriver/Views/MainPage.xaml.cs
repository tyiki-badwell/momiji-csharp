using Microsoft.UI.Xaml.Controls;

using MomijiDriver.ViewModels;

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

    private async void Run_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var m = App.MainWindow as MainWindow;
        if (m != default) await m.RunAsync();
    }

    private async void Stop_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var m = App.MainWindow as MainWindow;
        if (m != default) await m.StopAsync();
    }

    private void Open_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var m = App.MainWindow as MainWindow;
        if (m != default) m.Open();
    }

    private async void Close_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var m = App.MainWindow as MainWindow;
        if (m != default) await m.CloseAsync();
    }

}
