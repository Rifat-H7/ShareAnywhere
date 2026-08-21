/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using ShareAnywhere.Models;
using System.Collections.Concurrent;

namespace ShareAnywhere.Services
{
    public class PeerRegistry
    {
        private readonly ConcurrentDictionary<string, NearbyPeer> _peersByConnection = new();

        public NearbyPeer Add(string connectionId, string displayName, string roomKey)
        {
            var peer = new NearbyPeer
            {
                PeerId = connectionId,
                ConnectionId = connectionId,
                DisplayName = displayName,
                RoomKey = roomKey
            };

            _peersByConnection[connectionId] = peer;
            return peer;
        }

        public NearbyPeer? Remove(string connectionId)
        {
            return _peersByConnection.TryRemove(connectionId, out var peer) ? peer : null;
        }

        public NearbyPeer? Get(string peerId)
        {
            return _peersByConnection.TryGetValue(peerId, out var peer) ? peer : null;
        }

        public NearbyPeer? UpdateDisplayName(string peerId, string displayName)
        {
            if (!_peersByConnection.TryGetValue(peerId, out var peer))
            {
                return null;
            }

            peer.DisplayName = displayName;
            return peer;
        }

        public IReadOnlyCollection<NearbyPeer> GetRoomPeers(string roomKey, string? excludingPeerId = null)
        {
            return _peersByConnection.Values
                .Where(peer =>
                    string.Equals(peer.RoomKey, roomKey, StringComparison.Ordinal) &&
                    !string.Equals(peer.PeerId, excludingPeerId, StringComparison.Ordinal))
                .OrderBy(peer => peer.DisplayName)
                .ToArray();
        }
    }
}
