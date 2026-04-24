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
    }
}
