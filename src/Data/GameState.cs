using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Text.RegularExpressions;

namespace GCDTracker.Data
{
    public unsafe static class GameState {
        public static bool IsCasting() => DataStore.ClientState.LocalPlayer.CurrentCastTime > 0;
        
        public static bool CastingNonAbility() {
            var objectKind = DataStore.ClientState?.LocalPlayer?.TargetObject?.ObjectKind;

            return objectKind switch
            {
                ObjectKind.Aetheryte => true,
                ObjectKind.EventObj => true,
                ObjectKind.EventNpc => true,
                _ => DataStore.ActionManager->CastActionType 
                    is not ActionType.Action 
                    and not ActionType.None
            };
        }
        public static bool IsCastingTeleport() => 
            DataStore.TeleportIds.Contains(DataStore.Action->CastId);
        

        public static string GetCastbarContents() {
            if (DataStore.AtkStage == null){
                GCDTracker.Log.Warning("AtkStage was not loaded");
                return "";
            }
            var stringArrayData = DataStore.AtkStage->GetStringArrayData(StringArrayType.CastBar);
            if (stringArrayData == null) return "";
            string contents = HelperMethods.ReadStringFromPointer(stringArrayData[0].StringArray);
            string cleanedContents = new Regex("\x02.*?\x03").Replace(contents, string.Empty);
            return cleanedContents;
        }
        
    }
}