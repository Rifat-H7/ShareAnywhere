/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
namespace ShareAnywhere.Models
{
    public class NearbyPeer
    {
        public required string PeerId { get; set; }
        public required string ConnectionId { get; set; }
        public required string DisplayName { get; set; }
        public required string RoomKey { get; set; }
        public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
