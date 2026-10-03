using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.ViewModels;

namespace DynamicIsland.Views;

public partial class TranslatorView : UserControl
{
    public TranslatorView()
    {
        InitializeComponent();

        PreviewMouseDown += OnPreviewMouseDown;
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is TranslatorViewModel viewModel && viewModel.TranslateCommand.CanExecute(null))
        {
            viewModel.TranslateCommand.Execute(null);
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsTextInput(source))
        {
            if (Window.GetWindow(this) is MainWindow main)
            {
                main.ActivateForInput();
            }
        }
    }

    private static bool IsTextInput(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is TextBox or ComboBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
