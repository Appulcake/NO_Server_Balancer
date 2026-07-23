using System;
using NuclearOption.Chat;
using NuclearOption.Networking;

namespace NO_SB;

internal static class Utils
{
    private const int MaxChatMessageLength = 128;
    private const string ServerWhisperPrefix = "LCAC: ";
    
    internal static bool TryGetPlayerSteamId(Player? player, out ulong steamId)
    {
        steamId = 0UL;
        
        if (player == null) return false;
        
        steamId = player.SteamID;
        
        // In case for some reason steamID returns 0
        return steamId != 0UL;
    }
    
    internal static bool TrySendWhisper(Player? targetPlayer, string? message)
    {
        if (targetPlayer == null)
        {
            Plugin.Logger.LogWarning("Cannot send private chat message: target player is null.");
            
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(message))
        {
            Plugin.Logger.LogWarning("Cannot send private chat message: message is empty.");
            
            return false;
        }
        
        var chatManager = ChatManager.i;
        
        if (chatManager == null)
        {
            Plugin.Logger.LogWarning("Cannot send private chat message: ChatManager is unavailable.");
            
            return false;
        }
        
        var actualMessage = $"{ServerWhisperPrefix}{message?.Trim()}";
        
        try
        {
            for (var offset = 0;
                 offset < actualMessage.Length;
                 offset += MaxChatMessageLength)
            {
                var chunkLength = Math.Min(MaxChatMessageLength, actualMessage.Length - offset);
                
                var chunk = actualMessage.Substring(offset, chunkLength);
                
                chatManager.RpcTargetServerMessage(targetPlayer.Owner, chunk, true);
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning($"Failed to send private chat message to {targetPlayer.PlayerName}: {exception}");
            
            return false;
        }
        
        Plugin.Logger.LogDebug($"Sent private message to {targetPlayer.PlayerName}: " + actualMessage);
        
        return true;
    }
}