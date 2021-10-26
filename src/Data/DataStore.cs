﻿using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using System.Runtime.InteropServices;

namespace GCDTracker.Data
{
    static unsafe class DataStore
    {
        public static Combo* combo;
        public static Action* action;
        public static ClientState clientState;
        public static Condition condition;

        public static void Init(SigScanner scanner,ClientState cs,Condition cond)
        {
            var comboPtr = scanner.GetStaticAddressFromSig("48 89 2D ?? ?? ?? ?? 85 C0");
            var ActionManagerPtr = scanner.GetStaticAddressFromSig("E8 ?? ?? ?? ?? 33 C0 E9 ?? ?? ?? ?? 8B 7D 0C");

            combo = (Combo*)comboPtr;
            action = (Action*)ActionManagerPtr;
            clientState = cs;
            condition = cond;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct Action
    {
        [FieldOffset(0x0)] public void* ActionManager;
        [FieldOffset(0x8)] public float AnimationLock;
        [FieldOffset(0x28)] public bool IsCast;
        [FieldOffset(0x60)] public float ComboTimer;
        [FieldOffset(0x64)] public uint ComboID;
        [FieldOffset(0x68)] public bool InQueue1;
        [FieldOffset(0x68)] public bool InQueue2;
        [FieldOffset(0x70)] public uint QueuedAction;
        [FieldOffset(0x78)] public float dunno; //always 2.01 when queuing stuff
        [FieldOffset(0x618)] public float ElapsedGCD;
        [FieldOffset(0x61C)] public float TotalGCD;
        [FieldOffset(0x810)] public float AnimationTimer;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x8)]
    public struct Combo
    {
        [FieldOffset(0x00)] public float Timer;
        [FieldOffset(0x04)] public uint Action;
    }
}
