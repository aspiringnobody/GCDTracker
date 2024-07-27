using Dalamud.Game;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using GCDTracker.Data;
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
        private Vector2 slideCast;
        private Vector2 spellNamePos;
        private Vector2 spellTimePos;
        private Vector4 bgCache;
        private float slidecastStart;
        private float slidecastEnd;
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
                //if (acceptQueuedSpellName)
                    queuedAbilityName = GetAbilityName(actionID, DataStore.ClientState.LocalPlayer.CastActionType);
            } else {
                if (isWeaponSkill) {
                    EndCurrentGCD(TotalGCD);
                    //Store GCD in a variable in order to cache it when it goes back to 0
                    TotalGCD = act->TotalGCD;
                    AddWeaponSkill(act);
                    //if (acceptQueuedSpellName)
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
                    return "... " + actionType.ToString();
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
            float barHeight = ui.w_size.Y * conf.BarHeightRatio;
            float barWidth = ui.w_size.X * conf.BarWidthRatio;
            int borderSize = (int)conf.BarBorderSize;
            float barGCDClipTime = 0;
            Vector2 start = new(ui.w_cent.X - (barWidth / 2), ui.w_cent.Y - (barHeight / 2));
            Vector2 end = new(ui.w_cent.X + (barWidth / 2), ui.w_cent.Y + (barHeight / 2));
            if (conf.ShowOnlyGCDRunning && HelperMethods.IsTeleport(DataStore.Action->CastId)) {
                lastActionTP = true;
                return;
            }
            if (HelperMethods.IsCasting() && DataStore.Action->ElapsedCastTime >= gcdTotal && !HelperMethods.IsTeleport(DataStore.Action->CastId))
                gcdTime = gcdTotal;
            if (gcdTotal < 0.1f) return;
            FlagAlerts(ui);
            InvokeAlerts((conf.BarWidthRatio + 1) / 2.1f, -0.3f, ui);
            // Background
            ui.DrawBar(0f, 1f, barWidth, barHeight, BackgroundColor());

            //draw slidecast if we are transitioning from castbar where GCDTime > CastTime 
            if (shortCastFinished && conf.SlideCastEnabled && conf.CastBarEnabled && conf.SlideCastFullBar)
                ui.DrawBar(slidecastStart, slidecastEnd, barWidth, barHeight, conf.slideCol);

            ui.DrawBar(0f, Math.Min(gcdTime / gcdTotal, 1f), barWidth, barHeight, conf.frontCol);

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
                        // Draw the clipped part at the beginning
                        ui.DrawBar(0, barGCDClipTime / gcdTotal, barWidth, barHeight, conf.clipCol);
                    }
                }
                ui.DrawBar(ogcdStart / gcdTotal, ogcdEnd / gcdTotal, barWidth, barHeight, isClipping ? conf.clipCol : conf.anLockCol);
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

            if (conf.QueueLockEnabled) {
                Vector2 queueLock = new(
                    ui.w_cent.X + (0.8f * barWidth) - (barWidth / 2),
                    ui.w_cent.Y - (barHeight / 2) - (borderSize / 2)
                );
                ui.DrawRectFilled(queueLock,
                    queueLock + new Vector2(borderSize, barHeight + (borderSize / 2)),
                    conf.BarBackColBorder);
            }
            // Borders last so they're on top of all elements
            if (borderSize > 0) {
                ui.DrawRect(
                    start - (new Vector2(borderSize, borderSize) / 2),
                    end + (new Vector2(borderSize, borderSize) / 2),
                    conf.BarBackColBorder, borderSize);
            }
            //Gonna re-do this, but for now, we flag when we need to carryover from the castbar to the GCDBar
            //and dump all the crap here to draw on top. 
            if (shortCastFinished) {
                string abilityNameOutput = shortCastCachedSpellName;
                if (queuedAbilityName != " ")
                    abilityNameOutput = shortCastCachedSpellName + " -> " + queuedAbilityName;
                ui.DrawHardCastAbilityName(abilityNameOutput, spellNamePos);
                ui.DrawRectFilled(slideCast,
                    slideCast + new Vector2(borderSize, barHeight + (borderSize / 2)), conf.BarBackColBorder);
            }

        }

        public void DrawCastBar (PluginUI ui) {
            float gcdTotal = DataStore.Action->TotalGCD;
            float castTotal = DataStore.Action->TotalCastTime;
            float castElapsed = DataStore.Action->ElapsedCastTime;
            float barHeight = ui.w_size.Y * conf.BarHeightRatio;
            float barWidth = ui.w_size.X * conf.BarWidthRatio;
            int borderSize = (int)conf.BarBorderSize;
            float castbarEnd = 1f;
            float castbarProgress = castElapsed / castTotal;
            float slidecastOffset = 0.5f;
            slidecastStart = Math.Max((castTotal - slidecastOffset) / castTotal, 0f);
            slidecastEnd = castbarEnd;

            
            Vector2 start = new(ui.w_cent.X - (barWidth / 2), ui.w_cent.Y - (barHeight / 2));
            Vector2 end = new(ui.w_cent.X + (barWidth / 2), ui.w_cent.Y + (barHeight / 2));

            //handle short casts
            if (gcdTotal > castTotal) {
                castbarEnd = castTotal / gcdTotal;
                slidecastStart = Math.Max((castTotal - slidecastOffset) / gcdTotal, 0f);
                slidecastEnd = conf.SlideCastFullBar ? 1f : castbarEnd;
            }

            //background
            if (castbarProgress < 0.01f)
                bgCache = BackgroundColor();
            ui.DrawBar(0f, 1f, barWidth, barHeight, bgCache);
            
            //draw the slidecast bar
            if (conf.SlideCastEnabled) {
                ui.DrawBar(slidecastStart, slidecastEnd, barWidth, barHeight, conf.slideCol);
            }

            //draw the actual castbar
            ui.DrawBar(0f, Math.Min(castbarProgress * castbarEnd, castbarEnd), barWidth, barHeight, conf.frontCol);
           
            //draw slidecast border
            if (conf.SlideCastEnabled) {
                slideCast = new(
                    ui.w_cent.X + (slidecastStart * barWidth) - (barWidth / 2),
                    ui.w_cent.Y - (barHeight / 2) - (borderSize / 2)
                );                
                ui.DrawRectFilled(slideCast,
                    slideCast + new Vector2(borderSize, barHeight + (borderSize / 2)), 
                    conf.BarBackColBorder);
            }

            //draw the queuelock
            if (conf.QueueLockEnabled && gcdTotal > castTotal) {
                Vector2 queueLock = new(
                    ui.w_cent.X + (0.8f * barWidth) - (barWidth / 2),
                    ui.w_cent.Y - (barHeight / 2) - (borderSize / 2)
                );
                ui.DrawRectFilled(queueLock,
                    queueLock + new Vector2(borderSize, barHeight + (borderSize / 2)),
                    conf.BarBackColBorder);
            }

            var abilityID = DataStore.ClientState.LocalPlayer.CastActionId;
            var actionType = DataStore.ClientState.LocalPlayer.CastActionType;
            if (!string.IsNullOrEmpty(GetAbilityName(abilityID, actionType))) {
                spellNamePos = new(
                    ui.w_cent.X - (barWidth / 2.05f),
                    ui.w_cent.Y - (barHeight / 3)
                );
                spellTimePos = new(
                    ui.w_cent.X + (barWidth / 2.05f),
                    ui.w_cent.Y - (barHeight / 3)
                );
                if (castbarProgress <= 0.01f) {
                    queuedAbilityName = " ";
                }
                if (castbarEnd - castbarProgress <= 0.01f && gcdTotal > castTotal) {
                    shortCastFinished = true;
                    shortCastCachedSpellName = GetAbilityName(abilityID, actionType);
                }
                string abilityNameOutput = GetAbilityName(abilityID, actionType);
                if (queuedAbilityName != " ")
                    abilityNameOutput = GetAbilityName(abilityID, actionType) + " -> " + queuedAbilityName;
                ui.DrawHardCastAbilityName(abilityNameOutput, spellNamePos);
                ui.DrawHardCastAbilityTime(hardCastAbilityTime, spellTimePos);
            }

            if (borderSize > 0) {
                ui.DrawRect(
                    start - (new Vector2(borderSize, borderSize) / 2),
                    end + (new Vector2(borderSize, borderSize) / 2),
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
