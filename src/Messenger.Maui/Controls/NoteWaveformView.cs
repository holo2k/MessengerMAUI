namespace Messenger.Maui.Controls;

public sealed class NoteWaveformView : GraphicsView, IDrawable
{
    public IReadOnlyList<byte> Samples { get; set; } = [];
    public double Progress { get; set; }
    public bool Ring { get; set; }
    public NoteWaveformView() { Drawable = this; HeightRequest = 32; }
    public void Draw(ICanvas canvas, RectF rect)
    {
        var progress = Math.Clamp(Progress, 0, 1);
        if (Ring)
        {
            canvas.StrokeSize = 3;
            canvas.StrokeColor = Color.FromArgb("#77999999");
            canvas.DrawEllipse(3, 3, rect.Width - 6, rect.Height - 6);
            canvas.StrokeColor = Color.FromArgb("#0088FF");
            if (progress > 0) canvas.DrawArc(3, 3, rect.Width - 6, rect.Height - 6, 90, (float)(90 - progress * 360), true, false);
            return;
        }
        var count = Samples.Count == 0 ? 40 : Samples.Count;
        var step = rect.Width / count;
        for (var i = 0; i < count; i++)
        {
            var height = Math.Max(3, (Samples.Count == 0 ? 0 : Samples[i]) / 100f * (rect.Height - 4));
            canvas.FillColor = Color.FromArgb(i < count * progress ? "#0088FF" : "#A5C7E6");
            canvas.FillRoundedRectangle(i * step, (rect.Height - height) / 2, Math.Max(1, step - 2), height, 1.5f);
        }
    }
}
