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
        const string PLUGIN_VERSION = "1.1.1";

        private readonly Harmony harmony = new Harmony(PLUGIN_ID);

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> enableMultipleBedfellows;
        private static ConfigEntry<bool> sleepAnyTime;
        private static ConfigEntry<bool> ignoreExposure;
        private static ConfigEntry<bool> ignoreEnemies;
        private static ConfigEntry<bool> ignoreFire;
        private static ConfigEntry<bool> ignoreWet;
        private static ConfigEntry<bool> sleepWithoutSpawnpoint;
        private static ConfigEntry<bool> multipleSpawnpointsPerBed;
        private static ConfigEntry<bool> sleepWithoutClaiming;

        private static SleepoverMod self;
        private static Assembly assembly;

        private static readonly bool debugThis = true;

        internal void Awake()
        {
            self = this;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            enableMultipleBedfellows = Config.Bind<bool>("General", "Multiple sleepers", true, "Allow multiple people to use this bed simultaneously. Not tested on public servers.");
            sleepAnyTime = Config.Bind<bool>("General", "Ignore time restrictions", true, "Sleep at any time of day, not just at night.");
            ignoreExposure = Config.Bind<bool>("General", "Ignore exposure restrictions", true, "Ignore restrictions for walls and a roof. Sleep under a starry sky.");
            ignoreEnemies = Config.Bind<bool>("General", "Ignore nearby enemies", true, "Enemies no longer prevent you from sleeping.");
            ignoreFire = Config.Bind<bool>("General", "Ignore fire requirement", true, "Sleep without a nearby fire.");
            ignoreWet = Config.Bind<bool>("General", "Ignore wet restrictions", true, "Sleep while wet.");
            sleepWithoutClaiming = Config.Bind<bool>("General", "Do not automatically claim beds", true, "Sleep without claiming a bed first.");
            sleepWithoutSpawnpoint = Config.Bind<bool>("General", "Do not set spawnpoint", true, "Sleep without setting a spawnpoint first.");
            multipleSpawnpointsPerBed = Config.Bind<bool>("General", "Multiple spawnpoints per bed", true, "Any number of players can use the same bed as a spawnpoint.");

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
                if (!modEnabled.Value)
                {
                    return true;
                }

                // Only patch game code if we are worrying about sleep time
                if (!sleepAnyTime.Value)
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
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStop");
                        return false;
                    }
                }
                else if (
                    !EnvMan.instance.IsTimeSkipping() &&
                    ((EnvMan.instance.IsAfternoon() || EnvMan.instance.IsNight()) || sleepAnyTime.Value) &&
                    __instance.EverybodyIsTryingToSleep()
                )
                {
                    EnvMan.instance.SkipToMorning();
                    __instance.m_sleeping = true;
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "SleepStart");
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
                if (!modEnabled.Value)
                {
                    return true;
                }
                
                string sleepHover = "[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_sleep\n";
                string claimHover = "[<color=yellow><b>shift + $KEY_Use</b></color>] $piece_bed_claim\n";
                string setSpawnHover = "[<color=yellow><b>alt + $KEY_Use</b></color>] $piece_bed_setspawn\n";
                bool maySleep;
                bool mayClaim;
                bool maySetSpawn;
                string ownerName = __instance.GetOwnerName();
                string ownerText = ownerName + "'s $piece_bed\n";

                // Sleep rules
                maySleep = (
                    (__instance.IsMine() && __instance.IsCurrent()) || // Default - it's my claimed spawn bed
                    ((!__instance.IsMine() && ownerName != "" && enableMultipleBedfellows.Value)) || // Many sleepers
                    (ownerName == "" && sleepWithoutClaiming.Value) || // Ignore claim rules
                    (!__instance.IsCurrent() && sleepWithoutSpawnpoint.Value) // Ignore spawn rules
                );

                // Claim rules
                mayClaim = (
                    (ownerName == "")
                );

                // Set spawn rules
                maySetSpawn = (
                    (!__instance.IsCurrent() && __instance.IsMine()) || // Default - it's my bed, but not currently spawn
                    (!__instance.IsCurrent() && (!__instance.IsMine() && multipleSpawnpointsPerBed.Value)) // Allow multiple spawns
                );

                __result = ownerName != "" ? ownerText : "$piece_bed_unclaimed\n";

                if (maySleep)
                {
                    __result += sleepHover;
                }

                if (mayClaim)
                {
                    __result += claimHover;
                }

                if (maySetSpawn)
                {
                    __result += setSpawnHover;
                }

                __result = Localization.instance.Localize(__result);
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.VeryHigh)]
            [HarmonyPatch(nameof(Bed.Interact))]
            public static bool Interact(Bed __instance, ref bool __result, ref Humanoid human, ref bool repeat)
            {
                if (!modEnabled.Value) {
                    return true;
                }

                if (repeat)
                {
                    return false;
                }

                Player thePlayer = human as Player;
                long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
                bool isClaimIntent = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool isSetSpawnIntent = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool isSleepIntent = !isClaimIntent && !isSetSpawnIntent;

                bool maySleep;
                bool mayClaim;
                bool maySetSpawn;
                string ownerName = __instance.GetOwnerName();

                // Sleep rules
                maySleep = (
                    (__instance.IsMine() && __instance.IsCurrent()) || // Default - it's my claimed spawn bed
                    ((!__instance.IsMine() && ownerName != "" && enableMultipleBedfellows.Value)) || // Many sleepers
                    (ownerName == "" && sleepWithoutClaiming.Value) || // Ignore claim rules
                    (!__instance.IsCurrent() && sleepWithoutSpawnpoint.Value) // Ignore spawn rules
                );

                // Claim rules
                mayClaim = (
                    (ownerName == "")
                );

                // Set spawn rules
                maySetSpawn = (
                    (!__instance.IsCurrent() && __instance.IsMine()) || // Default - it's my bed, but not currently spawn
                    (!__instance.IsCurrent() && (!__instance.IsMine() && multipleSpawnpointsPerBed.Value)) // Allow multiple spawns
                );

                if (isClaimIntent && mayClaim)
                {
                    if (!__instance.CheckExposure(thePlayer))
                    {
                        __result = false;
                        return false;
                    }

                    __instance.SetOwner(playerID, Game.instance.GetPlayerProfile().GetName());
                }

                if (isSetSpawnIntent && maySetSpawn)
                {
                    if (!__instance.CheckExposure(thePlayer))
                    {
                        __result = false;
                        return false;
                    }

                    // My bed, not current spawnpoint. Normal behaviour
                    Game.instance.GetPlayerProfile().SetCustomSpawnPoint(__instance.GetSpawnPoint());
                    human.Message(MessageHud.MessageType.Center, "$msg_spawnpointset", 0, null);
                    return false;
                }

                // Triggering "sleep" hover actions
                if (isSleepIntent && maySleep)
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
                }

                __result = false;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(Bed.IsCurrent))]
            public static bool IsCurrent(Bed __instance, ref bool __result)
            {
                if (!__instance.IsMine() && !multipleSpawnpointsPerBed.Value)
                {
                    __result = false;
                    return false;
                }

                __result = Vector3.Distance(__instance.GetSpawnPoint(), Game.instance.GetPlayerProfile().GetCustomSpawnPoint()) < 1f;
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
