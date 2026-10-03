namespace DynamicIsland.Models;

public sealed record TranslationResult(
    string SourceText,
    string TranslatedText,
    string DetectedSourceLanguage,
    string TargetLanguage);
