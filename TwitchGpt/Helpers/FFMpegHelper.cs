using System.Drawing;
using FFMpegCore;
using FFMpegCore.Enums;

namespace TwitchGpt.Helpers;

public static class FFMpegHelper
{
    public static async Task<bool> SnapshotAsync(
        Uri input,
        string output,
        Size? size = null,
        TimeSpan? captureTime = null,
        int? streamIndex = null,
        int inputFileIndex = 0,
        Codec? codec = null)
    {
        var analyseResult = await FFProbe.AnalyseAsync(input).ConfigureAwait(false);
        
        var (args, outputOptions) = BuildSnapshotArguments(input, analyseResult, size, captureTime, streamIndex, inputFileIndex, codec);
        
        return await args.OutputToFile(output, true, outputOptions).ProcessAsynchronously();
    }
    
    private static Size? PrepareSnapshotSize(IMediaAnalysis source, Size? wantedSize)
    {
        if (!wantedSize.HasValue || wantedSize.Value.Height <= 0 && wantedSize.Value.Width <= 0 || source.PrimaryVideoStream == null)
            return null;
        Size size = new (source.PrimaryVideoStream.Width, source.PrimaryVideoStream.Height);
        if (source.PrimaryVideoStream.Rotation == 90 || source.PrimaryVideoStream.Rotation == 180)
            size = new (source.PrimaryVideoStream.Height, source.PrimaryVideoStream.Width);
        if (wantedSize.Value.Width == size.Width && wantedSize.Value.Height == size.Height)
            return null;
        if (wantedSize.Value.Width <= 0 && wantedSize.Value.Height > 0)
        {
            double num = (double) wantedSize.Value.Height / size.Height;
            return new ((int) (size.Width * num), (int) (size.Height * num));
        }
        if (wantedSize.Value.Height > 0 || wantedSize.Value.Width <= 0)
            return wantedSize;
        double num1 = (double) wantedSize.Value.Width / (double) size.Width;
        return new ((int) (size.Width * num1), (int) (size.Height * num1));
    }

    private static (FFMpegArguments, Action<FFMpegArgumentOptions>) BuildSnapshotArguments(
        Uri input,
        IMediaAnalysis source,
        Size? size = null,
        TimeSpan? captureTime = null,
        int? streamIndex = null,
        int inputFileIndex = 0,
        Codec? codec = null)
    {
        if (!captureTime.HasValue)
            captureTime = TimeSpan.FromSeconds(source.Duration.TotalSeconds / 3.0);
        size = PrepareSnapshotSize(source, size);
        if (!streamIndex.HasValue)
        {
            VideoStream primaryVideoStream = source.PrimaryVideoStream;
            int index;
            if (primaryVideoStream == null)
            {
                var videoStream = source.VideoStreams.FirstOrDefault();
                index = videoStream?.Index ?? 0;
            }
            else
                index = primaryVideoStream.Index;

            streamIndex = index;
        }

        return new(
            FFMpegArguments.FromUrlInput(input, (Action<FFMpegArgumentOptions>)(options => options.Seek(captureTime))),
            (options => options.SelectStream(streamIndex.Value, inputFileIndex).WithVideoCodec(codec ?? VideoCodec.Png)
                .WithFrameOutputCount(1).Resize(size)));
    }
}