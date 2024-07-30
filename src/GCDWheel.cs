using Dalamud.Game;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using GCDTracker.Data;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests")]
namespace GCDTracker {
    public record AbilityTiming(float AnimationLock, bool IsCasted);

    public unsafe class GCDWheel {
        private readonly Configuration conf;
        private readonly IDataManager dataManager;
        public Dictionary<float, AbilityTiming> ogcds = [];
        public float TotalGCD = 3.5f;
        private DateTime lastGCDEnd = DateTime.Now;

        private float lastElapsedGCD;
        private float lastClipDelta;
        private ulong targetBuffer = 1;

        public int idleTimerAccum;
        public int GCDTimeoutBuffer;
        public bool abcBlocker;
        public bool lastActionTP;

        private bool idleTimerReset = true;
        private bool idleTimerDone;
        private bool lastActionCast;

        private bool clippedGCD;
        private bool checkClip;
        private bool checkABC;
        private bool abcOnThisGCD;
        private bool abcOnLastGCD;
        private bool isRunning;
        private bool isHardCast;
        private string queuedAbilityName = " ";
        private string hardCastAbilityTime;
        private string shortCastCachedSpellName = " ";
        private Vector4 bgCache;
        private bool shortCastFinished = false;

        public GCDWheel(Configuration conf, IDataManager dataManager) {
            this.conf = conf;
            this.dataManager = dataManager;
        }

        public void OnActionUse(byte ret, ActionManager* actionManager, ActionType actionType, uint actionID, ulong targetedActorID, uint param, uint useType, int pvp) {
            var act = DataStore.Action;
            var isWeaponSkill = HelperMethods.IsWeaponSkill(actionType, actionID);
            var addingToQueue = HelperMethods.IsAddingToQueue(isWeaponSkill, act) && useType != 1;
            var executingQueued = act->InQueue && !addingToQueue;
            if (ret != 1) {
                if (executingQueued && Math.Abs(act->ElapsedCastTime-act->TotalCastTime) < 0.0001f && isWeaponSkill)
                    ogcds.Clear();
                return;
            }
            //check to make sure that the player is targeting something, so that if they are spamming an action
            //button after the mob dies it won't update the targetBuffer and trigger an ABC
            if (DataStore.ClientState.LocalPlayer?.TargetObject != null)
                targetBuffer = DataStore.ClientState.LocalPlayer.TargetObjectId;
            if (addingToQueue) {
                AddToQueue(act, isWeaponSkill);
                    queuedAbilityName = GetAbilityName(actionID, DataStore.ClientState.LocalPlayer.CastActionType);
            } else {
                if (isWeaponSkill) {
                    EndCurrentGCD(TotalGCD);
                    //Store GCD in a variable in order to cache it when it goes back to 0
                    TotalGCD = act->TotalGCD;
                    AddWeaponSkill(act);
                        queuedAbilityName = GetAbilityName(actionID, DataStore.ClientState.LocalPlayer.CastActionType);
                } else if (!executingQueued) {
                    ogcds[act->ElapsedGCD] = new(act->AnimationLock, false);
                }
            }
        }

        //probably should find a way to do this from DataStore so we aren't passing the world
        //into GCDWheel
        private string GetAbilityName(uint actionID, byte actionType) {
            var lumina = dataManager;



            switch (actionType) {
                    //seem to need case 0 here for follow up casts for short spells (gcdTime>castTime).
                    case 0:
                    case 1:
                    var ability = lumina.GetExcelSheet<Lumina.Excel.GeneratedSheets.Action>()?.GetRow(actionID);
                    return ability?.Name;

                    case 2:
                    var item = lumina.GetExcelSheet<Lumina.Excel.GeneratedSheets.Item>()?.GetRow(actionID);
                    return item?.Name;

                    case 13:
                    var mount = lumina.GetExcelSheet<Lumina.Excel.GeneratedSheets.Mount>()?.GetRow(actionID);
                    return CapitalizeOutput(mount?.Singular);
                    
                    default:
                    //so, we're not going to talk about this, and I'm going to deny ever doing it.
                    if (DataStore.ClientState.LocalPlayer.TargetObject.ObjectKind.ToString() == "Aetheryte") {
                        return "Attuning...";
                    }
                    if (DataStore.ClientState.LocalPlayer.TargetObject.ObjectKind.ToString() == "EventObj") {
                        return "Interacting...";
                    }
                    if (DataStore.ClientState.LocalPlayer.TargetObject.ObjectKind.ToString() == "EventNpc") {
                        return "Interacting...";
                    }
                    return "... " + actionID.ToString() + " " +actionType.ToString() + " " + DataStore.ClientState.LocalPlayer.TargetObject.ObjectKind.ToString();
            }
        }

        private string CapitalizeOutput(string input) {
            if (string.IsNullOrEmpty(input))
                return input;

            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            return textInfo.ToTitleCase(input.ToLower());
        }

        private void AddToQueue(Data.Action* act, bool isWeaponSkill) {
            var timings = new List<float>() {
                isWeaponSkill ? act->TotalGCD : 0, // Weapon skills
            };
            if (!act->IsCast) {
                // Add OGCDs
                timings.Add(act->ElapsedGCD + act->AnimationLock);
            } else if (act->ElapsedCastTime < act->TotalGCD) {
                // Add Casts
                timings.Add(act->TotalCastTime + 0.1f);
            } else {
                // Add Casts after 1 whole GCD of casting
                timings.Add(act->TotalCastTime - act->ElapsedCastTime + 0.1f);
            }
            ogcds[timings.Max()] = new(0.64f, false);
        }

        private void AddWeaponSkill(Data.Action* act) {
            if (act->IsCast) {
                lastActionCast = true;
                ogcds[0f] = new(0.1f, false);
                ogcds[act->TotalCastTime] = new(0.1f, true);
            } else {
                ogcds[0f] = new(act->AnimationLock, false);
            }
        }

        public void Update(IFramework framework) {
            if (DataStore.ClientState.LocalPlayer == null)
                return;
            CleanFailedOGCDs();
            GCDTimeoutHelper(framework);
            hardCastAbilityTime = (DataStore.Action->TotalCastTime - DataStore.Action->ElapsedCastTime).ToString("F1");
            if (lastActionCast && !HelperMethods.IsCasting())
                HandleCancelCast();
            else if (DataStore.Action->ElapsedGCD < lastElapsedGCD)
                EndCurrentGCD(lastElapsedGCD);
            else if (DataStore.Action->ElapsedGCD < 0.0001f)
                SlideGCDs((float)(framework.UpdateDelta.TotalMilliseconds * 0.001), false);
            lastElapsedGCD = DataStore.Action->ElapsedGCD;
        }

        private void CleanFailedOGCDs() {
            if (DataStore.Action->AnimationLock == 0 && ogcds.Count > 0) {
                ogcds = ogcds
                    .Where(x => x.Key > DataStore.Action->ElapsedGCD || x.Key + x.Value.AnimationLock < DataStore.Action->ElapsedGCD)
                    .ToDictionary(x => x.Key, x => x.Value);
            }
        }

        private void GCDTimeoutHelper(IFramework framework) {
            // Determine if we are running
            isRunning = (DataStore.Action->ElapsedGCD != DataStore.Action->TotalGCD) || HelperMethods.IsCasting();
            // Reset idleTimer when we start casting
            if (isRunning && idleTimerReset) {
                idleTimerAccum = 0;
                isHardCast = false;
                idleTimerReset = false;
                idleTimerDone = false;
                abcBlocker = false;
                GCDTimeoutBuffer = (int)(1000 * conf.GCDTimeout);
            }
            if (!isRunning && !idleTimerDone) {
                idleTimerAccum += framework.UpdateDelta.Milliseconds;
                idleTimerReset = true;
            }
            // Handle caster tax
            if (!isHardCast && HelperMethods.IsCasting() && DataStore.Action->TotalCastTime - 0.1f >= DataStore.Action->TotalGCD)
                isHardCast = true;
            checkABC = !abcBlocker && (idleTimerAccum >= (isHardCast ? (conf.abcDelay + 100) : conf.abcDelay));
            // Reset state after the GCDTimeout
            if (idleTimerAccum >= GCDTimeoutBuffer) {
                checkABC = false;
                clippedGCD = false;
                checkClip = false;
                abcOnLastGCD = false;
                abcOnThisGCD = false;
                lastActionTP = false;
                idleTimerDone = true;
            }
        }

        private void HandleCancelCast() {
            lastActionCast = false;
            EndCurrentGCD(DataStore.Action->TotalCastTime);
        }

        /// <summary>
        /// This function slides all the GCDs forward by a delta and deletes the ones that reach 0
        /// </summary>
        internal void SlideGCDs(float delta, bool isOver) {
            if (delta <= 0) return; //avoid problem with float precision
            var ogcdsNew = new Dictionary<float, AbilityTiming>();
            foreach (var (k, (v,vt)) in ogcds) {
                if (k < -0.1) { } //remove from dictionary
                else if (k < delta && v > delta) {
                    ogcdsNew[k] = new(v - delta, vt);
                } else if (k > delta) {
                    ogcdsNew[k - delta] = new(v, vt);
                } else if (isOver && k + v > TotalGCD) {
                    ogcdsNew[0] = new(k + v - delta, vt);
                    if (k < delta - 0.02f) // Ignore things that are queued or queued + cast end animation lock
                        lastClipDelta = k + v - delta;
                }
            }
            ogcds = ogcdsNew;
        }

        private bool ShouldStartClip() {
            checkClip = false;
            clippedGCD = lastClipDelta > 0.01f;
            return clippedGCD;
        }

        private bool ShouldStartABC() {
            abcBlocker = true;
            // compare cached target object ID at the time of action use to the current target object ID
            return DataStore.ClientState.LocalPlayer.TargetObjectId == targetBuffer;
        }

        private void FlagAlerts(PluginUI ui){
            bool inCombat = DataStore.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
            if(conf.ClipAlertEnabled && (!conf.HideAlertsOutOfCombat || inCombat)){
                if (checkClip && ShouldStartClip()) {
                    ui.StartAlert(true, lastClipDelta);
                    lastClipDelta = 0;
                }
            }
            if (conf.abcAlertEnabled && (!conf.HideAlertsOutOfCombat || inCombat)){
                if (!clippedGCD && checkABC && !abcBlocker && ShouldStartABC()) {
                    ui.StartAlert(false, 0);
                    abcOnThisGCD = true;
                }
            }
        }

        private void InvokeAlerts(float relx, float rely, PluginUI ui){
            if (conf.ClipAlertEnabled && clippedGCD)
                ui.DrawAlert(relx, rely, conf.ClipTextSize, conf.ClipTextColor, conf.ClipBackColor, conf.ClipAlertPrecision);
            if (conf.abcAlertEnabled && (abcOnThisGCD || abcOnLastGCD))
                ui.DrawAlert(relx, rely, conf.abcTextSize, conf.abcTextColor, conf.abcBackColor, 3);
           }

        public Vector4 BackgroundColor(){
            var bg = conf.backCol;
            if (conf.ColorClipEnabled && clippedGCD)
                bg = conf.clipCol;
            if (conf.ColorABCEnabled && (abcOnLastGCD || abcOnThisGCD))
                bg = conf.abcCol;
            return bg;
        }

        public void DrawGCDWheel(PluginUI ui) {
            float gcdTotal = TotalGCD;
            float gcdTime = lastElapsedGCD;
            if (conf.ShowOnlyGCDRunning && HelperMethods.IsTeleport(DataStore.Action->CastId)) {
                lastActionTP = true;
                return;
            }
            if (HelperMethods.IsCasting() && DataStore.Action->ElapsedCastTime >= gcdTotal && !HelperMethods.IsTeleport(DataStore.Action->CastId))
                gcdTime = gcdTotal;
            if (gcdTotal < 0.1f) return;
            FlagAlerts(ui);
            InvokeAlerts(0.5f, 0, ui);
            // Background
            ui.DrawCircSegment(0f, 1f, 6f * ui.Scale, conf.backColBorder);
            ui.DrawCircSegment(0f, 1f, 3f * ui.Scale, BackgroundColor());
            if (conf.QueueLockEnabled) {
                ui.DrawCircSegment(0.8f, 1, 9f * ui.Scale, conf.backColBorder);
                ui.DrawCircSegment(0.8f, 1, 6f * ui.Scale, BackgroundColor());
            }
            ui.DrawCircSegment(0f, Math.Min(gcdTime / gcdTotal, 1f), 20f * ui.Scale, conf.frontCol);
            foreach (var (ogcd, (anlock, iscast)) in ogcds) {
                var isClipping = CheckClip(iscast, ogcd, anlock, gcdTotal, gcdTime);
                ui.DrawCircSegment(ogcd / gcdTotal, (ogcd + anlock) / gcdTotal, 21f * ui.Scale, isClipping ? conf.clipCol : conf.anLockCol);
                if (!iscast) ui.DrawCircSegment(ogcd / gcdTotal, (ogcd + 0.04f) / gcdTotal, 23f * ui.Scale, conf.ogcdCol);
            }
        }

        public void DrawGCDBar(PluginUI ui) {
            float gcdTotal = TotalGCD;
            float gcdTime = lastElapsedGCD;

            if (conf.ShowOnlyGCDRunning && HelperMethods.IsTeleport(DataStore.Action->CastId)) {
                lastActionTP = true;
                return;
            }
            if (HelperMethods.IsCasting() && DataStore.Action->ElapsedCastTime >= gcdTotal && !HelperMethods.IsTeleport(DataStore.Action->CastId))
                gcdTime = gcdTotal;
            if (gcdTotal < 0.1f) return;
            FlagAlerts(ui);
            InvokeAlerts((conf.BarWidthRatio + 1) / 2.1f, -0.3f, ui);
            DrawBarElements(ui, false, shortCastFinished, gcdTime / gcdTotal, gcdTime, gcdTotal);

            // Gonna re-do this, but for now, we flag when we need to carryover from the castbar to the GCDBar
            // and dump all the crap here to draw on top. 
            if (shortCastFinished) {
                string abilityNameOutput = shortCastCachedSpellName;
                if (queuedAbilityName != " ")
                    abilityNameOutput = shortCastCachedSpellName + " -> " + queuedAbilityName;
                if (!string.IsNullOrEmpty(abilityNameOutput))
                    DrawBarText(ui, abilityNameOutput);
            }

        }

        public void DrawCastBar (PluginUI ui) {
            float gcdTotal = DataStore.Action->TotalGCD;
            float castTotal = DataStore.Action->TotalCastTime;
            float castElapsed = DataStore.Action->ElapsedCastTime;
            float castbarProgress = castElapsed / castTotal;
            float castbarEnd = 1f;
            float slidecastOffset = 0.5f;
            float slidecastStart = Math.Max((castTotal - slidecastOffset) / castTotal, 0f);
            float slidecastEnd = castbarEnd;

            // handle short casts
            if (gcdTotal > castTotal) {
                castbarEnd = castTotal / gcdTotal;
                slidecastStart = Math.Max((castTotal - slidecastOffset) / gcdTotal, 0f);
                slidecastEnd = conf.SlideCastFullBar ? 1f : castbarEnd;
            }

            DrawBarElements(ui, true, gcdTotal > castTotal, castbarProgress * castbarEnd, slidecastStart, slidecastEnd);

            // Text
            var abilityID = DataStore.ClientState.LocalPlayer.CastActionId;
            var actionType = DataStore.ClientState.LocalPlayer.CastActionType;
            if (!string.IsNullOrEmpty(GetAbilityName(abilityID, actionType))) {
                //reset the queued name when we start to cast.
                if (castbarProgress <= 0.25f) {
                    queuedAbilityName = " ";
                }
                if (castbarEnd - castbarProgress <= 0.01f && gcdTotal > castTotal) {
                    shortCastFinished = true;
                    shortCastCachedSpellName = GetAbilityName(abilityID, actionType);
                }
                string abilityNameOutput = GetAbilityName(abilityID, actionType);
                if (queuedAbilityName != " ")
                    abilityNameOutput = GetAbilityName(abilityID, actionType) + " -> " + queuedAbilityName;
                    
                DrawBarText(ui, abilityNameOutput);
            }
        }

        private void DrawBarText(PluginUI ui, string abilityNameOutput){
            int barWidth = (int)(ui.w_size.X * conf.BarWidthRatio);
            string combinedText = abilityNameOutput + hardCastAbilityTime;
            Vector2 spellNamePos = new(ui.w_cent.X - ((float)barWidth / 2.05f), ui.w_cent.Y);
            Vector2 spellTimePos = new(ui.w_cent.X + ((float)barWidth / 2.05f), ui.w_cent.Y);

            if (!string.IsNullOrEmpty(abilityNameOutput))
                ui.DrawHardCastAbilityName(abilityNameOutput, combinedText, spellNamePos, conf.CastBarTextSize);
            if (!string.IsNullOrEmpty(hardCastAbilityTime))
                ui.DrawHardCastAbilityTime(hardCastAbilityTime, combinedText, spellTimePos, conf.CastBarTextSize);
        }

        private void DrawBarElements(PluginUI ui, bool isCastBar, bool isShortCast, float castBarCurrentPos, float gcdTime_slidecastStart, float gcdTotal_slidecastEnd) {
            int barHeight = (int)(ui.w_size.Y * conf.BarHeightRatio);
            int barWidth = (int)(ui.w_size.X * conf.BarWidthRatio);
            int halfBarHeight = barHeight % 2 == 0 ? (barHeight / 2) : (barHeight / 2) + 1;
            int halfBarWidth = barWidth % 2 == 0 ? (barWidth / 2) : (barWidth / 2) + 1;
            int borderSize = (int)conf.BarBorderSize;
            int halfBorderSize = borderSize % 2 == 0 ? (borderSize / 2) : (borderSize / 2) + 1;
            float barGCDClipTime = 0;
            Vector2 start = new(
                (int)(ui.w_cent.X - (barWidth / 2)), 
                (int)(ui.w_cent.Y - (barHeight / 2))
            );
            Vector2 end = new(
                (int)(ui.w_cent.X + halfBarWidth), 
                (int)(ui.w_cent.Y + halfBarHeight)
            );
            Vector2 gcdBarEnd = new(
                (int)(ui.w_cent.X + (castBarCurrentPos * barWidth) - halfBarWidth),
                (int)(ui.w_cent.Y + halfBarHeight)
            );
            
            // in both modes:
            // draw the background
            if (!isCastBar)
                bgCache = BackgroundColor();
            if (isCastBar && castBarCurrentPos < 0.25f)
                bgCache = BackgroundColor();
            ui.DrawRectFilledNoAA(start, end, bgCache);

            // in both modes:
            // draw cast/gcd progress (main) bar
            ui.DrawRectFilledNoAA(start, gcdBarEnd, conf.frontCol);
            
            // in Castbar mode:
            // draw the slidecast bar
            if (conf.SlideCastEnabled && isCastBar) {
                float slidecastStart = gcdTime_slidecastStart;
                float slidecastEnd = gcdTotal_slidecastEnd;
                if (slidecastStart <= castBarCurrentPos)
                    slidecastStart = castBarCurrentPos;


                //ui.DrawBar(slidecastStartBuffer, slidecastEndBuffer, barWidth, barHeight, conf.slideCol);
                //ui.DrawBar(slidecastStartBuffer, slidecastStartBuffer + ((float)borderSize / barWidth), barWidth, barHeight, conf.BarBackColBorder);

                Vector2 slidecastStartVector_BottomLeft = new(
                    (int)(ui.w_cent.X + (slidecastStart * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y + halfBarHeight)
                    );
                Vector2 slidecastStartVector_TopRight = new(
                    (int)(ui.w_cent.X + ((slidecastStart + ((float)borderSize / barWidth)) * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y - ((barHeight) / 2))
                    );
                Vector2 slidecastStartVector_BottomRight = new(
                    (int)(ui.w_cent.X + ((slidecastStart + ((float)borderSize / barWidth)) * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y + halfBarHeight)
                    );
                Vector2 slidecastEndVector = new(
                    (int)(ui.w_cent.X + (slidecastEnd * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y + halfBarHeight)
                );

                Vector2 topRight_LeftVertex = new Vector2(slidecastStartVector_TopRight.X, slidecastStartVector_TopRight.Y);

                Vector2 bottomLeft_LeftVertex = new Vector2(slidecastStartVector_BottomLeft.X - conf.triangleSize, slidecastStartVector_BottomLeft.Y );
                Vector2 bottomLeft_RightVertex = new Vector2(slidecastStartVector_BottomLeft.X, slidecastStartVector_BottomLeft.Y);
                Vector2 bottomLeft_TopVertex = new Vector2(slidecastStartVector_BottomLeft.X, slidecastStartVector_BottomLeft.Y - conf.triangleSize);
                Vector2 bottomRight_LeftVertex = new Vector2(slidecastStartVector_BottomRight.X, slidecastStartVector_BottomRight.Y);
                Vector2 bottomRight_RightVertex = new Vector2(slidecastStartVector_BottomRight.X + conf.triangleSize + 1, slidecastStartVector_BottomRight.Y);
                Vector2 bottomRight_TopVertex = new Vector2(slidecastStartVector_BottomRight.X, slidecastStartVector_BottomRight.Y - (conf.triangleSize + 1));

                // draw slidecast bar
                ui.DrawRectFilledNoAA(topRight_LeftVertex, slidecastEndVector, conf.slideCol);
                // draw sidecast vertical line
                ui.DrawRectFilledNoAA(topRight_LeftVertex, bottomLeft_RightVertex, conf.BarBackColBorder);


                if(isShortCast){  
                    //bottom left
                    ui.DrawRightTriangle(bottomLeft_LeftVertex, bottomLeft_RightVertex, bottomLeft_TopVertex, conf.BarBackColBorder);
                    //bottom right
                    ui.DrawRightTriangle(bottomRight_LeftVertex, bottomRight_RightVertex, bottomRight_TopVertex, conf.BarBackColBorder);
                }
            }

            // in GCDBar mode:
            // draw oGCDs and clips
            if (!isCastBar) {
                float gcdTime = gcdTime_slidecastStart;
                float gcdTotal = gcdTotal_slidecastEnd;

                foreach (var (ogcd, (anlock, iscast)) in ogcds) {
                    var isClipping = CheckClip(iscast, ogcd, anlock, gcdTotal, gcdTime);
                    float ogcdStart = (conf.BarRollGCDs && gcdTotal - ogcd < 0.2f) ? 0 + barGCDClipTime : ogcd;
                    float ogcdEnd = ogcdStart + anlock;

                    // Ends next GCD
                    if (conf.BarRollGCDs && ogcdEnd > gcdTotal) {
                        ogcdEnd = gcdTotal;
                        barGCDClipTime += ogcdStart + anlock - gcdTotal;
                        //prevent red bar when we "clip" a hard-cast ability
                        if (!isHardCast) {
                            // create end vertex
                            Vector2 clipEndVector = new(
                                (int)(ui.w_cent.X + ((barGCDClipTime / gcdTotal) * barWidth) - halfBarWidth),
                                (int)(ui.w_cent.Y + halfBarHeight)
                            );
                            // Draw the clipped part at the beginning
                            ui.DrawRectFilledNoAA(start, clipEndVector, conf.clipCol);
                            //ui.DrawBar(0, barGCDClipTime / gcdTotal, barWidth, barHeight, conf.clipCol);
                        }
                    }

                    Vector2 oGCDStartVector = new(
                        (int)(ui.w_cent.X + ((ogcdStart / gcdTotal) * barWidth) - (barWidth / 2)),
                        (int)(ui.w_cent.Y - (barHeight / 2))
                    );
                    Vector2 oGCDEndVector = new(
                        (int)(ui.w_cent.X + ((ogcdEnd / gcdTotal) * barWidth) - halfBarWidth),
                        (int)(ui.w_cent.Y + ((barHeight) / 2))
                    );

                    ui.DrawRectFilledNoAA(oGCDStartVector, oGCDEndVector, isClipping ? conf.clipCol : conf.anLockCol);
                    //ui.DrawBar(ogcdStart / gcdTotal, ogcdEnd / gcdTotal, barWidth, barHeight, isClipping ? conf.clipCol : conf.anLockCol);
                    if (!iscast && (!isClipping || ogcdStart > 0.01f)) {
                        Vector2 clipPos = new(
                            ui.w_cent.X + (ogcdStart / gcdTotal * barWidth) - (barWidth / 2),
                            ui.w_cent.Y - (barHeight / 2) + 1f
                        );
                        ui.DrawRectFilled(clipPos,
                            clipPos + new Vector2(2f * ui.Scale, barHeight - 2f),
                            conf.ogcdCol);
                    }
                }
            }

            ui.DrawDebugText((conf.BarWidthRatio + 1) / 2.1f, -2f, conf.ClipTextSize, conf.ClipTextColor, conf.ClipBackColor, barWidth.ToString() 
                + " " + barHeight.ToString() + " " + start.X.ToString() + " " + end.X.ToString() + " " + start.Y.ToString() + " " + end.Y.ToString() + " " + 
                (end.X - start.X).ToString() + " " + (end.Y - start.Y).ToString());


            //in both modes:
            //draw the queuelock (if enabled)
            if (conf.QueueLockEnabled && !(!isShortCast && isCastBar)) {

                float queuelockStartBuffer = 0.8f;
                if (castBarCurrentPos >= 0.8f)
                    queuelockStartBuffer = castBarCurrentPos;
                    if (castBarCurrentPos >= 1f - ((float)borderSize / barWidth))
                        queuelockStartBuffer = 1f -((float)borderSize / barWidth);

                //ui.DrawBar(queuelockStartBuffer, queuelockStartBuffer + ((float)borderSize / barWidth), barWidth, barHeight, conf.BarBackColBorder);
                Vector2 queuelockStartVector_TopLeft = new(
                    (int)(ui.w_cent.X + (queuelockStartBuffer * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y - (barHeight / 2))
                    );
                Vector2 queuelockStartVector_BottomLeft = new(
                    (int)(ui.w_cent.X + (queuelockStartBuffer * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y + halfBarHeight)
                    );
                Vector2 queuelockStartVector_TopRight = new(
                    (int)(ui.w_cent.X + ((queuelockStartBuffer + ((float)borderSize / barWidth)) * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y - ((barHeight) / 2))
                    );
                Vector2 queuelockStartVector_BottomRight = new(
                    (int)(ui.w_cent.X + ((queuelockStartBuffer + ((float)borderSize / barWidth)) * barWidth) - halfBarWidth),
                    (int)(ui.w_cent.Y + halfBarHeight)
                    );
                
                // clamp the right virtex of the right triangles to the end of the bar
                // to prevent it from hanging over the edge when bar is full

                // for the love of me, i have no idea why conf.triangleSize +1 is necessary.
                // stuff of nightmares -- really.
                float rightClamp = queuelockStartVector_TopRight.X + (conf.triangleSize + 1);
                if (rightClamp >= end.X)
                    rightClamp = end.X;

                // create vertices for the top (queuelock) triangles
                Vector2 topLeft_LeftVertex = new Vector2(queuelockStartVector_TopLeft.X - conf.triangleSize, queuelockStartVector_TopLeft.Y);
                Vector2 topLeft_RightVertex = new Vector2(queuelockStartVector_TopLeft.X, queuelockStartVector_TopLeft.Y);
                Vector2 topLeft_BottomVertex = new Vector2(queuelockStartVector_TopLeft.X, queuelockStartVector_TopLeft.Y + conf.triangleSize);
                Vector2 topRight_LeftVertex = new Vector2(queuelockStartVector_TopRight.X, queuelockStartVector_TopRight.Y);
                Vector2 topRight_RightVertex = new Vector2(rightClamp, queuelockStartVector_TopRight.Y);
                Vector2 topRight_BottomVertex = new Vector2(queuelockStartVector_TopRight.X, queuelockStartVector_TopRight.Y + (conf.triangleSize + 1));
                // create vertices for the bottom (slidelock) triangles
                Vector2 bottomLeft_LeftVertex = new Vector2(queuelockStartVector_BottomLeft.X - conf.triangleSize, queuelockStartVector_BottomLeft.Y);
                Vector2 bottomLeft_RightVertex = new Vector2(queuelockStartVector_BottomLeft.X, queuelockStartVector_BottomLeft.Y);
                Vector2 bottomLeft_TopVertex = new Vector2(queuelockStartVector_BottomLeft.X, queuelockStartVector_BottomLeft.Y - conf.triangleSize);
                Vector2 bottomRight_LeftVertex = new Vector2(queuelockStartVector_BottomRight.X, queuelockStartVector_BottomRight.Y);
                Vector2 bottomRight_RightVertex = new Vector2(rightClamp, queuelockStartVector_BottomRight.Y);
                Vector2 bottomRight_TopVertex = new Vector2(queuelockStartVector_BottomRight.X, queuelockStartVector_BottomRight.Y - (conf.triangleSize + 1));
                
                // in both modes:
                // draw top (queuelock) triangles and the vertical border line
                //top left
                ui.DrawRightTriangle(topLeft_LeftVertex, topLeft_RightVertex, topLeft_BottomVertex, conf.BarBackColBorder);
                //top right
                ui.DrawRightTriangle(topRight_LeftVertex, topRight_RightVertex, topRight_BottomVertex, conf.BarBackColBorder);
                //vertical bar
                ui.DrawRectFilledNoAA(topLeft_RightVertex, bottomRight_LeftVertex, conf.BarBackColBorder);                
                // in GCDBar mode:
                // draw the bottom triangles too
                if(!isCastBar && isShortCast) {
                    //bottom left
                    ui.DrawRightTriangle(bottomLeft_LeftVertex, bottomLeft_RightVertex, bottomLeft_TopVertex, conf.BarBackColBorder);
                    //bottom right
                    ui.DrawRightTriangle(bottomRight_LeftVertex, bottomRight_RightVertex, bottomRight_TopVertex, conf.BarBackColBorder);
                }
            }

            // in both modes:
            // draw borders
            if (borderSize > 0) {
                ui.DrawRect(
                    start - new Vector2(halfBorderSize, halfBorderSize),
                    end + new Vector2(halfBorderSize, halfBorderSize),
                    conf.BarBackColBorder, borderSize);
            }
        }

        private bool CheckClip(bool iscast, float ogcd, float anlock, float gcdTotal, float gcdTime) =>
            !iscast && !isHardCast && DateTime.Now > lastGCDEnd + TimeSpan.FromMilliseconds(50)  &&
            (
                (ogcd < (gcdTotal - 0.05f) && ogcd + anlock > gcdTotal) // You will clip next GCD
                || (gcdTime < 0.001f && ogcd < 0.001f && (anlock > (lastActionCast? 0.125:0.025))) // anlock when no gcdRolling nor CastEndAnimation
            );

        private void EndCurrentGCD(float GCDtime) {
            SlideGCDs(GCDtime, true);
            if (lastElapsedGCD > 0 && !isHardCast) checkClip = true;
            lastElapsedGCD = DataStore.Action->ElapsedGCD;
            lastGCDEnd = DateTime.Now;
            //I'm sure there's a better way to accomplish this
            abcOnLastGCD = abcOnThisGCD;
            abcOnThisGCD = false;
            shortCastFinished = false;
        }

        public void UpdateAnlock(float oldLock, float newLock) {
            if (oldLock == newLock) return; //Ignore autoattacks
            if (ogcds.Count == 0) return;
            if (oldLock == 0) { //End of cast
                lastActionCast = false;
                return;
            }
            var ctime = DataStore.Action->ElapsedGCD;

            var items = ogcds.Where(x => x.Key <= ctime && ctime < x.Key + x.Value.AnimationLock);
            if (!items.Any()) return;
            var item = items.First(); //Should always be one

            ogcds[item.Key] = new(ctime - item.Key + newLock, item.Value.IsCasted);
            var diff = newLock - oldLock;
            var toSlide = ogcds.Where(x => x.Key > ctime).ToList();
            foreach (var ogcd in toSlide)
                ogcds[ogcd.Key + diff] = ogcd.Value;
            foreach (var ogcd in toSlide)
                ogcds.Remove(ogcd.Key);
        }
    }
}
