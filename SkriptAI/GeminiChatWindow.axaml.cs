using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Layout;
using System.Text.Json;
using Avalonia;
using SkEditor.API;
using SkEditor.Utilities.Files;
using AvaloniaEdit;

namespace SkriptAI;

public partial class GeminiChatWindow : Window
{
    private readonly GeminiChatAddon _addon;

    public GeminiChatWindow(GeminiChatAddon addon)
    {
        InitializeComponent();
        _addon = addon;
        SendButton.Click += OnSendClicked;
        ExplainButton.Click += OnExplainClicked;
        InsertButton.Click += OnInsertClicked;
        RefactorButton.Click += OnRefactorClicked;
    }

    private OpenedFile? GetCurrentFile() => SkEditorAPI.Files.GetCurrentOpenedFile();
    private TextEditor? GetEditor() => GetCurrentFile()?.Editor;
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

    private async void OnSendClicked(object? sender, RoutedEventArgs e)
    {
        var userMessage = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(userMessage)) return;
        AddMessageToHistory(userMessage, isUser: true);
        InputTextBox.Text = string.Empty;
        AddMessageToHistory("...", isUser: false);
        var replyJson = await _addon.AskGeminiAsync(userMessage);
        var reply = ExtractGeminiReply(replyJson);
        if (ChatHistoryPanel.Children.Count > 0)
            ChatHistoryPanel.Children.RemoveAt(ChatHistoryPanel.Children.Count - 1);
        AddMessageToHistory(reply, isUser: false);
    }

    private async void OnExplainClicked(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedText();
        if (string.IsNullOrWhiteSpace(selected))
        {
            AddMessageToHistory("No code selected.", isUser: false);
            return;
        }
        AddMessageToHistory(selected, isUser: true);
        AddMessageToHistory("...", isUser: false);
        var replyJson = await _addon.AskGeminiAsync($"Explain this Skript code:\n{selected}");
        var reply = ExtractGeminiReply(replyJson);
        if (ChatHistoryPanel.Children.Count > 0)
            ChatHistoryPanel.Children.RemoveAt(ChatHistoryPanel.Children.Count - 1);
        AddMessageToHistory(reply, isUser: false);
    }

    private async void OnInsertClicked(object? sender, RoutedEventArgs e)
    {
        var prompt = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(prompt)) return;
        AddMessageToHistory(prompt, isUser: true);
        AddMessageToHistory("...", isUser: false);
        var replyJson = await _addon.AskGeminiAsync($"Write Skript code for: {prompt}");
        var reply = ExtractGeminiReply(replyJson);
        if (ChatHistoryPanel.Children.Count > 0)
            ChatHistoryPanel.Children.RemoveAt(ChatHistoryPanel.Children.Count - 1);
        AddMessageToHistory(reply, isUser: false);
        InsertAtCaret(reply);
    }

    private async void OnRefactorClicked(object? sender, RoutedEventArgs e)
    {
        var selected = GetSelectedText();
        if (string.IsNullOrWhiteSpace(selected))
        {
            AddMessageToHistory("No code selected.", isUser: false);
            return;
        }
        AddMessageToHistory(selected, isUser: true);
        AddMessageToHistory("...", isUser: false);
        var replyJson = await _addon.AskGeminiAsync($"Refactor this Skript code:\n{selected}");
        var reply = ExtractGeminiReply(replyJson);
        if (ChatHistoryPanel.Children.Count > 0)
            ChatHistoryPanel.Children.RemoveAt(ChatHistoryPanel.Children.Count - 1);
        AddMessageToHistory(reply, isUser: false);
        ReplaceSelectedText(reply);
    }

    private void AddMessageToHistory(string message, bool isUser = false)
    {
        var bubble = new Border
        {
            Background = isUser ? new SolidColorBrush(Color.Parse("#60cdff")) : new SolidColorBrush(Color.Parse("#23242B")),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            MaxWidth = 340,
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 0),
            Child = new TextBlock
            {
                Text = message,
                Foreground = isUser ? Brushes.Black : Brushes.White,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            }
        };
        ChatHistoryPanel.Children.Add(bubble);
    }

    private string ExtractGeminiReply(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
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
} 