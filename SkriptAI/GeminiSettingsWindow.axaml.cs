using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkriptAI;

public partial class GeminiSettingsWindow : Window
{
    public string ApiKey { get; private set; } = string.Empty;

    public GeminiSettingsWindow(string currentKey = "")
    {
        InitializeComponent();
        ApiKeyTextBox.Text = currentKey;
        SaveButton.Click += OnSaveClicked;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        ApiKey = ApiKeyTextBox.Text;
        Close();
    }
} 