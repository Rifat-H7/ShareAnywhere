/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using Microsoft.AspNetCore.SignalR;
using ShareAnywhere.Services;
using System.Net;

namespace ShareAnywhere.Hubs
{
    public class NearbyHub : Hub
    {
        private readonly PeerRegistry _peerRegistry;

        public NearbyHub(PeerRegistry peerRegistry)
        {
            _peerRegistry = peerRegistry;
        }

        public override async Task OnConnectedAsync()
        {
            var roomKey = GetRoomKey();
            var displayName = GetDisplayName();
            var peer = _peerRegistry.Add(Context.ConnectionId, displayName, roomKey);

            await Groups.AddToGroupAsync(Context.ConnectionId, roomKey);
            await Clients.Caller.SendAsync("PeerRegistered", new
            {
                peer.PeerId,
                peer.DisplayName,
                peer.RoomKey
            });
            await Clients.Caller.SendAsync("PeerList", _peerRegistry.GetRoomPeers(roomKey, peer.PeerId));
            await Clients.GroupExcept(roomKey, Context.ConnectionId).SendAsync("PeerJoined", peer);

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var peer = _peerRegistry.Remove(Context.ConnectionId);
            if (peer != null)
            {
                await Clients.GroupExcept(peer.RoomKey, Context.ConnectionId).SendAsync("PeerLeft", peer.PeerId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, peer.RoomKey);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendTransferOffer(string targetPeerId, string fileName, long fileSize, string contentType, string downloadLink)
        {
            var sender = _peerRegistry.Get(Context.ConnectionId);
            var receiver = _peerRegistry.Get(targetPeerId);

            if (sender == null || receiver == null ||
                !string.Equals(sender.RoomKey, receiver.RoomKey, StringComparison.Ordinal))
            {
                throw new HubException("Receiver is no longer available.");
            }

            await Clients.Client(receiver.ConnectionId).SendAsync("IncomingTransfer", new
            {
                SenderPeerId = sender.PeerId,
                SenderName = sender.DisplayName,
                FileName = fileName,
                FileSize = Math.Max(0, fileSize),
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                DownloadLink = downloadLink
            });
        }

        public async Task UpdateName(string displayName)
        {
            var sanitizedName = SanitizeDisplayName(displayName, Context.ConnectionId);
            var peer = _peerRegistry.UpdateDisplayName(Context.ConnectionId, sanitizedName);
            if (peer == null)
            {
                return;
            }

            await Clients.Caller.SendAsync("PeerRegistered", new
            {
                peer.PeerId,
                peer.DisplayName,
                peer.RoomKey
            });
            await Clients.GroupExcept(peer.RoomKey, Context.ConnectionId).SendAsync("PeerUpdated", peer);
        }

        public async Task SendTransferResponse(string targetPeerId, bool accepted, string fileName)
        {
            var sender = _peerRegistry.Get(Context.ConnectionId);
            var receiver = _peerRegistry.Get(targetPeerId);

            if (sender == null || receiver == null ||
                !string.Equals(sender.RoomKey, receiver.RoomKey, StringComparison.Ordinal))
            {
                return;
            }

            await Clients.Client(receiver.ConnectionId).SendAsync("TransferResponse", new
            {
                ReceiverName = sender.DisplayName,
                Accepted = accepted,
                FileName = fileName
            });
        }

        private string GetDisplayName()
        {
            var requestedName = Context.GetHttpContext()?.Request.Query["name"].ToString();
            return SanitizeDisplayName(requestedName, Context.ConnectionId);
        }

        private static string SanitizeDisplayName(string? displayName, string connectionId)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                var normalizedName = displayName.Trim();
                return normalizedName.Length > 40 ? normalizedName[..40] : normalizedName;
            }

            var suffix = connectionId.Length >= 4 ? connectionId[^4..].ToUpperInvariant() : "NEW";
            return $"Device {suffix}";
        }

        private string GetRoomKey()
        {
            var httpContext = Context.GetHttpContext();
            var requestedRoom = httpContext?.Request.Query["room"].ToString();
            if (!string.IsNullOrWhiteSpace(requestedRoom))
            {
                return $"room:{requestedRoom.Trim().ToUpperInvariant()}";
            }

            var ipAddress = GetClientIpAddress(httpContext);
            if (ipAddress == null)
            {
                return "network:unknown";
            }

            if (ipAddress.IsIPv4MappedToIPv6)
            {
                ipAddress = ipAddress.MapToIPv4();
            }

            var bytes = ipAddress.GetAddressBytes();
            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && bytes.Length == 4)
            {
                return $"ipv4:{bytes[0]}.{bytes[1]}.{bytes[2]}.*";
            }

            if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && bytes.Length == 16)
            {
                return $"ipv6:{Convert.ToHexString(bytes[..8])}";
            }

            return $"ip:{ipAddress}";
        }

        private static IPAddress? GetClientIpAddress(HttpContext? httpContext)
        {
            var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var firstForwardedIp = forwardedFor?.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(firstForwardedIp) && IPAddress.TryParse(firstForwardedIp, out var forwardedIp))
            {
                return forwardedIp;
            }

            return httpContext?.Connection.RemoteIpAddress;
        }
    }
}
