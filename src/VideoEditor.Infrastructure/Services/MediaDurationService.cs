using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Enums;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
/// 	Читает длительность аудио- и видеофайлов.
/// </summary>
public sealed class MediaDurationService : IMediaDurationService
{
    /// <inheritdoc />
    public TimeSpan GetDuration(string filePath, MediaType mediaType)
    {
        if (!File.Exists(filePath) || mediaType == MediaType.Image)
            return TimeSpan.Zero;

        var completed = new AutoResetEvent(false);
        var duration = TimeSpan.Zero;

        var thread = new Thread(() =>
        {
            var player = new MediaPlayer();

            player.MediaOpened += (_, _) =>
            {
                if (player.NaturalDuration.HasTimeSpan)
                    duration = player.NaturalDuration.TimeSpan;

                player.Close();
                completed.Set();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };

            player.MediaFailed += (_, _) =>
            {
                player.Close();
                completed.Set();
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            };

            player.Open(new Uri(filePath, UriKind.Absolute));
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!completed.WaitOne(TimeSpan.FromSeconds(5)))
            return TimeSpan.Zero;

        thread.Join(TimeSpan.FromSeconds(1));
        return duration;
    }
}
