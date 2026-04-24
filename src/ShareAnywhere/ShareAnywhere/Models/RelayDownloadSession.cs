using System.Threading.Channels;

namespace ShareAnywhere.Models
{
    public class RelayDownloadSession
    {
        public required string TransferId { get; set; }
        public required string FileName { get; set; }
        public required string ContentType { get; set; }
        public required ChannelReader<byte[]> Reader { get; set; }
    }
}
