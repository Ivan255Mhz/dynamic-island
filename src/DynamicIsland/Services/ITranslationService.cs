using DynamicIsland.Models;

namespace DynamicIsland.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default);
}
