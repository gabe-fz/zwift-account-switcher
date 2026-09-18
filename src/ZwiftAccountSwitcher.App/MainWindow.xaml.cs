using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using ZwiftAccountSwitcher.App.ViewModels;

namespace ZwiftAccountSwitcher.App;

public partial class MainWindow : Window
{
    private bool initialized;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.DeleteRequested += OnDeleteRequested;
    }

    public MainViewModel ViewModel { get; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await ViewModel.InitializeAsync();
    }

    private async void SaveEditor_Click(object sender, RoutedEventArgs e)
    {
        string? transientText = EditorPasswordBox.Password;
        var password = transientText.ToCharArray();
        EditorPasswordBox.Clear();
        transientText = null;
        try
        {
            await ViewModel.SaveEditorAsync(password);
        }
        finally
        {
            Array.Clear(password);
        }
    }

    private void CancelEditor_Click(object sender, RoutedEventArgs e)
    {
        EditorPasswordBox.Clear();
        if (ViewModel.CancelEditorCommand.CanExecute(null))
        {
            ViewModel.CancelEditorCommand.Execute(null);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Zwift Launcher",
            Filter = "Zwift Launcher (ZwiftLauncher.exe)|ZwiftLauncher.exe",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.SetLauncherPathAsync(dialog.FileName);
        }
    }

    private async void OnDeleteRequested(AccountCardViewModel account)
    {
        var answer = MessageBox.Show(
            this,
            "Remove this account and its saved Windows credential?",
            "Delete account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            await ViewModel.DeleteAsync(account);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;
        if (ViewModel.CancelLoginCommand.CanExecute(null))
        {
            ViewModel.CancelLoginCommand.Execute(null);
        }

        MessageBox.Show(
            this,
            "Cancellation was requested. Wait for the current safe step to finish, then close the switcher.",
            "Login in progress",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
