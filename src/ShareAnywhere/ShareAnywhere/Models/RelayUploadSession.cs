using System.Threading.Channels;

namespace ShareAnywhere.Models
{
    public class RelayUploadSession
    {
        public required ChannelWriter<byte[]> Writer { get; set; }
    }
}
