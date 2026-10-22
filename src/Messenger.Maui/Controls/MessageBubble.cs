using Messenger.Contracts.Messages;
using Microsoft.Maui.Controls.Shapes;
using Messenger.Contracts.Music;
using Messenger.Maui.Features.Music;

namespace Messenger.Maui.Controls;

public sealed class MessageBubble : ContentView
{
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(MessageResponse), typeof(MessageBubble), null, propertyChanged: Refresh);
    public static readonly BindableProperty CurrentUserIdProperty = BindableProperty.Create(
        nameof(CurrentUserId), typeof(Guid), typeof(MessageBubble), Guid.Empty, propertyChanged: Refresh);
    public static readonly BindableProperty ReadSequenceProperty = BindableProperty.Create(
        nameof(ReadSequence), typeof(long), typeof(MessageBubble), 0L, propertyChanged: (view, _, _) => ((MessageBubble)view).UpdateReceipt());
    private Image? _receipt;
    private readonly List<MessageAttachmentView> _attachments = [];
    private bool _updatingMediaSize;
    public static readonly BindableProperty PlayerProperty = BindableProperty.Create(
        nameof(Player), typeof(MusicViewModel), typeof(MessageBubble), null, propertyChanged: Refresh);
    public MusicViewModel? Player { get => (MusicViewModel?)GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public event EventHandler<MusicTrackResponse>? TrackOpened;
    public event EventHandler<MusicTrackResponse>? TrackPlaybackRequested;
    public event EventHandler<MessageResponse>? MessageActionsRequested;
    internal void RequestMessageActions()
    {
        if (Message is { DeletedAt: null } message) MessageActionsRequested?.Invoke(this, message);
    }

    public MessageBubble()
    {
        Padding = new Thickness(12, 3);
        SizeChanged += (_, _) =>
        {
            if (Content is not Border border) return;
            border.MaximumWidthRequest = Math.Max(160, Width * 0.82);
            UpdateMediaSize();
        };
    }

    public MessageResponse? Message { get => (MessageResponse?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public Guid CurrentUserId { get => (Guid)GetValue(CurrentUserIdProperty); set => SetValue(CurrentUserIdProperty, value); }
    public long ReadSequence { get => (long)GetValue(ReadSequenceProperty); set => SetValue(ReadSequenceProperty, value); }

    private static void Refresh(BindableObject bindable, object oldValue, object newValue) => ((MessageBubble)bindable).Render();

    private void Render()
    {
        if (Message is not { } message) return;
        _receipt = null;
        foreach (var attachment in _attachments) attachment.DisplaySizeChanged -= OnAttachmentSizeChanged;
        _attachments.Clear();
        var outgoing = CurrentUserId != Guid.Empty && message.SenderUserId == CurrentUserId;
        var stack = new VerticalStackLayout { Spacing = 3 };
        if (message.IsForwarded && message.DeletedAt is null)
            stack.Add(new Label { Text = "Пересланное сообщение", FontSize = 11, TextColor = Color.FromArgb("#0088FF") });
        if (message.DeletedAt is null && message.MusicTrack is { } track)
        {
            var card = new Grid { ColumnDefinitions = [new(54), new(GridLength.Star), new(40)], ColumnSpacing = 10, MinimumWidthRequest = 230, Padding = new Thickness(0, 4) };
            var artwork = new Grid { WidthRequest = 54, HeightRequest = 54 };
            artwork.Add(new MusicArtworkView { Track = track });
            var openArtwork = new TapGestureRecognizer();
            openArtwork.Tapped += (_, _) => TrackOpened?.Invoke(this, track);
            artwork.GestureRecognizers.Add(openArtwork);
            card.Add(artwork);
            var info = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
            info.Add(new Label { Text = track.Title, FontSize = 15, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation });
            info.Add(new Label { Text = track.Artist, FontSize = 13, TextColor = Color.FromArgb("#929292"), LineBreakMode = LineBreakMode.TailTruncation });
            info.Add(new Label { Text = Services.PlaybackTime.Format(track.DurationMs), FontSize = 11, TextColor = Color.FromArgb("#929292") });
            var open = new TapGestureRecognizer();
            open.Tapped += (_, _) => TrackOpened?.Invoke(this, track);
            info.GestureRecognizers.Add(open);
            card.Add(info, 1);
            var play = new ImageButton { Source = "music_play_dark.png", WidthRequest = 40, HeightRequest = 44, Padding = 10, BackgroundColor = Colors.Transparent };
            SemanticProperties.SetDescription(play, "Воспроизвести или приостановить песню");
            if (Player is not null)
            {
                var binding = new MultiBinding { Converter = new DisplayValueConverter(), ConverterParameter = "messageMusicPlayIcon" };
                binding.Bindings.Add(new Binding(nameof(MusicTrackResponse.Id), source: track));
                binding.Bindings.Add(new Binding("CurrentTrack.Id", source: Player));
                binding.Bindings.Add(new Binding(nameof(MusicViewModel.IsPlaying), source: Player));
                play.SetBinding(ImageButton.SourceProperty, binding);
            }
            play.Clicked += (_, _) => TrackPlaybackRequested?.Invoke(this, track);
            card.Add(play, 2);
            stack.Add(card);
        }
        else if (message.DeletedAt is null && message.Attachments is { Count: > 0 } attachments)
        {
            foreach (var attachment in attachments)
            {
                var view = new MessageAttachmentView { Attachment = attachment };
                view.DisplaySizeChanged += OnAttachmentSizeChanged;
                _attachments.Add(view);
                stack.Add(view);
            }
            if (!string.IsNullOrWhiteSpace(message.Body))
                stack.Add(new Label { Text = message.Body, FontSize = 16, LineBreakMode = LineBreakMode.WordWrap });
        }
        else
            stack.Add(new Label { Text = DisplayValueConverter.MessageText(message), FontSize = 16, LineBreakMode = LineBreakMode.WordWrap });
        var metadata = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 4 };
        metadata.Add(new Label
        {
            Text = $"{(message.IsPinned ? "Закреплено · " : "")}{(message.EditedAt.HasValue ? "изменено · " : "")}{message.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}",
            FontSize = 10, TextColor = Color.FromArgb("#929292"), HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center, MaxLines = 2
        });
        if (outgoing && message.DeletedAt is null)
        {
            var read = message.Sequence <= ReadSequence;
            var receipt = new Image
            {
                Source = read ? "message_read.png" : "message_sent.png",
                WidthRequest = 18, HeightRequest = 14, VerticalOptions = LayoutOptions.Center
            };
            SemanticProperties.SetDescription(receipt, read ? "Прочитано" : "Отправлено");
            metadata.Add(receipt, 1);
            _receipt = receipt;
        }
        stack.Add(metadata);
        Content = new Border
        {
            Padding = new Thickness(10, 6), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = Color.FromArgb(outgoing ? "#F0EAFF" : "#FFFFFF"),
            HorizontalOptions = outgoing ? LayoutOptions.End : LayoutOptions.Start,
            MaximumWidthRequest = Width > 0 ? Math.Max(160, Width * 0.82) : 300,
            WidthRequest = message.Attachments is { Count: > 0 } ? Math.Min(300, Width > 0 ? Math.Max(160, Width * 0.82) : 300) : -1,
            Content = stack
        };
        UpdateMediaSize();
    }

    private void OnAttachmentSizeChanged(object? sender, EventArgs e) => UpdateMediaSize();

    private void UpdateMediaSize()
    {
        if (_updatingMediaSize || _attachments.Count == 0 || Content is not Border border) return;
        _updatingMediaSize = true;
        try
        {
            var maximum = Math.Min(300, Width > 0 ? Math.Max(160, Width * 0.82) : 300);
            foreach (var attachment in _attachments) attachment.MaxImageWidth = maximum - 20;
            border.WidthRequest = Math.Min(maximum, Math.Max(160, _attachments.Max(attachment => attachment.WidthRequest) + 20));
        }
        finally { _updatingMediaSize = false; }
    }

    private void UpdateReceipt()
    {
        if (_receipt is null || Message is not { } message) return;
        var read = message.Sequence <= ReadSequence;
        _receipt.Source = read ? "message_read.png" : "message_sent.png";
        SemanticProperties.SetDescription(_receipt, read ? "Прочитано" : "Отправлено");
    }
}
