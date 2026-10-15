using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Contract;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PluginBaseProject.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private MenuItem _base;

    [ObservableProperty]
    public partial string Text { get; set; } = "Pick an option!";

    [ObservableProperty]
    public partial ObservableCollection<MenuItem> PluginItems { get; set; } = new();

    public MainViewModel()
    {
        _base = new MenuItem() { Header = "Base Application", Command = new RelayCommand(() => Text = "This action is built into the base application.") };
        PluginItems.Add(_base);
    }

    public void OnPluginsRefreshed(IEnumerable<IMagicPlugin> plugins)
    {
        PluginItems.Clear();
        PluginItems.Add(_base);
        foreach (var plugin in plugins)
        {
            PluginItems.Add(new MenuItem() { Header = plugin.Name, Command = new RelayCommand(() => Text = plugin.Message) });
        }
    }
}
