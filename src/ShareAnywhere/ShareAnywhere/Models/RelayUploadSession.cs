/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using System.Threading.Channels;

namespace ShareAnywhere.Models
{
    public class RelayUploadSession
    {
        public required ChannelWriter<byte[]> Writer { get; set; }
        public required long FileSize { get; init; }
        public required Task<int?> ReceiverResult { get; init; }
        public required CancellationToken TransferEnded { get; init; }
    }
}
