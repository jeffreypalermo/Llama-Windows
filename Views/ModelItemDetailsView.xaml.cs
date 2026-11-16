using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace LlamaApp.Views;

/// <summary>
/// The model details panel. Presentation only: every action forwards to
/// <see cref="ModelItemDetailsViewModel"/>, which owns the interaction state
/// and orchestrates the shell's workflows. Hosted inside the main flyout's
/// models card (MainWindow swaps it with the list) — no window or frame
/// navigation of its own.
/// </summary>
public sealed partial class ModelItemDetailsView : UserControl
{
    /// <summary>Raised by the back row; the shell swaps the list back in.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The details ViewModel currently bound; null until the first show.</summary>
    public ModelItemDetailsViewModel? ViewModel { get; private set; }

    public ModelItemDetailsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Binds a new ViewModel and refreshes the compiled bindings (the view
    /// instance is reused across shows; the ViewModel is created per show).
    /// </summary>
    public void SetViewModel(ModelItemDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    /// <summary>Moves keyboard focus into the view when it opens (best effort).</summary>
    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    /// <summary>
    /// Writes a pending arguments edit. Called when the box loses focus and
    /// again by the shell before the view is dropped, so either path persists
    /// a valid value and neither persists a rejected one.
    /// </summary>
    public void CommitPendingArguments()
        => ViewModel?.CommitServeArguments(ServeArgumentsBox.Text);

    private void ServeArguments_LostFocus(object sender, RoutedEventArgs e)
        => CommitPendingArguments();

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingArguments();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ContextOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ContextLengthOption option })
            ViewModel?.SelectContextLength(option);
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.ChatAsync();
    }

    private void CopyModelId_Click(object sender, RoutedEventArgs e)
        => ViewModel?.CopyModelId();

    private void BuildApiRequest_Click(object sender, RoutedEventArgs e)
        => ViewModel?.BuildApiRequest();

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
        => ViewModel?.OpenRepository();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.DownloadAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.DeleteAsync();
    }
}
