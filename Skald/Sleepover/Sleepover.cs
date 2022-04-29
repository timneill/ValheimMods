using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace Sleepover
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SleepoverMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.sleepover";
        const string PLUGIN_NAME = "Sleepover";
        const string PLUGIN_VERSION = "1.0.2";
        const string ALT_FUNC_KEY = "left shift";

        private readonly Harmony harmony = new Harmony(PLUGIN_ID);

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> enableMultipleBedfellows;
        private static ConfigEntry<bool> sleepAnyTime;
        private static ConfigEntry<bool> ignoreExposure;
        private static ConfigEntry<bool> ignoreEnemies;
        private static ConfigEntry<bool> ignoreFire;
        private static ConfigEntry<bool> ignoreWet;
        private static ConfigEntry<bool> sleepWithoutSpawnpoint;
        private static ConfigEntry<bool> sleepWithoutClaiming;

        private static SleepoverMod self;
        private static Assembly assembly;

        private static readonly bool debugThis = true;

        internal void Awake()
        {
            self = this;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            enableMultipleBedfellows = Config.Bind<bool>("General", "Multiple bedfellows", true, "Allow multiple people to use this bed simultaneously.");
            sleepAnyTime = Config.Bind<bool>("General", "Ignore time restrictions", true, "Sleep at any time of day, not just at night.");
            ignoreExposure = Config.Bind<bool>("General", "Ignore exposure restrictions", true, "Ignore restrictions for walls and a roof. Sleep under a starry sky.");
            ignoreEnemies = Config.Bind<bool>("General", "Ignore nearby enemies", true, "Enemies no longer prevent you from sleeping.");
            ignoreFire = Config.Bind<bool>("General", "Ignore fire requirement", true, "Sleep without a nearby fire.");
            ignoreWet = Config.Bind<bool>("General", "Ignore wet restrictions", true, "Sleep while wet.");
            sleepWithoutSpawnpoint = Config.Bind<bool>("General", "Do not set spawnpoint", true, "Sleeping in a bed will not automatically set a spawn point.");
            sleepWithoutClaiming = Config.Bind<bool>("General", "Do not automatically claim beds", true, "Sleep without claiming a bed first.");

            if (!modEnabled.Value)
            {
                return;
            }

            dbg("Initializing Sleepover...");

            assembly = typeof(SleepoverMod).Assembly;
            harmony.PatchAll(assembly);

            dbg("Sleepover initialized.");
        }

        void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        private static void dbg(string val)
        {
            if (debugThis)
            {
                self.Logger.Log(LogLevel.Info, val);
            }  
        }

        [HarmonyPatch(typeof(Game))]
        public static class GamePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(Game.UpdateSleeping))]
            public static bool UpdateSleeping(ref Game __instance)
            {
                // Only patch game code if we are worrying about sleep time
                if (!modEnabled.Value || !sleepAnyTime.Value)
                {
                    return true;
                }

                if (!ZNet.instance.IsServer())
                {
                    return false;
                }
                if (__instance.m_sleeping)
                {
                    if (!EnvMan.instance.IsTimeSkipping())
                    {
                        __instance.m_sleeping = false;
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop", Array.Empty<object>());
                        return false;
                    }
                }
                else if (!EnvMan.instance.IsTimeSkipping())
                {
                    if (!__instance.EverybodyIsTryingToSleep())
                    {
                        return false;
                    }
                    EnvMan.instance.SkipToMorning();
                    __instance.m_sleeping = true;
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStart", Array.Empty<object>());
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Bed))]
        public static class BedPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.GetHoverText))]
            public static bool GetHoverText(Bed __instance, ref string __result)
            {
                // No special logic, so defer to normal execution
                if (!modEnabled.Value && !enableMultipleBedfellows.Value && !sleepWithoutSpawnpoint.Value)
                {
                    return true;
                }

                // @todo Better hook and not replace entire method
                string ownerName = __instance.GetOwnerName();
                if (ownerName == "")
                {
                    string claimText = "$piece_bed_unclaimed\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_claim";

                    if (sleepWithoutClaiming.Value)
                    {
                        claimText += "\n[<color=yellow><b>left shift + $KEY_Use</b></color>] $piece_bed_sleep";
                    }

                    __result = Localization.instance.Localize(claimText);

                    return false;
                }

                string ownerText = ownerName + "'s $piece_bed";

                if (!__instance.IsMine())
                {
                    if (enableMultipleBedfellows.Value)
                    {
                        __result = Localization.instance.Localize(ownerText + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_sleep");
                    } else
                    {
                        __result = Localization.instance.Localize(ownerText);
                    }

                    return false;
                }

                if (sleepWithoutSpawnpoint.Value)
                {
                    __result = Localization.instance.Localize(ownerText + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_sleep");
                    return false;
                }

                if (!__instance.IsCurrent())
                {
                    __result = Localization.instance.Localize(ownerText + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_setspawn");
                }

                return false;
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static bool Interact(Bed __instance, ref bool __result, ref Humanoid human, ref bool repeat)
            {
                if (!modEnabled.Value) {
                    return true;
                }

                // No special logic, so defer to normal execution
                if (!enableMultipleBedfellows.Value && !sleepWithoutSpawnpoint.Value && !sleepAnyTime.Value && !sleepWithoutClaiming.Value)
                {
                    return true;
                }

                if (repeat)
                {
                    return false;
                }

                long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
                bool owner = __instance.GetOwner() != 0L;
                bool altFunc = Input.GetKey(ALT_FUNC_KEY);
                Player thePlayer = human as Player;

                // If there is no owner at all
                if (!owner)
                {
                    if (!__instance.CheckExposure(thePlayer))
                    {
                        __result = false;
                        return false;
                    }

                    if (!altFunc)
                    {
                        __instance.SetOwner(playerID, Game.instance.GetPlayerProfile().GetName());
                        __result = false;
                        return false;
                    }

                    if (!sleepWithoutSpawnpoint.Value)
                    {
                        Game.instance.GetPlayerProfile().SetCustomSpawnPoint(__instance.GetSpawnPoint());
                        human.Message(MessageHud.MessageType.Center, "$msg_spawnpointset", 0, null);
                    }

                    if (!altFunc && sleepWithoutClaiming.Value && sleepWithoutSpawnpoint.Value)
                    {
                        __result = false;
                        return false;
                    }
                }
                
                // If the bed belongs to the current player
                if (__instance.IsMine() || enableMultipleBedfellows.Value)
                {
                    if (__instance.IsCurrent() || sleepWithoutSpawnpoint.Value)
                    {
                        if (!sleepAnyTime.Value && !EnvMan.instance.IsAfternoon() && !EnvMan.instance.IsNight())
                        {
                            human.Message(MessageHud.MessageType.Center, "$msg_cantsleep", 0, null);
                            __result = false;
                            return false;
                        }
                        if (!__instance.CheckEnemies(thePlayer))
                        {
                            __result = false;
                            return false;
                        }
                        if (!__instance.CheckExposure(thePlayer))
                        {
                            __result = false;
                            return false;
                        }
                        if (!__instance.CheckFire(thePlayer))
                        {
                            __result = false;
                            return false;
                        }
                        if (!__instance.CheckWet(thePlayer))
                        {
                            __result = false;
                            return false;
                        }

                        human.AttachStart(__instance.m_spawnPoint, human.gameObject, true, true, false, "attach_bed", new Vector3(0f, 0.5f, 0f));
                        __result = false;

                        return false;
                    }
                    else
                    {
                        if (!__instance.CheckExposure(thePlayer))
                        {
                            __result = false;
                            return false;
                        }

                        if (!sleepWithoutSpawnpoint.Value)
                        {
                            Game.instance.GetPlayerProfile().SetCustomSpawnPoint(__instance.GetSpawnPoint());
                            human.Message(MessageHud.MessageType.Center, "$msg_spawnpointset", 0, null);
                        }
                    }
                }

                __result = false;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.CheckExposure))]
            public static bool CheckExposure(ref bool __result)
            {
                if (ignoreExposure.Value)
                {
                    __result = true;
                    return false;
                }

                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.CheckEnemies))]
            public static bool CheckEnemies(ref bool __result)
            {
                if (ignoreEnemies.Value)
                {
                    __result = true;
                    return false;
                }

                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.CheckFire))]
            public static bool CheckFire(ref bool __result)
            {
                if (ignoreFire.Value)
                {
                    __result = true;
                    return false;
                }

                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.CheckWet))]
            public static bool CheckWet(ref bool __result)
            {
                if (ignoreWet.Value)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
        }
    }
}
