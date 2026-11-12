using Avalonia.Controls;

namespace TableForge;

/// <summary>WPF-style <c>TextBox.Select(start, length-end)</c> helper: Avalonia exposes SelectionStart/SelectionEnd instead.</summary>
internal static class TextBoxExtensions
{
    /// <summary>Selects [start, end) (an empty range just places the caret).</summary>
    public static void Select(this TextBox box, int start, int end)
    {
        var len = box.Text?.Length ?? 0;
        start = System.Math.Clamp(start, 0, len);
        end = System.Math.Clamp(end, 0, len);
        box.SelectionStart = start;
        box.SelectionEnd = end;
        box.CaretIndex = end;
    }
}
