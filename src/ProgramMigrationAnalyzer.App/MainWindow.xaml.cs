using System.ComponentModel;
using System.Windows;
using ProgramMigrationAnalyzer.App.ViewModels;

namespace ProgramMigrationAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _webViewReady;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await UpdateMarkdownPreviewAsync();

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.MarkdownHtml) or nameof(MainViewModel.SelectedDocument))
        {
            await UpdateMarkdownPreviewAsync();
        }
    }

    private async Task UpdateMarkdownPreviewAsync()
    {
        try
        {
            if (!_webViewReady)
            {
                await MarkdownWebView.EnsureCoreWebView2Async();
                _webViewReady = true;
            }

            MarkdownWebView.NavigateToString(_viewModel.MarkdownHtml);
            MarkdownWebView.Visibility = Visibility.Visible;
            MarkdownFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            MarkdownWebView.Visibility = Visibility.Collapsed;
            MarkdownFallback.Visibility = Visibility.Visible;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        MarkdownWebView.Dispose();
    }
}
