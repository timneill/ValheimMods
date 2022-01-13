using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using fastJSON;
using HarmonyLib;
using Jotunn.Entities;
using Random = System.Random;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Skald
{
    [BepInPlugin(PLUGIN_ID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class SkaldMod : BaseUnityPlugin
    {
        const string PLUGIN_ID = "net.kinghfb.valheim.skald";
        const string PLUGIN_NAME = "Skald";
        const string PLUGIN_VERSION = "1.0.0";

        const string LANGUAGE_FILE = @"\localization.language";
        const string DREAM_TEXTS = @"\dreams.json";
        const string RUNESTONE_TEXTS = @"\runestones.json";

        private readonly Harmony harmony = new Harmony(PLUGIN_ID);

        protected static List<DreamTexts.DreamText> m_SkaldDreamTexts = new List<DreamTexts.DreamText>();
        protected static Dictionary<string, List<RuneStone.RandomRuneText>> m_SkaldRunestoneTexts = new Dictionary<string, List<RuneStone.RandomRuneText>>();
        protected static CustomLocalization m_localization = new CustomLocalization();

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<string> readMoreModifierKey;
        private static ConfigEntry<float> dreamChance;

        private static SkaldMod self;
        private static Assembly assembly;

        private static readonly Random rng = new Random();

        internal void Awake()
        {
            self = this;

            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            readMoreModifierKey = Config.Bind<string>("General", "Modifier key", "left shift", "Modifier key for additional functionality.");
            dreamChance = Config.Bind<float>("General", "Chance to dream", 1.0f, "Updated chance to dream. Game default 0.1 (10%)");

            if (!modEnabled.Value)
            {
                return;
            }

            Logger.Log(LogLevel.Info, "Initializing Skald...");

            // Recommended way to get current Assembly
            assembly = typeof(SkaldMod).Assembly;

            harmony.PatchAll(assembly);
            InitializeSkaldTexts();

            Logger.Log(LogLevel.Info, "Skald initialized.");
        }

        void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        private void InitializeSkaldTexts()
        {
            string libDir = Path.GetDirectoryName(assembly.Location);

            // Loading language file manually
            m_localization.AddLanguageFile(File.ReadAllText(libDir + LANGUAGE_FILE));

            Logger.Log(LogLevel.Info, "Loading dream texts...");
            LoadDreamTexts(libDir);

            Logger.Log(LogLevel.Info, "Loading runestone texts...");
            LoadRunestoneTexts(libDir);

            Logger.Log(LogLevel.Info, "Skald texts loaded.");
        }

        private void LoadDreamTexts(string libDir)
        {
            // Dream texts are a list of dictionary values with texts and conditional keys
            List<DreamDataItem> dreamTexts = JSON.ToObject<List<DreamDataItem>>(File.ReadAllText(libDir + DREAM_TEXTS));

            foreach (DreamDataItem dreamData in dreamTexts)
            {
                m_SkaldDreamTexts.Add(new DreamTexts.DreamText()
                {
                    m_text = m_localization.TryTranslate(dreamData.text),
                    m_trueKeys = dreamData.on,
                    m_falseKeys = dreamData.off
                });
            }

            Logger.Log(LogLevel.Info, $"Loaded {m_SkaldDreamTexts.Count} dream texts.");
        } 

        private void LoadRunestoneTexts(string libDir)
        {
            // Runestone texts are a dictionary: {"runestone type" => ["translation keys"]}
            Dictionary<string, List<string>> runestoneTextData = JSON.ToObject<Dictionary<string, List<string>>>(File.ReadAllText(libDir + RUNESTONE_TEXTS));
            Logger.Log(LogLevel.Info, $"Loaded {runestoneTextData.Count} runestone targets");

            foreach (var runestoneData in runestoneTextData)
            {
                List<RuneStone.RandomRuneText> runestoneTexts = MapRunestoneList(runestoneData.Value);
                m_SkaldRunestoneTexts.Add(runestoneData.Key, runestoneTexts);

                Logger.Log(LogLevel.Info, $"{runestoneData.Key} has {runestoneTexts.Count} texts.");
            }
        }

        // Simply maps List<string> to List<RandomRuneText>
        private List<RuneStone.RandomRuneText> MapRunestoneList(List<string> runestoneTextData)
        {
            List<RuneStone.RandomRuneText> mappedTexts = new List<RuneStone.RandomRuneText>();

            foreach (string runestoneText in runestoneTextData)
            {
                mappedTexts.Add(new RuneStone.RandomRuneText { m_text = m_localization.TryTranslate(runestoneText) });
            }

            return mappedTexts;
        }

        public class DreamDataItem
        {
            public string text { get; set; }
            public List<string> on { get; set; }
            public List<string> off { get; set; }
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
                    self.Logger.Log(LogLevel.Info, $"Preparing to add dream texts. Current count: {___m_texts.Count}");
                    ___m_texts.AddRange(m_SkaldDreamTexts);

                    foreach (DreamTexts.DreamText dreamText in ___m_texts)
                    {
                        dreamText.m_chanceToDream = dreamChance.Value;
                    }

                    self.Logger.Log(LogLevel.Info, $"Dreams added. New count: {___m_texts.Count}");

                    m_SkaldDreamsInitialized = true;
                }
            }
        }

        [HarmonyPatch(typeof(RuneStone))]
        [HarmonyPriority(Priority.High)]
        public class RuneStonePatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(nameof(RuneStone.Interact))]
            public static bool Interact(ref RuneStone __instance)
            {
                if (!(
                    Input.GetKey(readMoreModifierKey.Value) &&
                    m_SkaldRunestoneTexts.ContainsKey(__instance.name)
                ))
                {
                    return true;
                }

                RuneStone.RandomRuneText randomText = m_SkaldRunestoneTexts[__instance.name][rng.Next(m_SkaldRunestoneTexts[__instance.name].Count)];

                TextViewer.instance.ShowText(TextViewer.Style.Rune, m_localization.TryTranslate("skald_runestone_topic"), randomText.m_text, autoHide: true);

                return false;
            }

            [HarmonyPostfix]
            [HarmonyPatch(nameof(RuneStone.GetHoverText))]
            public static string GetHoverText(string __result, RuneStone __instance)
            {
                if (m_SkaldRunestoneTexts.ContainsKey(__instance.name))
                {
                    return (__result ?? "") + Localization.instance.Localize($"\n[<color=yellow><b>{readMoreModifierKey.Value} + $KEY_Use</b></color>] ") + m_localization.TryTranslate("skald_piece_rune_read_more");
                }

                return __result;
            }
        }
    }
}
