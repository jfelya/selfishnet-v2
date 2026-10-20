using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SelfishNetV2.App.Controls;

/// <summary>
/// Text box that accepts digits only (typing and paste). Used by the limit cells;
/// an empty box commits as 0 (unlimited) through the view-model.
/// </summary>
public sealed class NumericTextBox : TextBox
{
    public NumericTextBox()
    {
        PreviewTextInput += OnPreviewTextInput;
        DataObject.AddPastingHandler(this, OnPaste);
    }

    private static bool AllDigits(string? text) =>
        string.IsNullOrEmpty(text) || text.All(char.IsDigit);

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!e.Text.All(char.IsDigit)) e.Handled = true;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }
        if (!AllDigits(e.SourceDataObject.GetData(DataFormats.Text) as string))
            e.CancelCommand();
    }
}
