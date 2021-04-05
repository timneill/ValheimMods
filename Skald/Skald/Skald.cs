using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using fastJSON;
using HarmonyLib;
using Random = System.Random;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Skald
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency(ValheimLib.ValheimLib.ModGuid)]
    public class SkaldMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.skald";
        const string PLUGIN_NAME = "Skald";
        const string PLUGIN_VERSION = "1.0.0";

        const string DREAM_TEXTS = @"\dreams.json";
        const string RUNESTONE_TEXTS = @"\runestones.json";

        protected static List<string> m_SkaldDreamTexts = new List<string>();
        protected static Dictionary<string, List<RuneStone.RandomRuneText>> m_SkaldRunestoneTexts = new Dictionary<string, List<RuneStone.RandomRuneText>>();

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<string> readMoreModifierKey;

        private static SkaldMod self;
        private static Assembly assembly;
        private static readonly Random rng = new Random();

        internal void Awake()
        {
            self = this;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            readMoreModifierKey = Config.Bind<string>("General", "ReadMoreModifierKey", "left shift", "Modifier key for additional functionality.");

            if (!modEnabled.Value)
            {
                return;
            }

            Logger.Log(LogLevel.Info, "Initializing Skald...");

            // Recommended way to get current Assembly
            assembly = typeof(SkaldMod).Assembly;

            Harmony.CreateAndPatchAll(assembly, PLUGIN_ID);
            InitializeSkaldTexts();

            Logger.Log(LogLevel.Info, "Skald initialized.");
        }

        private void InitializeSkaldTexts()
        {
            string libDir = Path.GetDirectoryName(assembly.Location);

            Logger.Log(LogLevel.Info, "Loading dream texts...");

            // Dream texts are a simple flat array of translation keys
            m_SkaldDreamTexts = JSON.ToObject<List<string>>(File.ReadAllText(libDir + DREAM_TEXTS));
            Logger.Log(LogLevel.Info, $"Loaded {m_SkaldDreamTexts.Count} dream texts.");

            Logger.Log(LogLevel.Info, "Loading runestone texts...");

            // Runestone texts are a dictionary: {"runestone type" => ["translation keys"]}
            Dictionary<string, List<string>> runestoneTextData = JSON.ToObject<Dictionary<string, List<string>>>(File.ReadAllText(libDir + RUNESTONE_TEXTS));
            Logger.Log(LogLevel.Info, $"Loaded {runestoneTextData.Count} runestone replacement targets");

            foreach (var runestoneData in runestoneTextData)
            {
                List<RuneStone.RandomRuneText> runestoneTexts = MapList(runestoneData.Value);
                m_SkaldRunestoneTexts.Add(runestoneData.Key, runestoneTexts);

                Logger.Log(LogLevel.Info, $"{runestoneData.Key} has {runestoneTexts.Count} texts.");
            }

            Logger.Log(LogLevel.Info, "Skald texts loaded.");
        }

        // Simply maps List<string> to List<RandomRuneText>
        private List<RuneStone.RandomRuneText> MapList(List<string> runestoneTextData)
        {
            List<RuneStone.RandomRuneText> mappedTexts = new List<RuneStone.RandomRuneText>();

            foreach (string runestoneText in runestoneTextData)
            {
                mappedTexts.Add(new RuneStone.RandomRuneText { m_text = runestoneText });
            }

            return mappedTexts;
        }

        [HarmonyPatch(typeof(DreamTexts))]
        public class DreamTextsPatch
        {
            private static bool m_SkaldDreamsInitialized = false;

            [HarmonyPrefix]
            [HarmonyPatch(nameof(DreamTexts.GetRandomDreamText))]
            public static void GetRandomDreamText(ref List<DreamTexts.DreamText> ___m_texts)
            {
                if (!m_SkaldDreamsInitialized)
                {
                    self.Logger.Log(LogLevel.Info, $"Prefixing dream texts. Current count: {___m_texts.Count}");

                    foreach (string dreamText in m_SkaldDreamTexts)
                    {
                        ___m_texts.Add(new DreamTexts.DreamText {
                            m_text = dreamText,
                            m_chanceToDream = 1.0f
                        });
                    }

                    self.Logger.Log(LogLevel.Info, $"New dream text count: {___m_texts.Count}");

                    m_SkaldDreamsInitialized = true;
                }
            }
        }

        [HarmonyPatch(typeof(RuneStone))]
        [HarmonyPriority(Priority.High)]
        public class RuneStoneTextPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(RuneStone.Interact))]
            public static bool Interact(ref RuneStone __instance, ref List<RuneStone.RandomRuneText> ___m_randomTexts)
            {
                if (!(
                    Input.GetKey(readMoreModifierKey.Value) &&
                    m_SkaldRunestoneTexts.ContainsKey(__instance.name)
                ))
                {
                    return true;
                }

                RuneStone.RandomRuneText randomText = m_SkaldRunestoneTexts[__instance.name][rng.Next(m_SkaldRunestoneTexts[__instance.name].Count)];

                TextViewer.instance.ShowText(TextViewer.Style.Rune, "$skald_runestone_topic", randomText.m_text, autoHide: true);

                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(RuneStone.GetHoverText))]
            public static string GetHoverText(string __result, RuneStone __instance)
            {
                if (m_SkaldRunestoneTexts.ContainsKey(__instance.name))
                {
                    return (__result ?? "") + Localization.instance.Localize($"\n[<color=yellow><b>{readMoreModifierKey.Value} + $KEY_Use</b></color>] $skald_piece_rune_read_more");
                }

                return __result;
            }
        }
    }
}
