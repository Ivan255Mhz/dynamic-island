using System.Net.Http;
using System.Text;
using System.Text.Json;
using DynamicIsland.Models;

namespace DynamicIsland.Services;

/// <summary>
/// Free, key-less translation backed by Google's public endpoints.
/// Tries the primary endpoint first and transparently falls back to the
/// chrome-extension endpoint when the primary is throttled (HTTP 429).
/// Auto-detects the source language and translates into the requested target.
/// </summary>
public sealed class GoogleTranslationService : ITranslationService
{
    private const string PrimaryEndpoint = "https://translate.googleapis.com/translate_a/single";
    private const string FallbackEndpoint = "https://clients5.google.com/translate_a/t";

    private readonly HttpClient _http;

    public GoogleTranslationService(HttpClient http)
    {
        _http = http;
        if (_http.Timeout == Timeout.InfiniteTimeSpan || _http.Timeout > TimeSpan.FromSeconds(15))
        {
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(text, string.Empty, "auto", targetLanguage);
        }

        var source = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        var fallback = BuildUrl(FallbackEndpoint, targetLanguage, source, text, "client=dict-chrome-ex");
        try
        {
            return await RequestAsync(
                    BuildUrl(PrimaryEndpoint, targetLanguage, source, text, "client=gtx&dt=t"),
                    text,
                    targetLanguage,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return await RequestAsync(fallback, text, targetLanguage, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TranslationResult> RequestAsync(
        string url,
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Parse(payload, source, target);
    }

    private static string BuildUrl(string endpoint, string target, string source, string text, string query)
    {
        var url = new StringBuilder(endpoint)
            .Append('?').Append(query)
            .Append("&sl=").Append(Uri.EscapeDataString(source))
            .Append("&tl=").Append(Uri.EscapeDataString(target))
            .Append("&q=").Append(Uri.EscapeDataString(text))
            .ToString();
        return url;
    }

    private static TranslationResult Parse(string payload, string source, string target)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var translated = new StringBuilder();
        var detected = string.IsNullOrWhiteSpace(source) || source == "auto" ? "auto" : source;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var first = root[0];

            if (first.ValueKind == JsonValueKind.String)
            {
                // clients5 flat format (explicit source language): ["translated", ...]
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var part = item.GetString();
                        if (!string.IsNullOrEmpty(part))
                        {
                            if (translated.Length > 0)
                            {
                                translated.Append(' ');
                            }

                            translated.Append(part);
                        }
                    }
                }
            }
            else if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0)
            {
                if (first[0].ValueKind == JsonValueKind.String)
                {
                    // clients5 nested format: [["translated","detectedLang"]]
                    translated.Append(first[0].GetString());
                    if (first.GetArrayLength() > 1 && first[1].ValueKind == JsonValueKind.String)
                    {
                        detected = first[1].GetString() ?? detected;
                    }
                }
                else
                {
                    // translate_a/single format: [[[text, ...], ...], ..., "detected"]
                    foreach (var segment in first.EnumerateArray())
                    {
                        if (segment.ValueKind == JsonValueKind.Array &&
                            segment.GetArrayLength() > 0 &&
                            segment[0].ValueKind == JsonValueKind.String)
                        {
                            translated.Append(segment[0].GetString());
                        }
                    }

                    if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                    {
                        detected = root[2].GetString() ?? detected;
                    }
                }
            }
        }

        var text = translated.ToString().Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Переводчик вернул пустой ответ.");
        }

        return new TranslationResult(source, text, detected, target);
    }
}
