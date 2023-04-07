using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace EyeOfTyr
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class EyeOfTyrMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.eyeoftyr";
        const string PLUGIN_NAME = "Eye of Tyr";
        const string PLUGIN_VERSION = "0.0.1";

        private readonly Harmony harmony = new Harmony(PLUGIN_ID);

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<float> pollDelay;

        private static float pollTimer = 0.0f;

        private static EyeOfTyrMod self;
        private static Assembly assembly;

        private static readonly bool debugThis = true;

        internal void Awake()
        {
            self = this;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            pollDelay = Config.Bind<float>("General", "Update frequency", 3.0f, "Minimum time in between checks.");

            if (!modEnabled.Value)
            {
                return;
            }

            dbg("Initializing Eye of Tyr...");

            assembly = typeof(EyeOfTyrMod).Assembly;
            harmony.PatchAll(assembly);

            dbg("Eye of Tyr initialized.");
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

        [HarmonyPatch(typeof(BaseAI))]
        public static class BaseAIPatch
        {
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            [HarmonyPatch(nameof(BaseAI.CanHearTarget), typeof(Character))]
            public static void CanHearTarget(BaseAI __instance, Character target, ref bool __result)
            {
                if (!modEnabled.Value)
                {
                    return;
                }

                if (pollTimer > Time.time)
                {
                    return;
                }
                else
                {
                    pollTimer = Time.time + pollDelay.Value;
                }

                if (target.IsPlayer() && __result)
                {
                    FieldInfo field = AccessTools.Field(typeof(BaseAI), "m_character");
                    Character character = (Character)field.GetValue(__instance);

                    dbg(character.GetHoverName() + " can hear player " + target.GetHoverName() + ": " + __result.ToString());
                    target.Message(MessageHud.MessageType.Center, $"{character.GetHoverName()} can hear you.", 0, null);
                }
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            [HarmonyPatch(nameof(BaseAI.CanSeeTarget), typeof(Character))]
            public static void CanSeeTarget(BaseAI __instance, Character target, ref bool __result)
            {
                if (!modEnabled.Value)
                {
                    return;
                }

                if (pollTimer > Time.time)
                {
                    return;
                }
                else
                {
                    pollTimer = Time.time + pollDelay.Value;
                }

                if (target.IsPlayer() && __result)
                {
                    FieldInfo field = AccessTools.Field(typeof(BaseAI), "m_character");
                    Character character = (Character)field.GetValue(__instance);

                    dbg(character.GetHoverName() + " can see player " + target.GetHoverName() + ": " + __result.ToString());
                    target.Message(MessageHud.MessageType.Center, $"{character.GetHoverName()} can see you.", 0, null);
                }
            }
        }
    }
}
