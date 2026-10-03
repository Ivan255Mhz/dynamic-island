using System.Windows.Media;

namespace DynamicIsland.Models;

public enum ClipboardContentKind
{
    Text,
    Link,
    Image,
    Empty,
}

public sealed record ClipboardEntry(
    ClipboardContentKind Kind,
    string? Text,
    ImageSource? Image,
    DateTimeOffset CapturedAt,
    int Signature = 0)
{
    public bool IsText => Kind == ClipboardContentKind.Text;
    public bool IsLink => Kind == ClipboardContentKind.Link;
    public bool IsImage => Kind == ClipboardContentKind.Image;
    public bool IsEmpty => Kind == ClipboardContentKind.Empty;

    public bool HasText => !string.IsNullOrEmpty(Text);

    public string Preview => Kind switch
    {
        ClipboardContentKind.Text => BuildTextPreview(Text),
        ClipboardContentKind.Link => Text ?? string.Empty,
        ClipboardContentKind.Image => "Изображение",
        _ => "Пусто",
    };

    private static string BuildTextPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Пусто";
        }

        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length > 120 ? single[..120] + "…" : single;
    }
}
