namespace TVRoom.HLS
{
    public sealed record HlsSegmentInfo(
        string StreamInfo,
        int HlsVersion,
        int TargetDuration,
        double Duration,
        HlsSegment Segment);
}
