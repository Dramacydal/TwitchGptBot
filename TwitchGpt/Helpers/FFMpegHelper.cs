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
        (FFMpegArguments, Action<FFMpegArgumentOptions> outputOptions) tuple = BuildSnapshotArguments(input, await FFProbe.AnalyseAsync(input).ConfigureAwait(false), size, captureTime, streamIndex, inputFileIndex, codec);
        
        return await tuple.Item1.OutputToFile(output, true, tuple.outputOptions).ProcessAsynchronously();
    }
    
    private static Size? PrepareSnapshotSize(IMediaAnalysis source, Size? wantedSize)
    {
        if (!wantedSize.HasValue || wantedSize.Value.Height <= 0 && wantedSize.Value.Width <= 0 || source.PrimaryVideoStream == null)
            return new Size?();
        Size size = new Size(source.PrimaryVideoStream.Width, source.PrimaryVideoStream.Height);
        if (source.PrimaryVideoStream.Rotation == 90 || source.PrimaryVideoStream.Rotation == 180)
            size = new Size(source.PrimaryVideoStream.Height, source.PrimaryVideoStream.Width);
        if (wantedSize.Value.Width == size.Width && wantedSize.Value.Height == size.Height)
            return new Size?();
        if (wantedSize.Value.Width <= 0 && wantedSize.Value.Height > 0)
        {
            double num = (double) wantedSize.Value.Height / (double) size.Height;
            return new Size?(new Size((int) ((double) size.Width * num), (int) ((double) size.Height * num)));
        }
        if (wantedSize.Value.Height > 0 || wantedSize.Value.Width <= 0)
            return wantedSize;
        double num1 = (double) wantedSize.Value.Width / (double) size.Width;
        return new Size?(new Size((int) ((double) size.Width * num1), (int) ((double) size.Height * num1)));
    }
    
    private static (FFMpegArguments, Action<FFMpegArgumentOptions> outputOptions) BuildSnapshotArguments(
        Uri input,
        IMediaAnalysis source,
        Size? size = null,
        TimeSpan? captureTime = null,
        int? streamIndex = null,
        int inputFileIndex = 0,
        Codec? codec = null)
    {
        captureTime.GetValueOrDefault();
        if (!captureTime.HasValue)
            captureTime = new TimeSpan?(TimeSpan.FromSeconds(source.Duration.TotalSeconds / 3.0));
        size = PrepareSnapshotSize(source, size);
        streamIndex.GetValueOrDefault();
        if (!streamIndex.HasValue)
        {
            VideoStream primaryVideoStream = source.PrimaryVideoStream;
            int index;
            if (primaryVideoStream == null)
            {
                VideoStream videoStream = source.VideoStreams.FirstOrDefault<VideoStream>();
                index = videoStream != null ? videoStream.Index : 0;
            }
            else
                index = primaryVideoStream.Index;
            streamIndex = new int?(index);
        }
        return (FFMpegArguments.FromUrlInput(input, (Action<FFMpegArgumentOptions>) (options => options.Seek(captureTime))), (Action<FFMpegArgumentOptions>) (options => options.SelectStream(streamIndex.Value, inputFileIndex).WithVideoCodec(codec ?? VideoCodec.Png).WithFrameOutputCount(1).Resize(size)));
    }
}