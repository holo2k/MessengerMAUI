using Messenger.Contracts.Music;
using Messenger.Maui.Services;

namespace Messenger.Maui.Features.Music;

public sealed class MusicTrackActions(IMediaUploader uploader, IConversationApi conversations, IMusicApi api)
{
    public async Task ShowAsync(Page page, MusicViewModel music, MusicTrackResponse track)
    {
        var saved = music.Library.Any(item => item.Id == track.Id);
        var libraryAction = saved ? "Убрать из моей музыки" : "Добавить в мою музыку";
        var actions = new List<string> { "Отправить в чат", libraryAction, "Скачать трек" };
        if (music.CanEditCover(track)) actions.Add("Изменить обложку");
        var action = await page.DisplayActionSheetAsync(track.Title, "Отмена", null, actions.ToArray());
        switch (action)
        {
            case "Отправить в чат":
                await page.Navigation.PushModalAsync(new MusicSharePage(track, conversations, api), animated: false);
                break;
            case "Скачать трек": await music.DownloadAsync(track); break;
            case "Изменить обложку":
                var cover = await MusicCoverPicker.PickAsync();
                if (cover is null) return;
                var stored = await uploader.UploadAsync(cover);
                await music.UpdateCoverAsync(track, stored.Id);
                break;
            default:
                if (action == libraryAction)
                {
                    if (saved) await music.RemoveAsync(track);
                    else await music.AddAsync(track);
                }
                break;
        }
    }
}
