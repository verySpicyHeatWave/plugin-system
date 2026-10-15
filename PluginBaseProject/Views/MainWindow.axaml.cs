using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PluginBaseProject.Plugins;
using PluginBaseProject.ViewModels;
using SkiaSharp;
using System;
using System.Linq;

namespace PluginBaseProject.Views;

public partial class MainWindow : Window
{
    private readonly PluginLoader _loader = new();
    public MainWindow()
    {
        InitializeComponent();
        _loader.PluginsChanged += OnPluginLoaded;
        _loader.LoadAllPlugins();
    }

    private void OnPluginLoaded()
    {
        var vm = DataContext as MainViewModel;
        vm?.OnPluginsRefreshed(_loader.Plugins);
    }

    private void Window_Drop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        var mpPackages = e.DataTransfer.TryGetFiles()?
            .Select(file => file.TryGetLocalPath())
            .Where(path => path != null && path.EndsWith(".mppk", StringComparison.OrdinalIgnoreCase)).ToList();

        if (mpPackages == null || mpPackages.Count == 0) return;

        var vm = DataContext as MainViewModel;

        foreach (var package in mpPackages)
        {
            if (string.IsNullOrEmpty(package)) continue;
            var result = _loader.InstallNewPlugin(package);
            if (!result.Success) vm?.Text = $"{package} Error: {result.Message}";
        }
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Window_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OnPluginLoaded();
    }
}