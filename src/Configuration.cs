﻿using Dalamud.Configuration;
using Dalamud.Logging;
using Dalamud.Plugin;
using GCDTracker.Data;
using ImGuiNET;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GCDTracker
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 3;

        [JsonIgnore]
        public bool configEnabled;
        //GCDWheel
        public bool WheelEnabled = true;
        [JsonIgnore]
        public bool WindowMoveableGW = false;
        public bool ShowOutOfCombatGW = false;
        public bool ColorClipEnabled = true;
        public bool ClipAlertEnabled = true;
        public int ClipAlertPrecision = 0;
        public float ClipTextSize = 0.86f;
        public Vector4 backCol = new(0.376f, 0.376f, 0.376f, 1);
        public Vector4 backColBorder = new(0f, 0f, 0f, 1f);
        public Vector4 frontCol = new(0.9f, 0.9f, 0.9f, 1f);
        public Vector4 ogcdCol = new(1f, 1f, 1f, 1f);
        public Vector4 anLockCol = new(0.334f, 0.334f, 0.334f, 0.667f);
        public Vector4 clipCol = new(1f, 0f, 0f, 0.667f);

        //GCDBar
        public bool BarEnabled = false;
        [JsonIgnore]
        public bool BarWindowMoveable = false;
        public bool BarShowOutOfCombat = false;
        public bool BarColorClipEnabled = true;
        public bool BarClipAlertEnabled = true;
        public float BarClipTextSize = 0.8f;
        public float BarBorderSize = 2f;
        public float BarWidthRatio = 0.9f;
        public float BarHeightRatio = 0.5f;
        public Vector4 BarBackCol = new(0.376f, 0.376f, 0.376f, 0.667f);
        public Vector4 BarBackColBorder = new(0f, 0f, 0f, 1f);
        public Vector4 BarFrontCol = new(0.9f, 0.9f, 0.9f, 1f);
        public Vector4 BarOgcdCol = new(1f, 1f, 1f, 1f);
        public Vector4 BarAnLockCol = new(0.334f, 0.334f, 0.334f, 0.667f);
        public Vector4 BarclipCol = new(1f, 0f, 0f, 0.667f);

        //Combo
        public bool ComboEnabled = false;
        [JsonIgnore]
        public bool WindowMoveableCT = false;
        public bool ShowOutOfCombatCT = false;
        public Vector4 ctComboUsed = new(0.431f, 0.431f, 0.431f, 1f);
        public Vector4 ctComboActive = new(1f, 1f, 1f, 1f);
        public Vector2 ctsep = new(23, 23);

        // ID Main Class, Name, Supported in GW, Supported in CT
        [JsonIgnore]
        private readonly List<(uint, string,bool,bool)> infoJobs = [
            (19,"PLD",true,true),
            (21,"WAR",true,true),
            (32,"DRK",true,true),
            (37,"GNB",true,true),
            (28,"SCH",true,false),
            (24,"WHM",true,false),
            (33,"AST",true,false),
            (20,"MNK",true,false),
            (22,"DRG",true,true),
            (30,"NIN",true,true),
            (34,"SAM",true,true),
            (25,"BLM",true,false),
            (27,"SMN",true,true),
            (35,"RDM",true,true),
            (23,"BRD",true,false),
            (31,"MCH",true,true),
            (38,"DNC",true,false),
            (39,"RPR",true,true),
            (40,"SGE",true,false),
            (41,"VPR",true,false),
            (42,"PCT",true,false)
        ];

        public Dictionary<uint, bool> EnabledGWJobs = new() {
            {1,true},
            {19,true},
            {3,true},
            {21,true},
            {32,true},
            {37,true},
            {26,true},
            {28,true},
            {6,true},
            {24,true},
            {33,true},
            {2,true},
            {20,true},
            {4,true},
            {22,true},
            {29,true},
            {30,true},
            {34,true},
            {7,true},
            {25,true},
            {27,true},
            {35,true},
            {5,true},
            {23,true},
            {31,true},
            {38,true},
            {39,true},
            {40,true},
            {41,true},
            {42,true},
        };

        public Dictionary<uint, bool> EnabledGBJobs = new() {
            {1,true},
            {19,true},
            {3,true},
            {21,true},
            {32,true},
            {37,true},
            {26,true},
            {28,true},
            {6,true},
            {24,true},
            {33,true},
            {2,true},
            {20,true},
            {4,true},
            {22,true},
            {29,true},
            {30,true},
            {34,true},
            {7,true},
            {25,true},
            {27,true},
            {35,true},
            {5,true},
            {23,true},
            {31,true},
            {38,true},
            {39,true},
            {40,true},
            {41,true},
            {42,true},
        };

        public Dictionary<uint, bool> EnabledCTJobs = new() {
            { 1, true },
            { 19, true },
            { 3, true },
            { 21, true },
            { 32, true },
            { 37, true },
            { 26, false },
            { 28, false },
            { 6, false },
            { 24, false },
            { 33, false },
            { 2, true },
            { 20, false },
            { 4, true },
            { 22, true },
            { 29, true },
            { 30, true },
            { 34, true },
            { 7, false },
            { 25, false },
            { 27, true },
            { 35, true },
            { 5, true },
            { 23, false },
            { 31, true },
            { 38, false },
            { 39, true },
            { 40, false },
            { 41, false },
            { 42, false },
        };

        // Add any other properties or methods here.
        [JsonIgnore] private IDalamudPluginInterface pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface) => this.pluginInterface = pluginInterface;
        public void Save() => pluginInterface.SavePluginConfig(this);

        public void DrawConfig() {
            if (!configEnabled) return;
            var scale = ImGui.GetIO().FontGlobalScale;
            ImGui.SetNextWindowSizeConstraints(new Vector2(500 * scale, 100 * scale),new Vector2(500 * scale,1000 * scale));
            ImGui.Begin("GCDTracker Settings",ref configEnabled,ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);

            if (ImGui.BeginTabBar("GCDConfig")){
                if (ImGui.BeginTabItem("GCDWheel")) {
                    ImGui.Checkbox("Enable GCDWheel", ref WheelEnabled);
                    if (WheelEnabled) {
                        ImGui.Checkbox("Move/resize window", ref WindowMoveableGW);
                        if (WindowMoveableGW)
                            ImGui.TextDisabled("\tWindow being edited, may ignore further visibility options.");
                        ImGui.Checkbox("Show out of combat", ref ShowOutOfCombatGW);
                        ImGui.Separator();

                        ImGui.Checkbox("Color wheel on clipped GCD", ref ColorClipEnabled);
                        ImGui.Checkbox("Show clip alert", ref ClipAlertEnabled);
                        if (ClipAlertEnabled) {
                            ImGui.SameLine();
                            ImGui.RadioButton("CLIP", ref ClipAlertPrecision, 0);
                            ImGui.SameLine();
                            ImGui.RadioButton("0.X", ref ClipAlertPrecision, 1);
                            ImGui.SameLine();
                            ImGui.RadioButton("0.XX", ref ClipAlertPrecision, 2);
                        }
                        ImGui.SliderFloat("Clip text size", ref ClipTextSize, 0.2f, 2f);

                        ImGui.Separator();
                        ImGui.Columns(2);
                        ImGui.ColorEdit4("Background bar color", ref backCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Background border color", ref backColBorder, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("GCD bar color", ref frontCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.NextColumn();
                        ImGui.ColorEdit4("GCD start indicator color", ref ogcdCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Animation lock bar color", ref anLockCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Clipping color", ref clipCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.Columns(1);
                        ImGui.Separator();

                        DrawJobGrid(ref EnabledGWJobs, true);
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("GCDBar")) {
                    ImGui.Checkbox("Enable GCDBar", ref BarEnabled);
                    if (BarEnabled) {
                        ImGui.Checkbox("Move/resize window", ref BarWindowMoveable);
                        if (BarWindowMoveable)
                            ImGui.TextDisabled("\tWindow being edited, may ignore further visibility options.");
                        ImGui.Checkbox("Show out of combat", ref BarShowOutOfCombat);
                        ImGui.Separator();

                        ImGui.Checkbox("Color bar on clipped GCD", ref BarColorClipEnabled);
                        ImGui.Checkbox("Show clip alert", ref BarClipAlertEnabled);
                        ImGui.SliderFloat("Clip text size", ref BarClipTextSize, 0.2f, 2f);

                        ImGui.Separator();
                        ImGui.Columns(2);
                        ImGui.ColorEdit4("Background bar color", ref BarBackCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Background border color", ref BarBackColBorder, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("GCD bar color", ref BarFrontCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.NextColumn();
                        ImGui.ColorEdit4("GCD start indicator color", ref BarOgcdCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Animation lock bar color", ref BarAnLockCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Clipping color", ref BarclipCol, ImGuiColorEditFlags.NoInputs);
                        ImGui.Columns(1);
                        ImGui.Separator();
                        ImGui.SliderFloat("Border size", ref BarBorderSize, 0f, 10f);
                        Vector2 size = new(BarWidthRatio, BarHeightRatio);
                        ImGui.SliderFloat2("Width and height ratio", ref size, 0.1f, 1f);
                        BarWidthRatio = size.X;
                        BarHeightRatio = size.Y;
                        ImGui.Separator();

                        DrawJobGrid(ref EnabledGBJobs, true);
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("ComboTrack")) {
                    ImGui.Checkbox("Enable ComboTrack", ref ComboEnabled);
                    if (ComboEnabled) {
                        ImGui.Checkbox("Move/resize window", ref WindowMoveableCT);
                        if (WindowMoveableCT)
                            ImGui.TextDisabled("\tWindow being edited, may ignore further visibility options.");
                        ImGui.Checkbox("Show out of combat", ref ShowOutOfCombatCT);
                        ImGui.Separator();

                        ImGui.ColorEdit4("Actions used color", ref ctComboUsed, ImGuiColorEditFlags.NoInputs);
                        ImGui.ColorEdit4("Active combo action color", ref ctComboActive, ImGuiColorEditFlags.NoInputs);
                        ImGui.SliderFloat2("Separation betwen actions", ref ctsep, 0, 100);
                        ImGui.Separator();

                        DrawJobGrid(ref EnabledCTJobs, false);
                    }
                    ImGui.EndTabItem();
                }
            }
            ImGui.End();
        }

        private void DrawJobGrid(ref Dictionary<uint, bool> enabledDict,bool colorPos) {
            var redCol = ImGui.GetColorU32(new Vector4(1f, 0, 0, 1f));
            ImGui.Text("Enabled jobs:");
            if (ImGui.BeginTable("Job Grid", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame)) {
                for (int i = 0; i < infoJobs.Count; i++) {
                    ImGui.TableNextColumn();

                    var enabled = enabledDict[infoJobs[i].Item1];
                    var supported = colorPos ? infoJobs[i].Item3 : infoJobs[i].Item4;
                    if (!supported) ImGui.PushStyleColor(ImGuiCol.Text, redCol);
                    if (ImGui.Checkbox(infoJobs[i].Item2, ref enabled)) {
                        enabledDict[infoJobs[i].Item1] = enabled;
                        enabledDict[HelperMethods.GetParentJob(infoJobs[i].Item1) ?? 0] = enabled;
                    }
                    if (!supported) ImGui.PopStyleColor();
                }
                ImGui.EndTable();
                if(infoJobs.Any(x=>colorPos? !x.Item3: !x.Item4))
                    ImGui.TextColored(new Vector4(1f,0,0,1f), "Jobs in red are not currently supported and may have bugs");
            }
        }
    }
}
