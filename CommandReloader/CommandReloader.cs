using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using RaftModLoader;
using System.Runtime.CompilerServices;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CommandReloader {
    public class Main : Mod
    {
        Harmony harmony;
        static bool _ac = true;
        public static bool AutoReloadCraftingMenu => ExtraSettingsAPI_Loaded ? _ac : true;
        public void Start()
        {
            (harmony = new Harmony("com.aidanamite.CommandReloader")).PatchAll();
            RefreshCommands(null);
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            harmony?.UnpatchAll(harmony.Id);
            Log("Mod has been unloaded!");
        }

        static int last = ItemManager.GetAllItems().Count;
        static DateTime? lastChange = null;
        void Update()
        {
            if (AutoReloadCraftingMenu && ComponentManager<CraftingMenu>.Value && (Patch_RegisterItem.registered || last != ItemManager.GetAllItems().Count))
                lastChange = DateTime.UtcNow;
            if (AutoReloadCraftingMenu && lastChange != null && (DateTime.UtcNow - lastChange.Value).TotalMilliseconds > 500)
            {
                lastChange = null;
                Log(RefreshCraftingMenu(null));
            }
            Patch_RegisterItem.registered = false;
            last = ItemManager.GetAllItems().Count;
        }

        public void ExtraSettingsAPI_ButtonPress(string name)
        {
            if (name == "reload")
                RefreshCommands(null);
            if (name == "reload2")
                RefreshCraftingMenu(null);
        }

        public void ExtraSettingsAPI_Load() => ExtraSettingsAPI_SettingsClose();
        public void ExtraSettingsAPI_SettingsClose()
        {
            _ac = ExtraSettingsAPI_GetCheckboxState("autoreload");
        }
        public static string RefreshCommands(string[] args)
        {
            foreach (var c in FindObjectsOfType<RConsole>())
                c.RefreshCommands();
            return null;
        }

        public static string RefreshCraftingMenu(string[] args)
        {
            if (!ComponentManager<CraftingMenu>.Value)
                return "Can only reload the crafting menu in-world";
            {
                var d = Traverse.Create(ComponentManager<CraftingMenu>.Value).Field("allRecipes").GetValue<Dictionary<CraftingCategory, List<RecipeItem>>>();
                var s = new HashSet<int>();
                foreach (var p in d)
                    foreach (var i in p.Value)
                        foreach (var r in i.recipes)
                            if (r.settings_recipe.Learned)
                                s.Add(r.UniqueIndex);
                d.Clear();
                var l = Traverse.Create(ComponentManager<CraftingMenu>.Value).Field("recipeMenuItems").GetValue<List<RecipeMenuItem>>();
                foreach (var o in l)
                    if (o)
                        Destroy(o.gameObject);
                l.Clear();
                Traverse.Create(ComponentManager<CraftingMenu>.Value).Method("Awake").GetValue();
                d = Traverse.Create(ComponentManager<CraftingMenu>.Value).Field("allRecipes").GetValue<Dictionary<CraftingCategory, List<RecipeItem>>>();
                foreach (var p in d)
                    foreach (var i in p.Value)
                        foreach (var r in i.recipes)
                            if (!r.settings_recipe.Learned && (s.Contains(r.UniqueIndex) || GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.learnAllRecipiesAtStart))
                                r.settings_recipe.Learned = true;
                if (CanvasHelper.ActiveMenu == MenuType.Inventory)
                    ComponentManager<CraftingMenu>.Value.ReselectCategory();
            }
            {
                var s = new HashSet<int>();
                var l = Traverse.Create(ComponentManager<Inventory_ResearchTable>.Value).Field("menuItems").GetValue<List<ResearchMenuItem>>();
                foreach (var i in l)
                    if (i)
                    {
                        if (i.Learned)
                            s.Add(i.GetItem().UniqueIndex);
                        Destroy(i.gameObject);
                    }
                l.Clear();
                var s2 = new HashSet<int>();
                var d= Traverse.Create(ComponentManager<Inventory_ResearchTable>.Value).Field("availableResearchItems").GetValue<Dictionary<Item_Base, AvaialableResearchItem>>();
                foreach (var p in d)
                    if (p.Value) {
                        if (p.Value.Researched)
                            s2.Add(p.Key.UniqueIndex);
                        Destroy(p.Value.gameObject);
                            }
                d.Clear();
                ComponentManager<Inventory_ResearchTable>.Value.CreateMenuItems(ComponentManager<CraftingMenu>.Value);
                d = Traverse.Create(ComponentManager<Inventory_ResearchTable>.Value).Field("availableResearchItems").GetValue<Dictionary<Item_Base, AvaialableResearchItem>>();
                foreach (var p in d)
                    if (s2.Contains(p.Key.UniqueIndex) || GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.learnAllRecipiesAtStart)
                        p.Value.SetResearchedState(true);
                l = Traverse.Create(ComponentManager<Inventory_ResearchTable>.Value).Field("menuItems").GetValue<List<ResearchMenuItem>>();
                foreach (var i in l)
                    if (s.Contains(i.GetItem().UniqueIndex) || GameModeValueManager.GetCurrentGameModeValue().playerSpecificVariables.learnAllRecipiesAtStart)
                        i.Learn();
            }
            return "merp";
        }

        static bool ExtraSettingsAPI_Loaded = false;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ExtraSettingsAPI_GetCheckboxState(string SettingName) => false;
    }

    [HarmonyPatch]
    static class Patch_RConsole {
        static MethodBase TargetMethod() => typeof(RConsole).Assembly.GetTypes().First(x => x.Name.StartsWith("<RefreshCommands>")).GetMethod("MoveNext", ~BindingFlags.Default);
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.opcode == OpCodes.Callvirt && (x.operand as MethodInfo).Name == "Clear") + 1;
            var ind2 = code.FindIndex(ind, x => x.opcode == OpCodes.Callvirt && (x.operand as MethodInfo).Name == "set_Item");
            var newCode = new List<CodeInstruction>();
            for (int i = ind; i <= ind2; i++)
                newCode.Add(new CodeInstruction(code[i]) { labels = new List<Label>() });
            var loc = iL.DeclareLocal(typeof(ReloaderCommands));
            var lbl1 = iL.DefineLabel();
            var lbl2 = iL.DefineLabel();
            var ind3 = newCode.FindIndex(x => x.opcode == OpCodes.Ldstr);
            newCode.RemoveAt(ind3);
            newCode.InsertRange(ind3, new[]
            {
                new CodeInstruction(OpCodes.Ldloc,loc),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReloaderCommands),nameof(ReloaderCommands.CurrentName)))
            });
            ind3 = newCode.FindLastIndex(x => x.opcode == OpCodes.Ldstr);
            newCode.RemoveAt(ind3);
            newCode.InsertRange(ind3, new[]
            {
                new CodeInstruction(OpCodes.Ldloc,loc),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReloaderCommands),nameof(ReloaderCommands.CurrentDocs)))
            });
            ind3 = newCode.FindIndex(x => x.opcode == OpCodes.Ldftn);
            newCode.RemoveAt(ind3);
            newCode.InsertRange(ind3, new[]
            {
                new CodeInstruction(OpCodes.Ldloc,loc),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReloaderCommands),nameof(ReloaderCommands.CurrentMethod)))
            });
            newCode[0].labels.Add(lbl1);
            newCode.InsertRange(0,new[] {
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReloaderCommands),nameof(ReloaderCommands.Create))),
                new CodeInstruction(OpCodes.Stloc,loc),
                new CodeInstruction(OpCodes.Br_S,lbl2)
                });
            newCode.AddRange(new[]
            {
                new CodeInstruction(OpCodes.Ldloc,loc) { labels = new List<Label>() { lbl2 } },
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ReloaderCommands),nameof(ReloaderCommands.MoveNext))),
                new CodeInstruction(OpCodes.Brtrue,lbl1)
            });
            code.InsertRange(ind, newCode);
            return code;
        }

        class ReloaderCommands
        {
            (string, string, MethodInfo)[] commands = new[] {
                ("reloadAllCommands","Reloads all the console commands",AccessTools.Method(typeof(Main), nameof(Main.RefreshCommands))),
                ("reloadCraftingMenu","Reloads the crafting menu",AccessTools.Method(typeof(Main), nameof(Main.RefreshCraftingMenu)))
            };
            int current = -1;
            public static ReloaderCommands Create() => new ReloaderCommands();
            public string CurrentName() => commands[current].Item1;
            public string CurrentDocs() => commands[current].Item2;
            public IntPtr CurrentMethod() => commands[current].Item3.MethodHandle.GetFunctionPointer();
            public bool MoveNext() => (current += 1) < commands.Length;
        }
    }

    [HarmonyPatch(typeof(RAPI),nameof(RAPI.RegisterItem))]
    static class Patch_RegisterItem
    {
        public static bool registered = false;
        static void Prefix() => registered = true;
    }
}