using System.Diagnostics.CodeAnalysis;

namespace TVRoom.HLS
{
    public abstract record IngestHlsFile(SharedBuffer Payload);

    public sealed record IngestMasterPlaylist(SharedBuffer Payload) : IngestHlsFile(Payload)
    {
        public bool TryParse([MaybeNullWhen(false)] out ParsedMasterPlaylist parsedMasterPlaylist)
        {
            using var lease = Payload.Rent();
            return ParsedMasterPlaylist.TryParse(lease.GetSpan(), out parsedMasterPlaylist);
        }
    }

    public sealed record IngestStreamPlaylist(SharedBuffer Payload) : IngestHlsFile(Payload)
    {
        public bool TryParse([MaybeNullWhen(false)] out ParsedStreamPlaylist parsedStreamPlaylist)
        {
            using var lease = Payload.Rent();
            return ParsedStreamPlaylist.TryParse(lease.GetSpan(), out parsedStreamPlaylist);
        }
    }

    public sealed record IngestStreamSegment(string FileName, SharedBuffer Payload) : IngestHlsFile(Payload);
}
