# 原理与设计取舍

记录这个模组为什么只需要两处补丁，以及当时排除掉的方案。所有结论都来自对游戏 `Assembly-CSharp.dll` 的反编译（Unity 2021.3.45f2，游戏 0.46D，ModLoader 100-Rev1）。

> 反编译方法：用游戏自带的 `Unity.Burst.Cecil.dll` 读程序集，打印类型成员、字符串常量和指定方法的 IL。脚本见文末「复现」。

## 1. 涡轮是怎样作用于一台机器的

三档涡轮（`turbo_booster` / `turbo_booster_adv` / `turbo_booster_hypertech`）在 `ToolDirectory` 里构建，都是「工具指着目标物品」的交互：

```csharp
// 基础款：ToolDirectory.CreateTurboBooster
mayThisTargetItemFunc = (tool, target) => IsItemOwned(tool) && IsItemOwned(target) && IsTurboBoostEligible(target);
canThisTargetItemFunc = (tool, target) => IsItemOwned(tool) && IsItemOwned(target) && CanBoostNow(target);

onThisTargetItemFunc = (tool, target) => {
    target.onCycleEndEarlySlotItemFunc?.Invoke(target, target.parentInventory, target.GetIncompleteSlotInInventory());
    target.onCycleEndSlotItemFunc?.Invoke(...);
    AudioManager.Instance.Play2("turbo");
    tool.Destroy();                             // 基础款一次性
};

// 高级 / 超科技：MachineTurboBoosterAdv.BoostMachine(target) 只做前两步，另外扣充能
```

也就是说：**涡轮 = 手动触发目标自己的「换日」回调**。

## 2. 为什么养殖系统用不了

```csharp
// MachineTurboBoosterAdv.IsTurboBoostEligible
if (item.IsTag("STANDARD_MACHINE_TAG")) return true;
return item.IsTag("TURBO_BOOST_ELIGIBLE_TAG");
```

而 `MachineRAS.CreateRAS()` 打的标签是 `RAS_TAG` / `RAS_TANK_GRID_TAG` / `RAS_FILTER_SLOT_TAG` / `RAS_WATER_TANK_TAG` / 三个升级计数标签 —— 上面那两个**一个都没有**，所以三档涡轮都无法指向它。

同时，`CreateRAS` 早已把「过一天」挂好了：

| 委托 | 实现 |
|---|---|
| `onCycleEndEarlySlotItemFunc` | `AquacultureHelper.GeneratePower(machine, tankGrid)` |
| `onCycleEndSlotItemFunc` | `IsWorthRunning && TryDrawCyclePower` → `AquacultureHelper.TickTank(...)` → `MachineRAS.UpdateSprite` |

`TickTank` 内部 `AgeOne → AgeOneDay`：生长、摄食、产卵、疾病、死亡、游动。

**所以 RAS 只差一个资格判定，效果一行都不用写。**

## 3. 动物：同样只差资格

```csharp
// HusbandryHelper.InitAnimalItem
item.onCycleEndSlotItemFunc = (i, inv, m) => HusbandryHelper.OnAnimalItemDayEnd(i, inv);
```

`InitRatItem` 也是走 `InitAnimalItem`，所以老鼠天生就有每日回调。`OnAnimalItemDayEnd` 里依次是 `GrowAnimal`、`HandleHunger`、`CheckHygiene`、`HandleDisease`、`HandlePregnancyProgress`……

**结论：资格放行即可，不需要自己实现成长。**

## 4. 养殖笼：唯一需要额外代码的地方

```csharp
// AnimalCage.CreateAnimalCage 挂的每日回调，全程只在算：
//   遍历 childItems 里 ANIMAL_ITEM_TAG 且未死亡的个体
//   累计占用体积、检查 ANIMAL_OVERCROWDING_TAG
//   然后按这个结果扣 CAGE_HEALTH_TAG
// 它不推进任何动物。
```

那笼里的动物是怎么过夜的？`PlayerStore.EndNight`：

```csharp
foreach (var item in FindAllItem(...)) item.onCycleEndEarlySlotItemFunc?.Invoke(...);
foreach (var item in FindAllItem(...)) item.onCycleEndSlotItemFunc?.Invoke(...);
```

`FindAllItem` 对每个窗口调用 `GraphUtils.FindAllChildrenType<GameItem>`（递归），所以**笼里的动物是被逐个找到后各自过夜的**，笼子只是容器。

由此得到两条设计约束：

1. 对笼子用涡轮时，需要我们自己把笼内动物逐个推进一天；
2. **绝对不能**给笼子加一个「推进动物」的每日回调 —— 那样正常过夜会变成一天涨两天。

## 5. 最终实现

```
补丁 1：MachineTurboBoosterAdv.IsTurboBoostEligible  [Postfix]
    RAS_TAG                              → true
    CAGE_TAG && 笼内有活体                → true
    ANIMAL_ITEM_TAG && 未死亡             → true

补丁 2：ToolDirectory.CreateTurboBooster / ...Adv / ...Hypertech  [Postfix]
    包装 tool.onThisTargetItemFunc：
        target 是养殖笼 → 笼内每只活体各走一遍 onCycleEndEarly / onCycleEnd
        → 再调用原版委托（音效 / 销毁 / 扣充能）
```

调用参数与 `EndNight` 完全一致：`(动物, 它所在的库存, 它所在槽位)`。

## 6. 排除掉的方案

| 方案 | 为什么不采用 |
|---|---|
| 给养殖笼补每日回调来推进动物 | 正常过夜会重复推进（`EndNight` 已经逐个处理过动物） |
| 给 RAS / 动物补 `TURBO_BOOST_ELIGIBLE_TAG` 标签 | 要覆盖所有生成路径（工厂 / 起始方案 / 商人 / 读档），还得扫描旧存档；而资格补丁一处就够 |
| 只补 `BoostMachine` | 基础款涡轮根本不调 `BoostMachine`，它在自己的 lambda 里内联了同样的调用后销毁自己 |
| 直接补基础款那个编译器生成的 lambda | 名字是 `<CreateTurboBooster>b__4` 这种，游戏一更新就容易失配；包装公开委托更稳 |
| 复制一份原版养鱼/养鼠逻辑自己算一天 | 等于重写全部成长、饥饿、疾病、繁殖规则，必然和原版漂移 |

## 7. 复现反编译

```powershell
# 需要游戏本体；脚本用游戏自带的 Unity.Burst.Cecil.dll，无需联网
.\Dump-Type.ps1 -Names MachineTurboBoosterAdv -IL IsTurboBoostEligible
.\Dump-Type.ps1 -Names MachineRAS -Strings
.\Dump-Type.ps1 -Names 'AnimalCage/<>c' -IL '<CreateAnimalCage>b__20_4'
.\Find-Calls.ps1 -MethodName OnAnimalItemDayEnd
.\Find-Calls.ps1 -MethodName get_onCycleEndSlotItemFunc
```

脚本在本模组的开发工具链里维护，思路：`Dump-Type` 打印字段 / 方法 / `ldstr` 常量 / 方法 IL，`Find-Calls` 反查某个方法的调用方——后者是判断「这个回调是每帧跑还是一天跑一次」的关键。

## 8. 游戏更新后怎么修

1. 先跑 `IsTurboBoostEligible` 的 IL，确认资格判定是否还认同样的标签；
2. 跑 `Find-Calls -MethodName get_onCycleEndSlotItemFunc`，确认涡轮仍然通过这两个委托触发；
3. 跑 `AnimalCage` 的 `CreateAnimalCage`，确认笼子的每日回调仍然只管耐久；
4. 跑 `HusbandryHelper.InitAnimalItem`，确认动物仍挂 `OnAnimalItemDayEnd`；
5. 任一环节变了，对应调整 `TurboTargets` / 补丁目标即可——补丁本身只有 20 行。

## English summary

The Turbo Booster simply invokes a target's own `onCycleEndEarlySlotItemFunc` / `onCycleEndSlotItemFunc`. Vanilla gates targeting behind a tag whitelist (`STANDARD_MACHINE_TAG` / `TURBO_BOOST_ELIGIBLE_TAG`) that husbandry items lack, while the RAS and every animal already wire those callbacks to a full day of simulation. The animal cage is the only exception: its daily callback only decays cage durability, because caged animals are aged individually by `PlayerStore.EndNight`. Hence the mod only widens the eligibility check and, when a turbo is used on a cage, advances each live animal once — mirroring `EndNight`'s call signature — before handing control back to vanilla.
