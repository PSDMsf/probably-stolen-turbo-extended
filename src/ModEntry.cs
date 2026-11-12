using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RasTurboBoost
{
    /// <summary>
    /// 模组入口。Init/OnEnable 在主菜单阶段运行，这里只做初始化。
    /// 行为全部由下面的 Harmony 补丁提供（加载器会自动扫描并应用）。
    /// </summary>
    public class ModEntry : IMod
    {
        internal static ModLog Log;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            Log.Log("TurboBoosterExtended 初始化");
        }

        public void OnEnable()
        {
            Log.Log("已启用：涡轮增压器可以加速循环水养殖系统、养殖笼与单只动物");
        }

        public void OnDisable()
        {
        }
    }

    /// <summary>
    /// 原版 MachineTurboBoosterAdv.IsTurboBoostEligible 只认两个标签：
    ///   item.IsTag("STANDARD_MACHINE_TAG") || item.IsTag("TURBO_BOOST_ELIGIBLE_TAG")
    /// 养殖相关的东西两个都没有，所以涡轮连指都指不上去。
    /// 这里把三类目标补进「可被涡轮指向」的名单：
    ///   1. 循环水养殖系统（MachineRAS.RAS_TAG）
    ///   2. 养殖笼（AnimalCage.CAGE_TAG），且笼里至少有一只活着的动物
    ///   3. 单只活体动物（"ANIMAL_ITEM_TAG" 且未死亡）
    /// </summary>
    [HarmonyPatch(typeof(MachineTurboBoosterAdv), nameof(MachineTurboBoosterAdv.IsTurboBoostEligible))]
    internal static class TurboEligiblePatch
    {
        private static bool announcedRas;
        private static bool announcedCage;
        private static bool announcedAnimal;

        [HarmonyPostfix]
        private static void Postfix(GameItem gameItem, ref bool __result)
        {
            if (__result || gameItem == null) return;

            // 1) RAS：它自己的 onCycleEnd* 委托就是「养一天」，放行即可
            if (gameItem.IsTag(MachineRAS.RAS_TAG))
            {
                __result = true;
                if (!announcedRas) { announcedRas = true; ModEntry.Log?.Log("检测到循环水养殖系统：已放行涡轮加速"); }
                return;
            }

            // 2) 养殖笼：只有笼里有活体动物时才值得用涡轮
            if (gameItem.IsTag(AnimalCage.CAGE_TAG))
            {
                if (TurboTargets.HasLiveAnimal(gameItem))
                {
                    __result = true;
                    if (!announcedCage) { announcedCage = true; ModEntry.Log?.Log("检测到养殖笼（内有活体）：已放行涡轮加速"); }
                }
                return;
            }

            // 3) 单只活体动物：它的 onCycleEndSlotItemFunc 就是 HusbandryHelper.OnAnimalItemDayEnd
            if (TurboTargets.IsLiveAnimal(gameItem))
            {
                __result = true;
                if (!announcedAnimal) { announcedAnimal = true; ModEntry.Log?.Log("检测到活体动物：已放行涡轮加速"); }
            }
        }
    }

    /// <summary>
    /// 养殖笼本身只挂了「笼子耐久」的每日回调，笼里的动物是各自在 EndNight 里过夜的，
    /// 所以对笼子用涡轮时原版只会掉耐久、动物不动。
    /// 这里在涡轮三档的 onThisTargetItemFunc 外面包一层：
    /// 目标若是养殖笼，先让笼内每一只动物各过一天，再走原版逻辑（音效/销毁/扣充能）。
    /// 三档都用同一个委托，所以一并覆盖。
    /// </summary>
    [HarmonyPatch(typeof(ToolDirectory), "CreateTurboBooster")]
    [HarmonyPatch(typeof(ToolDirectory), "CreateTurboBoosterAdv")]
    [HarmonyPatch(typeof(ToolDirectory), "CreateTurboBoosterHypertech")]
    internal static class TurboToolPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameItem __result)
        {
            TurboTargets.WrapTool(__result);
        }
    }

    internal static class TurboTargets
    {
        private const string ANIMAL_ITEM_TAG = "ANIMAL_ITEM_TAG";

        // 防止同一个物品实例被重复包装（重复包装会让笼子一次推进多天）
        private static readonly ConditionalWeakTable<GameItem, object> Wrapped = new ConditionalWeakTable<GameItem, object>();

        public static bool IsLiveAnimal(GameItem item)
        {
            return item != null
                && item.IsTag(ANIMAL_ITEM_TAG)
                && !HusbandryHelper.IsAnimalDead(item);
        }

        public static bool HasLiveAnimal(GameItem cage)
        {
            GameGridInventory grid = AnimalCage.GetCageInventory(cage);
            if (grid == null) return false;

            List<GameItem> animals = grid.childItems;
            if (animals == null) return false;

            for (int i = 0; i < animals.Count; i++)
            {
                if (IsLiveAnimal(animals[i])) return true;
            }
            return false;
        }

        public static void WrapTool(GameItem tool)
        {
            if (tool == null) return;

            Action<GameItem, GameItem> original = tool.onThisTargetItemFunc;
            if (original == null) return;

            if (Wrapped.TryGetValue(tool, out _)) return;
            Wrapped.Add(tool, new object());

            tool.onThisTargetItemFunc = (self, target) =>
            {
                try
                {
                    if (target != null && target.IsTag(AnimalCage.CAGE_TAG))
                    {
                        AdvanceCage(target);
                    }
                }
                catch (Exception e)
                {
                    ModEntry.Log?.Error("推进养殖笼失败：" + e);
                }

                original(self, target);
            };
        }

        /// <summary>
        /// 让笼内每只动物各过一天。调用方式与 PlayerStore.EndNight 里的一致：
        /// 用动物自己的 onCycleEndEarlySlotItemFunc / onCycleEndSlotItemFunc，
        /// 参数为 (动物, 它所在的库存, 它所在槽位)。
        /// </summary>
        public static void AdvanceCage(GameItem cage)
        {
            GameGridInventory grid = AnimalCage.GetCageInventory(cage);
            if (grid == null) return;

            // 复制一份：动物可能因为繁殖/死亡在遍历中增删
            List<GameItem> animals = new List<GameItem>(grid.childItems ?? new List<GameItem>());

            foreach (GameItem animal in animals)
            {
                if (animal == null) continue;
                // 只推进动物；笼子格子里可能还有别的东西（饲料等），不要连带触发它们
                if (!animal.IsTag(ANIMAL_ITEM_TAG)) continue;
                if (HusbandryHelper.IsAnimalDead(animal)) continue;

                GameInventory inv = animal.parentInventory;
                SlotMarker marker = animal.GetIncompleteSlotInInventory();

                animal.onCycleEndEarlySlotItemFunc?.Invoke(animal, inv, marker);
                animal.onCycleEndSlotItemFunc?.Invoke(animal, inv, marker);
            }
        }
    }
}
