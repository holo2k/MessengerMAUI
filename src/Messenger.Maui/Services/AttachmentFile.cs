using Messenger.Contracts.Media;

namespace Messenger.Maui.Services;

public static class AttachmentFile
{
    public static async Task<string> DownloadAsync(StoredObjectResponse attachment, IMessageAttachmentClient client, CancellationToken ct = default, byte[]? bytes = null)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "message-attachments", attachment.Id.ToString("N"));
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(attachment.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name)) name = "attachment";
        var path = Path.Combine(folder, name);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var file = File.Create(pending))
            {
                if (bytes is not null) await file.WriteAsync(bytes, ct);
                else await client.DownloadAsync(attachment.Id, file, ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(pending, path, overwrite: true);
            return path;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static async Task OpenAsync(StoredObjectResponse attachment, IMessageAttachmentClient client, byte[]? bytes = null)
    {
        var path = await DownloadAsync(attachment, client, bytes: bytes);
        await Launcher.Default.OpenAsync(new OpenFileRequest(attachment.FileName, new ReadOnlyFile(path, attachment.ContentType)));
    }
}
