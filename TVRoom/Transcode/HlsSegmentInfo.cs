namespace TVRoom.Transcode
{
    public sealed record HlsSegmentInfo(
        string StreamInfo,
        int HlsVersion,
        int TargetDuration,
        double Duration,
        HlsSegment Segment);
}
