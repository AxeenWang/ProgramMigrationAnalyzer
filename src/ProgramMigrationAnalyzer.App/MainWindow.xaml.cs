using System.ComponentModel;
using System.Windows;
using ProgramMigrationAnalyzer.App.ViewModels;

namespace ProgramMigrationAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _webViewReady;
    private bool _webViewUnavailable;
    private Task? _webViewInitialization;

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
        if (_webViewUnavailable)
        {
            ShowMarkdownFallback();
            return;
        }

        try
        {
            if (!_webViewReady)
            {
                _webViewInitialization ??= MarkdownWebView.EnsureCoreWebView2Async();
                await _webViewInitialization;
                _webViewReady = true;
            }

            MarkdownWebView.NavigateToString(_viewModel.MarkdownHtml);
            MarkdownWebView.Visibility = Visibility.Visible;
            MarkdownFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            ShowMarkdownFallback();
            if (!_webViewUnavailable)
            {
                _webViewUnavailable = true;
                _viewModel.ReportMarkdownPreviewFailure(exception);
            }
        }
    }

    private void ShowMarkdownFallback()
    {
        MarkdownWebView.Visibility = Visibility.Collapsed;
        MarkdownFallback.Visibility = Visibility.Visible;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        MarkdownWebView.Dispose();
    }
}
