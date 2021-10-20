﻿using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using GCDTracker.Attributes;
using GCDTracker.Data;

namespace GCDTracker
{
    public class Plugin : IDalamudPlugin
    {
        [PluginService]
        [RequiredVersion("1.0")]
        private DalamudPluginInterface PluginInterface { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        private CommandManager Commands { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        public static Framework Framework { get; private set; }

        [PluginService]
        [RequiredVersion("1.0")]
        private ClientState ClientState { get; init; }


        [PluginService]
        [RequiredVersion("1.0")]
        private SigScanner Scanner { get; init; }

        [PluginService]
        [RequiredVersion("1.0")]
        private DataManager Data { get; init; }

        private readonly PluginCommandManager<Plugin> commandManager;
        private readonly Configuration config;
        private readonly PluginUI ui;

        public string Name => "GCDTracker";

        private Hook<HelperMethods.UseActionDelegate> UseActionHook;

        private List<Module> modules;

        public Plugin()
        {
            this.config = (Configuration)PluginInterface.GetPluginConfig() ?? new Configuration();
            this.config.Initialize(PluginInterface);

            DataStore.Init(Scanner,ClientState);
            HelperMethods.Init(Scanner);

            this.ui = new PluginUI(this.config);
            this.ui.conf = this.config;
            modules = new List<Module>(){
                new GCDWheel(),
                new ComboTracker()
            };
            ui.gcd = (GCDWheel)modules.Find(e => e is GCDWheel);
            ui.ct = (ComboTracker)modules.Find(e => e is ComboTracker);
            PluginInterface.UiBuilder.Draw += this.ui.Draw;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
            Framework.Update += this.ui.ct.Update;

            this.commandManager = new PluginCommandManager<Plugin>(this, Commands);

            UseActionHook = new Hook<HelperMethods.UseActionDelegate>(Scanner.ScanText("E8 ?? ?? ?? ?? 89 9F BC 76 02 00"), UseActionDetour);
            UseActionHook.Enable();
        }
        public unsafe byte UseActionDetour(IntPtr actionManager, uint actionType, uint actionID, long targetedActorID, uint param, uint useType, int pvp)
        {
            var ret = UseActionHook.Original(actionManager, actionType, actionID, targetedActorID, param, useType, pvp);
            foreach (var mod in modules)
            {
                mod.onActionUse(ret,actionManager, actionType, actionID, targetedActorID, param, useType, pvp);
            }
            return ret;
        }
        private void OpenConfig() { this.config.configEnabled = true; }

        [Command("/gcdtracker")]
        [HelpMessage("Open GCDTracker settings.")]
        public void GCDTrackerCommand(string command, string args)
        {
            // You may want to assign these references to private variables for convenience.
            // Keep in mind that the local player does not exist until after logging in.
            // var world = ClientState.LocalPlayer?.CurrentWorld.GameData;
            //Chat.Print($"Hello {world?.Name}!");
            //PluginLog.Log("Message sent successfully.2");
            this.OpenConfig();
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            UseActionHook?.Disable();

            this.commandManager.Dispose();

            PluginInterface.SavePluginConfig(this.config);

            PluginInterface.UiBuilder.Draw -= this.ui.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
            Framework.Update -= this.ui.ct.Update;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
    public abstract class Module {
        public PluginUI ui;
        public abstract void onActionUse(byte ret,IntPtr actionManager, uint actionType, uint actionID, long targetedActorID, uint param, uint useType, int pvp);

    }
}
