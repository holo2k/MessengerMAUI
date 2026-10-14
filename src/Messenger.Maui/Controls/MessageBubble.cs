using Messenger.Contracts.Messages;
using Microsoft.Maui.Controls.Shapes;

namespace Messenger.Maui.Controls;

public sealed class MessageBubble : ContentView
{
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(MessageResponse), typeof(MessageBubble), null, propertyChanged: Refresh);
    public static readonly BindableProperty CurrentUserIdProperty = BindableProperty.Create(
        nameof(CurrentUserId), typeof(Guid), typeof(MessageBubble), Guid.Empty, propertyChanged: Refresh);

    public MessageBubble()
    {
        Padding = new Thickness(12, 3);
        SizeChanged += (_, _) => { if (Content is Border border) border.MaximumWidthRequest = Math.Max(160, Width * 0.82); };
    }

    public MessageResponse? Message { get => (MessageResponse?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public Guid CurrentUserId { get => (Guid)GetValue(CurrentUserIdProperty); set => SetValue(CurrentUserIdProperty, value); }

    private static void Refresh(BindableObject bindable, object oldValue, object newValue) => ((MessageBubble)bindable).Render();

    private void Render()
    {
        if (Message is not { } message) return;
        var outgoing = CurrentUserId != Guid.Empty && message.SenderUserId == CurrentUserId;
        var stack = new VerticalStackLayout { Spacing = 3 };
        stack.Add(new Label { Text = DisplayValueConverter.MessageText(message), FontSize = 16, LineBreakMode = LineBreakMode.WordWrap });
        stack.Add(new Label
        {
            Text = $"{(message.IsPinned ? "Закреплено · " : "")}{(message.EditedAt.HasValue ? "изменено · " : "")}{message.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}",
            FontSize = 10, TextColor = Color.FromArgb("#929292"), HorizontalOptions = LayoutOptions.End
        });
        Content = new Border
        {
            Padding = new Thickness(10, 6), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = Color.FromArgb(outgoing ? "#F0EAFF" : "#FFFFFF"),
            HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start,
            MaximumWidthRequest = Width > 0 ? Math.Max(160, Width * 0.82) : 300,
            Content = stack
        };
    }
}
