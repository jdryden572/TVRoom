using CommunityToolkit.HighPerformance.Buffers;

namespace TVRoom.HLS
{
    public sealed record IngestHlsFile(string FileName, IngestFileType FileType, MemoryOwner<byte> Payload);
}
