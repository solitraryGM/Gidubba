using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using TableForge.Domain;

namespace TableForge.Views;

/// <summary>
/// Shows result text with its bold/italic on a TextBlock: <c>views:FormattedText.Segments="{Binding Segments}"</c> in place of
/// <c>Text="{Binding Text}"</c>. Text with no formatting is set as plain <see cref="TextBlock.Text"/>, so a plain table costs and
/// looks the same as it always has. With formatting, <see cref="TextBlock.Text"/> is still set to the whole plain text and the
/// Inlines are replaced with one Run per segment.
/// </summary>
public static class FormattedText
{
    public static readonly AttachedProperty<IReadOnlyList<FormattedSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<FormattedSegment>?>("Segments", typeof(FormattedText));

    /// <summary>Whether the TextBlock currently holds formatted Runs rather than its plain Text.</summary>
    private static readonly AttachedProperty<bool> HasRunsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("HasRuns", typeof(FormattedText));

    static FormattedText()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>((block, _) => Apply(block));
    }

    public static IReadOnlyList<FormattedSegment>? GetSegments(TextBlock element) => element.GetValue(SegmentsProperty);

    public static void SetSegments(TextBlock element, IReadOnlyList<FormattedSegment>? value) => element.SetValue(SegmentsProperty, value);

    private static void Apply(TextBlock block)
    {
        var segments = block.GetValue(SegmentsProperty) ?? [];
        var text = string.Concat(segments.Select(s => s.Text));

        if (segments.All(s => s.Style == TextStyle.None))
        {
            // Runs left over from earlier formatting are cleared first, then the plain text is set.
            if (block.GetValue(HasRunsProperty))
            {
                block.Inlines?.Clear();
                block.SetValue(HasRunsProperty, false);
            }
            block.Text = text;
            return;
        }

        block.Text = text;
        block.Inlines ??= new InlineCollection();
        block.Inlines.Clear();
        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsBold) run.FontWeight = FontWeight.Bold;
            if (segment.IsItalic) run.FontStyle = FontStyle.Italic;
            block.Inlines.Add(run);
        }
        block.SetValue(HasRunsProperty, true);
    }
}
