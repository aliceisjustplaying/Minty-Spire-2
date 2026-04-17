using System;
using System.Runtime.InteropServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;

namespace MintySpire2;

[HarmonyPatch]
public static class BackgroundExecution
{
    private const ulong NSActivityUserInitiated = 0x00FFFFFFUL | NSActivityIdleSystemSleepDisabled;
    private const ulong NSActivityIdleSystemSleepDisabled = 1UL << 20;
    private const ulong NSActivityLatencyCritical = 0xFF00000000UL;
    private static bool _initialized;
    private static nint _macActivity;

    [HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
    [HarmonyPostfix]
    public static void InitializeBackgroundExecution()
    {
        if (_initialized)
            return;

        _initialized = true;
        DisableLowProcessorMode();
        DisableGameBackgroundSettings();
        BeginMacActivity();
    }

    [HarmonyPatch(typeof(NBackgroundModeHandler), "EnterBackgroundMode")]
    [HarmonyPrefix]
    public static bool SkipEnterBackgroundMode() => false;

    [HarmonyPatch(typeof(NBackgroundModeHandler), "ExitBackgroundMode")]
    [HarmonyPrefix]
    public static bool SkipExitBackgroundMode() => false;

    [HarmonyPatch(typeof(NMuteInBackgroundHandler), "Mute")]
    [HarmonyPrefix]
    public static bool SkipMuteInBackground() => false;

    [HarmonyPatch(typeof(NMuteInBackgroundHandler), "Unmute")]
    [HarmonyPrefix]
    public static bool SkipUnmuteInBackground() => false;

    private static void DisableLowProcessorMode()
    {
        if (!OS.LowProcessorUsageMode)
            return;

        OS.LowProcessorUsageMode = false;
        MainFile.Logger.Info("Disabled Godot low processor usage mode.");
    }

    private static void DisableGameBackgroundSettings()
    {
        SaveManager? saveManager = SaveManager.Instance;
        if (saveManager?.SettingsSave is SettingsSave settingsSave && settingsSave.LimitFpsInBackground)
        {
            settingsSave.LimitFpsInBackground = false;
            MainFile.Logger.Info("Disabled the game's background FPS limiter.");
        }

        if (saveManager?.PrefsSave is PrefsSave prefsSave && prefsSave.MuteInBackground)
        {
            prefsSave.MuteInBackground = false;
            MainFile.Logger.Info("Disabled the game's background mute setting.");
        }
    }

    private static void BeginMacActivity()
    {
        if (!OperatingSystem.IsMacOS() || _macActivity != nint.Zero)
            return;

        try
        {
            var nsProcessInfoClass = objc_getClass("NSProcessInfo");
            var nsStringClass = objc_getClass("NSString");
            if (nsProcessInfoClass == nint.Zero || nsStringClass == nint.Zero)
                return;

            var processInfo = IntPtr_objc_msgSend(nsProcessInfoClass, sel_registerName("processInfo"));
            var reason = IntPtr_objc_msgSend_string(nsStringClass, sel_registerName("stringWithUTF8String:"), "Minty Spire background execution");
            if (processInfo == nint.Zero || reason == nint.Zero)
                return;

            _macActivity = IntPtr_objc_msgSend_ulong_IntPtr(
                processInfo,
                sel_registerName("beginActivityWithOptions:reason:"),
                NSActivityUserInitiated | NSActivityLatencyCritical,
                reason);

            if (_macActivity != nint.Zero)
                MainFile.Logger.Info("Registered a macOS activity to suppress App Nap.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to suppress App Nap: {ex}");
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint IntPtr_objc_msgSend(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint IntPtr_objc_msgSend_string(nint receiver, nint selector, string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint IntPtr_objc_msgSend_ulong_IntPtr(nint receiver, nint selector, ulong value, nint argument);
}
