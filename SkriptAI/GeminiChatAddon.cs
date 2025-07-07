using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using SkEditor.API;
using SkEditor.API.Settings;
using SkEditor.API.Settings.Types;
using SkEditor.Utilities;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using FluentAvalonia.UI.Controls;
using SymbolIconSource = FluentAvalonia.UI.Controls.SymbolIconSource;
using Symbol = FluentAvalonia.UI.Controls.Symbol;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using System.Text.RegularExpressions;

namespace SkriptAI;

public class GeminiChatAddon : IAddon
{
    public string Name => "Gemini AI Chat";
    public string Description => "Chat with Google Gemini AI inside SkEditor.";
    public string Identifier => "GeminiChatAddon";
    public string Version => "1.0.0";
    public Version GetMinimalSkEditorVersion() => new Version(2, 9, 0);

    private string _apiKey = "";
    private Window? _mainWindow;

    private const string SystemPrompt = @"Act like an expert Minecraft server developer and Skript specialist. You are fully integrated into the SkEditor IDE, which is purpose-built for writing, debugging, and enhancing scripts written in the Skript language and its various addons (such as SkQuery, SkBee, SkRayFall, skript-reflect, etc.).

Your objective is to assist the user in real time as they create or modify scripts in SkEditor. You must be deeply knowledgeable about:
- Minecraft mechanics and commands
- The Skript language syntax and structure
- Common Skript events, conditions, effects, and expressions
- Addon-specific syntax and use cases
- Debugging and optimizing scripts for performance on multiplayer servers

Step-by-step, perform the following:
1. Analyze the user's current script or request, and identify what it aims to do within Minecraft.
2. Provide accurate, version-compatible Skript code that aligns with the user's request. Default to 1.19+ compatible syntax unless stated otherwise.
3. If multiple addons can be used to achieve a result, recommend the cleanest or most efficient option, and explain why.
4. Always use correct Skript syntax, indentation, and spacing. Present final code in a clean block with comments explaining each section's function.
5. Warn the user of potential performance issues or event loop risks (e.g. heavy `on damage` or `on move` event misuse).
6. If errors are likely, help the user troubleshoot by pointing to common mistakes or missing dependencies.
7. Offer suggestions for improving user experience, automation, or gameplay logic using Skript or addon features.
8. Provide addon-specific syntax only if that addon is explicitly mentioned or inferred from context. If uncertain, ask the user to clarify.
9. If a script spans many lines or stages, break it into parts and explain each sequentially, ensuring readability and logic flow.
10. Do not invent unsupported features. Always ensure your suggestions are realistic within the Minecraft + Skript + addons ecosystem.

Take a deep breath and work on this problem step-by-step.";

    public void OnLoad()
    {
        Registries.SidebarPanels.Register(new RegistryKey(this, "GeminiPanel"), new GeminiSidebarPanel(this));
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = desktop.MainWindow;
            if (_mainWindow != null)
            {
                // previous handler if any to avoid duplicates
                _mainWindow.KeyDown -= MainWindow_KeyDown;
                _mainWindow.KeyDown += MainWindow_KeyDown;
            }
        }
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        var isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (isCtrl && isShift && e.Key == Key.X)
        {
            ShowChatWindow();
            e.Handled = true;
        }
    }

    public void OnUnload()
    {
        // Cleanup if needed
    }

    public void ShowSettings()
    {
        var window = new GeminiSettingsWindow(_apiKey);
        window.Show();
        if (!string.IsNullOrWhiteSpace(window.ApiKey))
        {
            _apiKey = window.ApiKey;
        }
    }

    public void ShowChatWindow()
    {
        var window = new GeminiChatWindow(this);
        window.Show();
    }

    private async Task<string?> FetchSkriptDocAsync(string syntax)
    {
        try
        {
            var url = $"https://docs.skunity.com/syntax/{Uri.EscapeDataString(syntax)}";
            using var client = new HttpClient();
            var html = await client.GetStringAsync(url);
            var mainMatch = Regex.Match(html, "<main[^>]*>([\\s\\S]*?)</main>", RegexOptions.IgnoreCase);
            string mainHtml = mainMatch.Success ? mainMatch.Groups[1].Value : html;
            string text = Regex.Replace(mainHtml, "<[^>]+>", "");
            text = Regex.Replace(text, "\\s+", " ").Trim();
            if (text.Length > 1000) text = text.Substring(0, 1000) + "...";
            return text;
        }
        catch { return null; }
    }

    public async Task<string> AskGeminiAsync(string prompt)
    {
        var skriptTerms = prompt.Split(' ', '\n', '\t', '.', ':', '(', ')', '{', '}', '[', ']', ',', ';')
            .Where(w => w.Length > 2 && w.All(char.IsLower))
            .Distinct()
            .Take(2)
            .ToList();
        string docContext = "";
        foreach (var term in skriptTerms)
        {
            var doc = await FetchSkriptDocAsync(term);
            if (!string.IsNullOrWhiteSpace(doc))
            {
                docContext += $"Documentation for '{term}':\n{doc}\n\n";
            }
        }
        string fullPrompt = string.IsNullOrWhiteSpace(docContext)
            ? SystemPrompt + "\n" + prompt
            : SystemPrompt + "\nRelevant Skript documentation:\n" + docContext + "---\n" + prompt;
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "Error: API key is not set.";
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-goog-api-key", _apiKey);
            var response = await client.PostAsJsonAsync(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent",
                new
                {
                    contents = new[] { new { parts = new[] { new { text = fullPrompt } } } }
                });
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return $"Error: {response.StatusCode} - {json}";
            return json ?? string.Empty;
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public void OnEnable()
    {
        OnLoad();
    }

    public List<Setting> GetSettings()
    {
        var apiKeySetting = new Setting(
            this,
            "Gemini API Key",
            "GeminiApiKey",
            _apiKey,
            new TextSetting("Enter your Gemini API key here."),
            "Enter your Gemini API key for Gemini AI Chat."
        );
        apiKeySetting.OnChanged = value =>
        {
            _apiKey = value as string ?? "";
        };
        return new List<Setting> { apiKeySetting };
    }
}

// for sidebar embedding
public class GeminiSidebarChatPanel : UserControl
{
    private readonly GeminiChatAddon _addon;
    private readonly StackPanel _chatHistoryPanel;
    private readonly TextBox _inputTextBox;
    private readonly Button _sendButton;

    public GeminiSidebarChatPanel(GeminiChatAddon addon)
    {
        _addon = addon;
        var dock = new DockPanel();
        _chatHistoryPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 8), Spacing = 12 };
        var scroll = new ScrollViewer { Content = _chatHistoryPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(scroll, Dock.Top);

        // Input area
        var inputGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Height = 40
        };
        _inputTextBox = new TextBox {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 15,
            Watermark = "Ask Gemini anything...",
            Padding = new Thickness(12, 10, 0, 10),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            MaxLines = 1,
            AcceptsReturn = false,
            CornerRadius = new CornerRadius(16,0,0,16),
            // Remove right border visually

        };
        _inputTextBox.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                OnSendClicked(sender, e);
            }
        };
        _sendButton = new Button {
            Content = new FluentAvalonia.UI.Controls.SymbolIcon { Symbol = FluentAvalonia.UI.Controls.Symbol.Send },
            Width = 40,
            Height = 40,
            Background = new SolidColorBrush(Color.Parse("#007AFF")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0,16,16,0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            // No margin
        };
        _sendButton.Click += OnSendClicked;
        inputGrid.Children.Add(_inputTextBox);
        inputGrid.Children.Add(_sendButton);
        Grid.SetColumn(_inputTextBox, 0);
        Grid.SetColumn(_sendButton, 1);
        var inputBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#23242B")),
            BorderBrush = new SolidColorBrush(Color.Parse("#35363C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(0),
            Margin = new Thickness(16, 0, 16, 16),
            Child = inputGrid
        };

        // Use a Grid for layout: Row 0 = chat area Row 1 = input area (Auto) help im losing sanity here
        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        rootGrid.Children.Add(scroll);
        rootGrid.Children.Add(inputBorder);
        Grid.SetRow(scroll, 0);
        Grid.SetRow(inputBorder, 1);
        Content = rootGrid;
    }

    private async void OnSendClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var prompt = _inputTextBox.Text;
        if (string.IsNullOrWhiteSpace(prompt)) return;
        var context = GetSelectedText() ?? GetFileText();
        var fullPrompt = !string.IsNullOrWhiteSpace(context) ? $"{prompt}\n\nContext:\n{context}" : prompt;
        AddMessageToHistory(prompt, isUser: true);
        _inputTextBox.Text = string.Empty;
        AddMessageToHistory("...", isUser: false);
        var replyJson = await _addon.AskGeminiAsync(fullPrompt);
        var reply = ExtractGeminiReply(replyJson);
        if (_chatHistoryPanel.Children.Count > 0)
            _chatHistoryPanel.Children.RemoveAt(_chatHistoryPanel.Children.Count - 1);
        AddMessageToHistory(reply, isUser: false);
        // If the prompt is a code action, insert/replace as appropriate
        if (prompt.ToLower().Contains("refactor") && !string.IsNullOrWhiteSpace(GetSelectedText()))
            ReplaceSelectedText(reply);
        else if (prompt.ToLower().Contains("insert") || prompt.ToLower().Contains("generate"))
            InsertAtCaret(reply);
    }

    private void AddMessageToHistory(string message, bool isUser = false)
    {
        // Markdown formatting
        string formatted = message
            .Replace("**", "")
            .Replace("\n", "\n")
            .Replace("* ", "• ");

        var bubble = new Border
        {
            Background = isUser ? new SolidColorBrush(Color.Parse("#60cdff")) : new SolidColorBrush(Color.Parse("#23242B")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 340,
            HorizontalAlignment = isUser ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
            Child = new TextBox
            {
                Text = formatted,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = isUser ? Brushes.Black : Brushes.White,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                CaretBrush = Brushes.Transparent,
                Focusable = false,
                Padding = new Thickness(0),
                ContextMenu = null
            }
        };
        _chatHistoryPanel.Children.Add(bubble);
    }

    private string ExtractGeminiReply(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var candidates = root.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                var parts = content.GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    return text ?? "(No reply)";
                }
            }
            return "(No reply)";
        }
        catch
        {
            return "(Error parsing AI response)";
        }
    }

    private SkEditor.Utilities.Files.OpenedFile? GetCurrentFile() => SkEditorAPI.Files.GetCurrentOpenedFile();
    private AvaloniaEdit.TextEditor? GetEditor() => GetCurrentFile()?.Editor;
    private string? GetSelectedText() => GetEditor()?.SelectedText;
    private void ReplaceSelectedText(string newText)
    {
        var editor = GetEditor();
        if (editor == null) return;
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        if (length > 0)
        {
            editor.Document.Replace(start, length, newText);
        }
    }
    private void InsertAtCaret(string newText)
    {
        var editor = GetEditor();
        if (editor == null) return;
        var offset = editor.CaretOffset;
        editor.Document.Insert(offset, newText);
    }
    private string? GetFileText() => GetEditor()?.Text;
    private void SetFileText(string newText)
    {
        var editor = GetEditor();
        if (editor == null) return;
        editor.Text = newText;
    }
}
public class GeminiSidebarPanel : SidebarPanel
{
    private readonly GeminiSidebarChatPanel _panel;
    public GeminiSidebarPanel(GeminiChatAddon addon)
    {
        _panel = new GeminiSidebarChatPanel(addon);
    }
    public override UserControl Content => _panel;
    public override IconSource Icon => new SymbolIconSource { Symbol = Symbol.Comment };
    public override IconSource IconActive => new SymbolIconSource { Symbol = Symbol.Comment };
    public override bool IsDisabled => false;
    public override int DesiredWidth => 350;
} 