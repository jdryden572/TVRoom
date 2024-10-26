using CommunityToolkit.HighPerformance.Buffers;

namespace TVRoom.Transcode
{
    public sealed record IngestHlsFile(string FileName, IngestFileType FileType, MemoryOwner<byte> Payload);
}
