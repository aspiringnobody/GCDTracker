using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using GCDTracker.Data;
using GCDTracker.UI;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests")]
namespace GCDTracker {
    public unsafe class GCDDisplay {
        private readonly Configuration conf;
        private readonly IDataManager dataManager;
        private readonly GCDHelper helper;
        private readonly AbilityManager abilityManager;
        string shortCastCachedSpellName;
        Vector4 bgCache;

        public GCDDisplay (Configuration conf, IDataManager dataManager, GCDHelper helper) {
            this.conf = conf;
            this.dataManager = dataManager;
            this.helper = helper;
            abilityManager = AbilityManager.Instance;
        }

        public void DrawGCDWheel(PluginUI ui) {
            float gcdTotal = helper.TotalGCD;
            float gcdTime = helper.lastElapsedGCD;

            if (GameState.IsCasting() && DataStore.Action->ElapsedCastTime >= gcdTotal && !GameState.IsCastingTeleport())
                gcdTime = gcdTotal;
            if (gcdTotal < 0.1f) return;
            helper.MiscEventChecker();
            helper.WheelCheckQueueEvent(conf, gcdTime / gcdTotal);

            var notify = GCDEventHandler.Instance;
            notify.Update(null, conf, ui);

            // Background
            ui.DrawCircSegment(0f, 1f, 6f * notify.WheelScale, conf.backColBorder);
            ui.DrawCircSegment(0f, 1f, 3f * notify.WheelScale, helper.BackgroundColor());
            if (conf.QueueLockEnabled) {
                ui.DrawCircSegment(0.8f, 1, 9f * notify.WheelScale, conf.backColBorder);
                ui.DrawCircSegment(0.8f, 1, 6f * notify.WheelScale, helper.BackgroundColor());
            }
            ui.DrawCircSegment(0f, Math.Min(gcdTime / gcdTotal, 1f), 20f * notify.WheelScale, conf.frontCol);
            foreach (var (ogcd, (anlock, iscast)) in abilityManager.ogcds) {
                var isClipping = helper.CheckClip(iscast, ogcd, anlock, gcdTotal, gcdTime);
                ui.DrawCircSegment(ogcd / gcdTotal, (ogcd + anlock) / gcdTotal, 21f * notify.WheelScale, isClipping ? conf.clipCol : conf.anLockCol);
                if (!iscast) ui.DrawCircSegment(ogcd / gcdTotal, (ogcd + 0.04f) / gcdTotal, 23f * notify.WheelScale, conf.ogcdCol);
            }
        }

        public void DrawGCDBar(PluginUI ui) {
            float gcdTotal = helper.TotalGCD;
            float gcdTime = helper.lastElapsedGCD;

            if (GameState.IsCasting() && DataStore.Action->ElapsedCastTime >= gcdTotal && !GameState.IsCastingTeleport())
                gcdTime = gcdTotal;
            if (gcdTotal < 0.1f) return;
            
            if (!conf.WheelEnabled)
                helper.MiscEventChecker();

            DrawBarElements(
                ui,
                false,
                helper.shortCastFinished,
                false,
                gcdTime / gcdTotal,
                gcdTime,
                gcdTotal, gcdTotal
            );

            // Gonna re-do this, but for now, we flag when we need to carryover from the castbar to the GCDBar
            // and dump all the crap here to draw on top. 
            if (helper.shortCastFinished) {
                string abilityNameOutput = shortCastCachedSpellName;
                if (!string.IsNullOrWhiteSpace(helper.queuedAbilityName) && conf.CastBarShowQueuedSpell)
                    abilityNameOutput += " -> " + helper.queuedAbilityName;
                if (!string.IsNullOrEmpty(abilityNameOutput))
                    DrawBarText(ui, abilityNameOutput);
            }
            if (conf.ShowQueuedSpellNameGCD && !helper.shortCastFinished) {
                if (gcdTime / gcdTotal < 0.8f)
                    helper.queuedAbilityName = " ";
                if (!string.IsNullOrWhiteSpace(helper.queuedAbilityName))
                    DrawBarText(ui, " -> " + helper.queuedAbilityName);
            }
        }

        public void DrawCastBar (PluginUI ui) {
            float gcdTotal = DataStore.Action->TotalGCD;
            float castTotal = DataStore.Action->TotalCastTime;
            float castElapsed = DataStore.Action->ElapsedCastTime;
            float castbarProgress = castElapsed / castTotal;
            float castbarEnd = 1f;
            float slidecastStart = Math.Max((castTotal - conf.SlidecastDelay) / castTotal, 0f);
            float slidecastEnd = castbarEnd;
            bool isTeleport = GameState.IsCastingTeleport();
            // handle short casts
            if (gcdTotal > castTotal) {
                castbarEnd = GameState.CastingNonAbility() ? 1f : castTotal / gcdTotal;
                slidecastStart = Math.Max((castTotal - conf.SlidecastDelay) / gcdTotal, 0f);
                slidecastEnd = conf.SlideCastFullBar ? 1f : castbarEnd;
            }

            DrawBarElements(
                ui,
                true,
                gcdTotal > castTotal,
                // Maybe we don't need the gcdTotal < 0.01f anymore?
                GameState.CastingNonAbility() || isTeleport || gcdTotal < 0.01f,
                castbarProgress * castbarEnd,
                slidecastStart,
                slidecastEnd,
                castbarEnd
            );

            var castName = GameState.GetCastbarContents();
            if (!string.IsNullOrEmpty(castName)) {
                if (castbarEnd - castbarProgress <= 0.01f && gcdTotal > castTotal) {
                    helper.shortCastFinished = true;
                    shortCastCachedSpellName = castName;
                }
                string abilityNameOutput = castName;
                if (conf.castTimePosition == 0 && conf.CastTimeEnabled)
                    abilityNameOutput += " (" + helper.remainingCastTimeString + ")";
                if (helper.queuedAbilityName != " " && conf.CastBarShowQueuedSpell)
                    abilityNameOutput += " -> " + helper.queuedAbilityName;
                    
                DrawBarText(ui, abilityNameOutput);
            }
        }

        private void DrawBarElements(
            PluginUI ui, 
            bool isCastBar, 
            bool isShortCast,
            bool isNonAbility,
            float castBarCurrentPos, 
            float gcdTime_slidecastStart, 
            float gcdTotal_slidecastEnd,
            float totalBarTime) {
            
            var bar = BarInfo.Instance;
            bar.Update(
                conf,
                ui.w_size.X,
                ui.w_cent.X,
                ui.w_size.Y,
                ui.w_cent.Y,
                castBarCurrentPos,
                gcdTime_slidecastStart, 
                gcdTotal_slidecastEnd,
                totalBarTime,
                conf.triangleSize,
                isCastBar, 
                isShortCast,
                isNonAbility
            );

            var go = BarDecisionHelper.Instance;
            go.Update(
                bar, 
                conf, 
                helper, 
                DataStore.ActionManager->CastActionType, 
                DataStore.ClientState?.LocalPlayer?.TargetObject?.ObjectKind ?? ObjectKind.None
            );

            var notify = GCDEventHandler.Instance;
            notify.Update(bar, conf, ui);

            var bar_v = BarVertices.Instance;
            bar_v.Update(bar, go, notify);
            var sc_sv = SlideCastStartVertices.Instance;
            sc_sv.Update(bar, bar_v, go);
            var sc_ev = SlideCastEndVertices.Instance;
            sc_ev.Update(bar, bar_v, go);
            var ql_v = QueueLockVertices.Instance;
            ql_v.Update (bar, bar_v, go); 

            float barGCDClipTime = 0;
            
            // in both modes:
            // draw the background
            if (bar.CurrentPos < 0.2f)
                bgCache = helper.BackgroundColor();
            ui.DrawRectFilledNoAA(bar_v.StartVertex, bar_v.EndVertex, bgCache, conf.BarBgGradMode, conf.BarBgGradientMul);

            // in both modes:
            // draw cast/gcd progress (main) bar
            if(bar.CurrentPos > 0.001f){
                var progressBarColor = notify.ProgressPulseColor;
                ui.DrawRectFilledNoAA(bar_v.StartVertex, bar_v.ProgressVertex, progressBarColor, conf.BarGradMode, conf.BarGradientMul);
            }
            // in Castbar mode:
            // draw the slidecast bar
            if (conf.SlideCastEnabled)
                DrawSlideCast(ui, sc_sv, sc_ev, go);

            // in GCDBar mode:
            // draw oGCDs and clips
            if (!isCastBar) {
                float gcdTime = gcdTime_slidecastStart;
                float gcdTotal = gcdTotal_slidecastEnd;

                foreach (var (ogcd, (anlock, iscast)) in abilityManager.ogcds) {
                    var isClipping = helper.CheckClip(iscast, ogcd, anlock, gcdTotal, gcdTime);
                    float ogcdStart = (conf.BarRollGCDs && gcdTotal - ogcd < 0.2f) ? 0 + barGCDClipTime : ogcd;
                    float ogcdEnd = ogcdStart + anlock;

                    // Ends next GCD
                    if (conf.BarRollGCDs && ogcdEnd > gcdTotal) {
                        ogcdEnd = gcdTotal;
                        barGCDClipTime += ogcdStart + anlock - gcdTotal;
                        //prevent red bar when we "clip" a hard-cast ability
                        if (!helper.IsHardCast) {
                            // create end vertex
                            Vector2 clipEndVector = new(
                                (int)(bar.CenterX + ((barGCDClipTime / gcdTotal) * bar_v.Width) - bar_v.HalfWidth),
                                (int)(bar.CenterY + bar_v.HalfHeight)
                            );
                            // Draw the clipped part at the beginning
                            ui.DrawRectFilledNoAA(bar_v.StartVertex, clipEndVector, conf.clipCol);
                        }
                    }

                    Vector2 oGCDStartVector = new(
                        (int)(bar.CenterX + ((ogcdStart / gcdTotal) * bar_v.Width) - bar_v.RawHalfWidth),
                        (int)(bar.CenterY - bar_v.RawHalfHeight)
                    );
                    Vector2 oGCDEndVector = new(
                        (int)(bar.CenterX + ((ogcdEnd / gcdTotal) * bar_v.Width) - bar_v.HalfWidth),
                        (int)(bar.CenterY + bar_v.HalfHeight)
                    );

                    if(!helper.shortCastFinished || isClipping) {
                        ui.DrawRectFilledNoAA(oGCDStartVector, oGCDEndVector, isClipping ? conf.clipCol : conf.anLockCol);
                        if (!iscast && (!isClipping || ogcdStart > 0.01f)) {
                            Vector2 clipPos = new(
                                bar.CenterX + (ogcdStart / gcdTotal * bar_v.Width) - bar_v.RawHalfWidth,
                                bar.CenterY - bar_v.RawHalfHeight + 1f
                            );
                            ui.DrawRectFilledNoAA(clipPos,
                                clipPos + new Vector2(2f * ui.Scale, bar_v.Height - 2f),
                                conf.ogcdCol);
                    }
                    }
                }
            }

            //in both modes:
            //draw the queuelock (if enabled)
            if (conf.QueueLockEnabled)
                DrawQueueLock(ui, ql_v, go);

            // in both modes:
            // draw borders
            if (bar.BorderSize > 0) {
                ui.DrawRect(
                    bar_v.StartVertex - new Vector2(bar.HalfBorderSize, bar.HalfBorderSize),
                    bar_v.EndVertex + new Vector2(bar.HalfBorderSize, bar.HalfBorderSize),
                    conf.backColBorder, bar.BorderSize);
            }
        }

        private void DrawBarText(PluginUI ui, string abilityName){
            int barWidth = (int)(ui.w_size.X * conf.BarWidthRatio);
            string combinedText = abilityName + helper.remainingCastTimeString + "!)/|";
            Vector2 spellNamePos = new(ui.w_cent.X - ((float)barWidth / 2.05f), ui.w_cent.Y);
            Vector2 spellTimePos = new(ui.w_cent.X + ((float)barWidth / 2.05f), ui.w_cent.Y);

            if (conf.EnableCastText) {
                if (!string.IsNullOrEmpty(abilityName))
                    ui.DrawCastBarText(abilityName, combinedText, spellNamePos, conf.CastBarTextSize, false);
                if (!string.IsNullOrEmpty(helper.remainingCastTimeString) && conf.castTimePosition == 1 && conf.CastTimeEnabled)
                    ui.DrawCastBarText(helper.remainingCastTimeString, combinedText, spellTimePos, conf.CastBarTextSize, true);
            }
        }

        private void DrawSlideCast(PluginUI ui, SlideCastStartVertices sc_sv, SlideCastEndVertices sc_ev, BarDecisionHelper go){
            // draw slidecast bar
            if (go.Slide_Background)
                ui.DrawRectFilledNoAA(sc_sv.TL_C, sc_ev.BR_C, conf.slideCol);
            // draw sidecast (start) vertical line
            if (go.SlideStart_VerticalBar)
                ui.DrawRectFilledNoAA(sc_sv.TL_C, sc_sv.BR_C, conf.backColBorder);
            //draw sidlecast (end) vertical line
            if (go.SlideEnd_VerticalBar)
                ui.DrawRectFilledNoAA(sc_ev.TL_C, sc_ev.BR_C, conf.backColBorder);
            //bottom left
            if (go.SlideStart_LeftTri)
                ui.DrawRightTriangle(sc_sv.BL_C, sc_sv.BL_X, sc_sv.BL_Y, conf.backColBorder);
            //bottom right
            if (go.SlideStart_RightTri)
                ui.DrawRightTriangle(sc_sv.BR_C, sc_sv.BR_X, sc_sv.BR_Y, conf.backColBorder);
            //end right
            if (go.SlideEnd_RightTri)
                ui.DrawRightTriangle(sc_ev.BR_C, sc_ev.BR_X, sc_ev.BR_Y, conf.backColBorder);
        }

        private void DrawQueueLock(PluginUI ui, QueueLockVertices ql_v, BarDecisionHelper go) {
            //queue vertical bar
            if (go.Queue_VerticalBar)
                ui.DrawRectFilledNoAA(ql_v.TL_C, ql_v.BR_C, conf.backColBorder); 
            //queue triangle
            if (go.Queue_Triangle) {
                ui.DrawRightTriangle(ql_v.TL_C, ql_v.TL_X, ql_v.TL_Y, conf.backColBorder);
                ui.DrawRightTriangle(ql_v.TR_C, ql_v.TR_X, ql_v.TR_Y, conf.backColBorder);
            }
        }

        public void DrawFloatingTriangles(PluginUI ui) {
            float gcdTotal = DataStore.Action->TotalGCD;
            float gcdElapsed = DataStore.Action->ElapsedGCD;
            float gcdPercent = gcdElapsed / gcdTotal;
            float castTotal = DataStore.Action->TotalCastTime;
            float castElapsed = DataStore.Action->ElapsedCastTime;
            float castPercent = castElapsed / castTotal;
            float slidecastStart = (castTotal - 0.5f) / castTotal;
            int triangleSize = (int)Math.Min(ui.w_size.X / 3, ui.w_size.Y / 3);
            int borderSize = triangleSize / 6;
            Vector4 red = new(1f, 0f, 0f, 1f);
            Vector4 green = new(0f, 1f, 0f, 1f);
            Vector4 bgCol = new(0f, 0f, 0f, .6f);

            // slidecast
            Vector2 slideTop = new(ui.w_cent.X, ui.w_cent.Y - triangleSize - borderSize);
            Vector2 slideLeft = slideTop + new Vector2(-triangleSize, triangleSize);
            Vector2 slideRight = slideTop + new Vector2(triangleSize, triangleSize);
            // queuelock
            Vector2 queueBot = new(slideTop.X,  ui.w_cent.Y + triangleSize + borderSize);
            Vector2 queueRight = queueBot - new Vector2(-triangleSize, triangleSize);
            Vector2 queueLeft = queueBot - new Vector2(triangleSize, triangleSize);

            Vector2 slideBGTop = slideTop - new Vector2(0f, borderSize);
            Vector2 slideBGLeft = slideLeft - new Vector2(1.75f * borderSize, - borderSize / 1.5f);
            Vector2 slideBGRight = slideRight + new Vector2(1.75f * borderSize, borderSize / 1.5f);

            // queuelock background
            Vector2 queueBGBot = queueBot + new Vector2(0f, borderSize);
            Vector2 queueBGRight = queueRight + new Vector2(1.75f * borderSize, - borderSize / 1.5f);
            Vector2 queueBGLeft = queueLeft - new Vector2(1.75f * borderSize, borderSize / 1.5f);

            bool cantSlide = castPercent != 0 && castPercent < slidecastStart;
            bool cantQueue = gcdPercent != 0 && gcdPercent < 0.8f;

            if (conf.SlidecastTriangleEnable && !(conf.OnlyGreenTriangles && cantSlide)) {
                ui.DrawRightTriangle(slideBGTop, slideBGLeft, slideBGRight, bgCol);
                ui.DrawRightTriangle(slideTop, slideLeft, slideRight, cantSlide ? red : green);
            }
            if (conf.QueuelockTriangleEnable && !(conf.OnlyGreenTriangles && cantQueue)) {
                ui.DrawRightTriangle(queueBGBot, queueBGRight, queueBGLeft, bgCol);
                ui.DrawRightTriangle(queueBot, queueRight, queueLeft, cantQueue ? red : green);
            }
        }
    }
}