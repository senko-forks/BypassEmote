using BypassEmote.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using NoireLib;
using NoireLib.Helpers;

namespace BypassEmote.Helpers;

public static class IpcHelper
{
    internal unsafe static nint GetCompanionAddress(ICharacter chara)
    {
        var native = CharacterHelper.GetCharacterAddress(chara);
        return (nint)native->CompanionData.CompanionObject;
    }

    internal unsafe static nint GetPetAddress(ICharacter chara)
    {
        var native = CharacterHelper.GetCharacterAddress(chara);
        var manager = CharacterManager.Instance();
        return (nint)manager->LookupPetByOwnerObject((BattleChara*)native);
    }

    public static bool IsLocalObject(ICharacter chara)
    {
        var localPlayer = NoireService.ObjectTable.LocalPlayer;
        if (localPlayer == null) return false;
        var playerAddress = localPlayer.Address;
        var companionAddress = GetCompanionAddress(localPlayer);
        var petAddress = GetPetAddress(localPlayer);
        return (playerAddress == chara.Address || companionAddress == chara.Address || petAddress == chara.Address);
    }

    public static void HandlePlayEmote(ICharacter chara, Emote emote)
    {
        if (IsLocalObject(chara))
        {
            // Fire IPC event after delay only if local player is the one playing the emote
            // The delay tries to ensure that your character has stopped moving on other clients (other players' screens) before notifying IPC subscribers
            // This is due to the slight desync/delay there is between 2 players when performing any action because this is how the game servers work
            // Without this delay, other players might see your character perform the bypassed emote, but then you will still be moving thus stopping the bypassed emote
            // This is also mitigated by the OnFrameworkUpdate check for position/rotation changes, but this delay helps a lot with consistency
            var provider = Service.Ipc;
            var ipcData = new IpcData(emote.RowId);
            bool isLooped = CommonHelper.GetEmotePlayType(emote) == Models.EmotePlayType.Looped;

            provider?.OnStateChangeImmediate?.Invoke(chara.Address, ipcData.Serialize());
            provider?.OnEmoteStateStartImmediate?.Invoke(chara.Address, isLooped, ipcData.Serialize());

            DelayerHelper.CancelAll();
            DelayerHelper.Start($"PlayBypassedEmote{chara.ObjectIndex}", () =>
            {
                provider?.OnStateChange?.Invoke(chara.Address, ipcData.Serialize());
                provider?.OnEmoteStateStart?.Invoke(chara.Address, isLooped, ipcData.Serialize());
            }, 500);
        }
    }

    public static void HandleStopEmote(ICharacter chara)
    {
        if (IsLocalObject(chara))
        {
            // Fire IPC event only if local player is stopping a looped emote
            var trackedCharacter = CommonHelper.TryGetTrackedCharacterFromAddress(chara.Address);

            if (trackedCharacter != null)
            {
                // Tell IPC Callers that the emote has stopped immediately and again after the delay
                // Kinda hacky-whacky way to ensure the emote stop is registered properly with sync but it works.
                // This is needed to avoid the server position desync issue.
                // When player A moves and bypasses an emote, this player might still be moving on player B's screen when player A starts the emote, causing a false-positive "stop emote" message
                uint playingEmoteId = trackedCharacter.PlayingEmoteId ?? 0;
                var provider = Service.Ipc;
                var ipcDataStop = new IpcData(0).Serialize();

                provider?.OnStateChangeImmediate?.Invoke(chara.Address, ipcDataStop);
                provider?.OnEmoteStateStopImmediate?.Invoke(chara.Address);

                provider?.OnStateChange?.Invoke(chara.Address, ipcDataStop);
                provider?.OnEmoteStateStop?.Invoke(chara.Address);

                DelayerHelper.CancelAll();
                DelayerHelper.Start($"PlayBypassedEmote{chara.ObjectIndex}", () =>
                {
                    provider?.OnStateChange?.Invoke(chara.Address, ipcDataStop);
                    provider?.OnEmoteStateStop?.Invoke(chara.Address);
                }, 500);
            }
        }
    }
}
