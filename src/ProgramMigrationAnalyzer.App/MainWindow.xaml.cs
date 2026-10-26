using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ProgramMigrationAnalyzer.App.ViewModels;
using ProgramMigrationAnalyzer.App.Services;
using Microsoft.Web.WebView2.Core;

namespace ProgramMigrationAnalyzer.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _webViewReady;
    private bool _webViewUnavailable;
    private Task? _webViewInitialization;
    private bool _disposed;
    private readonly MarkdownPreviewSession _previewSession;
    private Uri? _previewUri;
    private ulong _navigationId;
    private bool _previewFailureReported;
    internal bool IsDisposed => _disposed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _previewSession = new MarkdownPreviewSession(viewModel.OutputDirectory);
        DataContext = viewModel;
        Closed += OnClosed;
        PreviewTabs.SelectionChanged += OnPreviewTabChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        MarkdownWebView.NavigationStarting += OnNavigationStarting;
        MarkdownWebView.NavigationCompleted += OnNavigationCompleted;
    }

    private async void OnPreviewTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, PreviewTabs) && IsMarkdownTabSelected())
        {
            await UpdateMarkdownPreviewAsync();
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (IsMarkdownTabSelected() &&
            eventArgs.PropertyName is (nameof(MainViewModel.MarkdownHtml) or nameof(MainViewModel.SelectedDocument)))
        {
            await UpdateMarkdownPreviewAsync();
        }
    }

    private bool IsMarkdownTabSelected() =>
        Equals((PreviewTabs.SelectedItem as TabItem)?.Header, "Markdown");

    private async Task UpdateMarkdownPreviewAsync()
    {
        if (_disposed || !_viewModel.IsWorkspaceActive) return;
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
                if (_disposed || !_viewModel.IsWorkspaceActive) return;
                // Generated reports are static. Local-file navigation must not grant source text script access.
                MarkdownWebView.CoreWebView2.Settings.IsScriptEnabled = false;
                MarkdownWebView.CoreWebView2.Settings.IsWebMessageEnabled = false;
                MarkdownWebView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                MarkdownWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                _webViewReady = true;
            }

            _previewUri = _previewSession.Write(_viewModel.MarkdownHtml);
            MarkdownWebView.CoreWebView2.Navigate(_previewUri.AbsoluteUri);
            MarkdownWebView.Visibility = Visibility.Visible;
            MarkdownFallback.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            if (_disposed || !_viewModel.IsWorkspaceActive) return;
            ReportPreviewFailure(exception, initializationFailure: !_webViewReady);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_previewUri is not null && e.Uri == _previewUri.AbsoluteUri) _navigationId = e.NavigationId;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_disposed || !_viewModel.IsWorkspaceActive || e.NavigationId != _navigationId ||
            _previewUri is null || MarkdownWebView.CoreWebView2?.Source != _previewUri.AbsoluteUri) return;
        if (!e.IsSuccess)
        {
            ReportPreviewFailure(new InvalidOperationException($"WebView2 navigation failed: {e.WebErrorStatus}"), false);
            return;
        }
        _previewSession.Retain(_previewUri);
        _previewFailureReported = false;
        MarkdownWebView.Visibility = Visibility.Visible;
        MarkdownFallback.Visibility = Visibility.Collapsed;
    }

    private void ReportPreviewFailure(Exception exception, bool initializationFailure)
    {
        ShowMarkdownFallback();
        if (initializationFailure) _webViewUnavailable = true;
        if (_previewFailureReported) return;
        _previewFailureReported = true;
        _viewModel.ReportMarkdownPreviewFailure(exception, initializationFailure);
    }

    private void ShowMarkdownFallback()
    {
        MarkdownWebView.Visibility = Visibility.Collapsed;
        MarkdownFallback.Visibility = Visibility.Visible;
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        PreviewTabs.SelectionChanged -= OnPreviewTabChanged;
        MarkdownWebView.NavigationStarting -= OnNavigationStarting;
        MarkdownWebView.NavigationCompleted -= OnNavigationCompleted;
        MarkdownWebView.Dispose();
        _previewSession.Dispose();
        _viewModel.Dispose();
        DataContext = null;
    }
}
