using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace MintySpire2.MintySpire2.src;

/*
 * TODO:
 * - Disable (hide or grey out button) when it can't be used (Multiplayer)
 * - Test what happens when used in events, ancients, post combat, etc and disable where it breaks stuff
 * - Make own mod
 */

[HarmonyPatch]
public class QuickRestart
{
    [HarmonyPatch(typeof(NPauseMenu), "_Ready")]
    public class PauseMenuButtonPatch
    {
        private static String RestartButtonName = "QuickRestartButton";
        private static NPauseMenuButton _restartButton;

        [HarmonyPostfix]
        static void InitRestartButton(NPauseMenu __instance, Godot.Control ____buttonContainer, NPauseMenuButton ____settingsButton, NPauseMenuButton ____giveUpButton)
        {
            if (_restartButton == null || !GodotObject.IsInstanceValid(_restartButton))
            {
                CreateRestartButton(____buttonContainer, ____settingsButton, ____giveUpButton);
            }
            else
            {
                Log.Debug("[Restart] Reusing previously created button in pause menu");
            }
        }
        
        private static void OnPressed()
        {
            RestartRoom();
        }

        private static void CreateRestartButton(Control btnContainer, NPauseMenuButton settingsBtn, NPauseMenuButton giveUpBtn)
        {
            Log.Debug("[Restart] Creating a new save button");
            try
            {
                _restartButton = (NPauseMenuButton)settingsBtn.Duplicate();
                _restartButton.Name = RestartButtonName;

                // Duplicate the shader material on ButtonImage so hover isn't shared
                var image = _restartButton.GetNode<TextureRect>("ButtonImage");
                image.Material = (ShaderMaterial)image.Material.Duplicate();
                
                // Update the internal _hsv reference to point to the new material
                _restartButton._hsv = (ShaderMaterial)image.Material;
                
                // Add button above the give up button
                btnContainer.AddChild(_restartButton);
                var giveupIndex = giveUpBtn.GetIndex();
                btnContainer.MoveChild(_restartButton, giveupIndex);

                _restartButton.GetNode<MegaLabel>("Label").SetTextAutoSize("Restart Room");
                _restartButton.Enable();
                _restartButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnPressed()));
            }
            catch (Exception e)
            {
                Log.Error($"[Restart] Ran into error during restart button creation: \n{e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Restarts the current room in single player. Restarting works by mimicking exiting the run and then loading it.
    /// </summary>
    public static void RestartRoom()
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
        {
            Log.Error("[Restart] Not in singleplayer!!! How did this get called");
            return;
        }

        if (!SaveManager.Instance.HasRunSave)
        {
            Log.Error("[Restart] We don't have a run save, aborting");
            return;
        }

        // Cleaning up the current room
        RunManager.Instance.ActionQueueSet.Reset();
        NRunMusicController.Instance.StopMusic();
        RunManager.Instance.CleanUp();

        Log.Info("[Restart] Cleaned up, starting load now");

        // Loads run data
        ReadSaveResult<SerializableRun> runSave = SaveManager.Instance.LoadRunSave();
        SerializableRun serializableRun = runSave.SaveData;
        RunState runState = RunState.FromSerializable(serializableRun);

        Log.Info("Managed to load run data");

        // Make use of run data to reload current run
        RunManager.Instance.SetUpSavedSinglePlayer(runState, serializableRun);
        Log.Info($"Continuing run with character: {serializableRun.Players[0].CharacterId}");
        SfxCmd.Play(runState.Players[0].Character.CharacterTransitionSfx);
        NGame.Instance.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
        TaskHelper.RunSafely(NGame.Instance.LoadRun(runState, serializableRun.PreFinishedRoom));
    }
}