using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Models;
using DynamicIsland.Services;

namespace DynamicIsland.ViewModels;

public sealed partial class TranslatorViewModel : ObservableObject
{
    private readonly ITranslationService _translator;
    private readonly IClipboardService _clipboard;
    private readonly DispatcherTimer _debounce;

    private int _requestId;
    private string? _lastInput;
    private string? _lastSource;
    private string? _lastTarget;

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private string _sourceLanguage = "ru";

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public TranslatorViewModel(ITranslationService translator, IClipboardService clipboard)
    {
        _translator = translator;
        _clipboard = clipboard;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = TranslateCoreAsync(force: false);
        };
    }

    public ObservableCollection<LanguageOption> Languages { get; } = new()
    {
        new LanguageOption("ru", "RU"),
        new LanguageOption("en", "EN"),
        new LanguageOption("de", "DE"),
        new LanguageOption("fr", "FR"),
        new LanguageOption("es", "ES"),
        new LanguageOption("auto", "AUTO"),
    };

    [RelayCommand]
    private void SwapLanguages()
    {
        (SourceLanguage, TargetLanguage) = (TargetLanguage, SourceLanguage);
        (Input, Output) = (Output, Input);
    }

    [RelayCommand]
    private Task TranslateAsync() => TranslateCoreAsync(force: true);

    [RelayCommand]
    private async Task PasteFromClipboardAsync()
    {
        if (_clipboard.Current.HasText)
        {
            Input = _clipboard.Current.Text!;
            await TranslateCoreAsync(force: true);
        }
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (!string.IsNullOrEmpty(Output))
        {
            _clipboard.SetText(Output);
        }
    }

    partial void OnInputChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _debounce.Stop();
            _lastInput = null;
            Output = string.Empty;
            Error = null;
            return;
        }

        RestartDebounce();
    }

    partial void OnSourceLanguageChanged(string value) => RestartDebounce();

    partial void OnTargetLanguageChanged(string value) => RestartDebounce();

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task TranslateCoreAsync(bool force)
    {
        var text = (Input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            Output = string.Empty;
            return;
        }

        if (!force
            && text == _lastInput
            && SourceLanguage == _lastSource
            && TargetLanguage == _lastTarget)
        {
            return;
        }

        var requestId = ++_requestId;
        IsBusy = true;
        Error = null;
        Trace($"start '{text}' {SourceLanguage}->{TargetLanguage} force={force}");

        try
        {
            var result = await _translator.TranslateAsync(text, TargetLanguage, SourceLanguage);
            if (requestId != _requestId)
            {
                Trace($"stale result for '{text}'");
                return;
            }

            Output = result.TranslatedText;
            _lastInput = text;
            _lastSource = SourceLanguage;
            _lastTarget = TargetLanguage;
            Trace($"ok '{result.TranslatedText}'");
        }
        catch (Exception ex)
        {
            if (requestId == _requestId)
            {
                Error = $"Ошибка перевода: {ex.Message}";
            }

            Trace($"error {ex}");
        }
        finally
        {
            if (requestId == _requestId)
            {
                IsBusy = false;
            }
        }
    }

    private static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("DI_TRACE") != "1")
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "di_translator.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
        }
    }
}
