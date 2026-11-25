/*
 * © 2026 RH-Factory
 * Author: Md. Zawad Hossain Rifat
 * All rights reserved.
 *
 * This source code is the property of RH-Factory.
 * Unauthorized copying or distribution is prohibited.
 */
using Microsoft.AspNetCore.SignalR;
using ShareAnywhere.Models;
using ShareAnywhere.Services;
using ShareAnywhere.Services.Interfaces;
using System.Net;

namespace ShareAnywhere.Hubs;

public class NearbyHub(PeerRegistry peerRegistry, IFileStoreService files) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var networkKey = GetNetworkKey(http?.Connection.RemoteIpAddress);
        var displayName = SanitizeDisplayName(http?.Request.Query["name"].ToString());
        var deviceId = Guid.TryParseExact(http?.Request.Query["deviceId"].ToString(), "N", out var id)
            ? id.ToString("N")
            : Context.ConnectionId;
        var peer = peerRegistry.Add(Context.ConnectionId, deviceId, displayName, networkKey);
        await Clients.Caller.SendAsync("PeerRegistered", new { peer.PeerId, peer.DisplayName });
        var peers = peerRegistry.GetNetworkPeers(peer);
        await Clients.Caller.SendAsync("PeerList", peers.Select(other => new { other.PeerId, other.DisplayName }));
        await Clients.Clients(peers.Select(other => other.ConnectionId).ToArray())
            .SendAsync("PeerJoined", new { peer.PeerId, peer.DisplayName });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var peer = peerRegistry.Remove(Context.ConnectionId);
        if (peer != null)
        {
            await NearbyClients(peer).SendAsync("PeerLeft", peer.PeerId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<string> SendTransferOffer(string targetPeerId, string senderToken)
    {
        var sender = peerRegistry.Get(Context.ConnectionId);
        var receiver = peerRegistry.Get(targetPeerId ?? string.Empty);
        if (sender == null || receiver == null || sender.DeviceId == receiver.DeviceId ||
            !string.Equals(sender.NetworkKey, receiver.NetworkKey, StringComparison.Ordinal))
        {
            throw new HubException("Receiver is no longer available.");
        }

        var offer = files.CreateNearbyOffer(senderToken ?? string.Empty, sender.PeerId, receiver.PeerId)
            ?? throw new HubException("This file offer is unavailable.");
        await Clients.Client(receiver.ConnectionId).SendAsync("IncomingTransfer", new
        {
            OfferId = offer.Id,
            SenderName = sender.DisplayName,
            offer.FileName,
            offer.FileSize
        });
        return offer.Id;
    }

    public async Task UpdateName(string displayName)
    {
        var peer = peerRegistry.UpdateDisplayName(Context.ConnectionId, SanitizeDisplayName(displayName));
        if (peer != null)
        {
            await NearbyClients(peer)
                .SendAsync("PeerUpdated", new { peer.PeerId, peer.DisplayName });
        }
    }

    public async Task<string?> SendTransferResponse(string offerId, bool accepted)
    {
        var receiver = peerRegistry.Get(Context.ConnectionId);
        if (receiver == null)
        {
            throw new HubException("Nearby discovery is disconnected.");
        }

        var response = files.RespondToNearbyOffer(offerId ?? string.Empty, receiver.PeerId, accepted)
            ?? throw new HubException("This file offer is unavailable.");
        await Clients.Client(response.Offer.SenderPeerId).SendAsync("TransferResponse", new
        {
            OfferId = response.Offer.Id,
            ReceiverName = receiver.DisplayName,
            Accepted = accepted,
            response.Offer.FileName
        });

        var pathBase = Context.GetHttpContext()?.Request.PathBase;
        return response.Ticket == null ? null : $"{pathBase}/File/Receive/{response.Ticket}";
    }

    private IClientProxy NearbyClients(NearbyPeer peer) =>
        Clients.Clients(peerRegistry.GetNetworkPeers(peer).Select(other => other.ConnectionId).ToArray());

    private string GetNetworkKey(IPAddress? address)
    {
        if (address == null)
        {
            return $"connection:{Context.ConnectionId}";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return "loopback";
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 16)
        {
            return $"ipv6:{Convert.ToHexString(bytes.AsSpan(0, 8))}:{address.ScopeId}";
        }

        var isPrivate = bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
        return isPrivate
            ? $"ipv4-subnet:{bytes[0]}.{bytes[1]}.{bytes[2]}"
            : $"ipv4:{address}";
    }

    private string SanitizeDisplayName(string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var normalizedName = displayName.Trim();
            return normalizedName.Length > 40 ? normalizedName[..40] : normalizedName;
        }

        var suffix = Context.ConnectionId.Length >= 4 ? Context.ConnectionId[^4..].ToUpperInvariant() : "NEW";
        return $"Device {suffix}";
    }
}
