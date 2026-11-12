# Changelog

本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.1.1] - 2026-11-12

### 修正

- `manifest.xml` 的 `<GameVersions>` 改为游戏实际版本 **`0.46D`**。原写法 `1.0.0` 与游戏 `Application.version` 不一致，模组菜单会把该条目标红并提示「不适用于游戏版本」（不影响加载，但很难看）。
- 游戏版本可以从 `<游戏目录>\Probably Stolen_Data\globalgamemanagers` 里的 `Application.version` 字符串读出来；游戏更新后要同步这里。

## [1.1.0] - 2026-11-12

### 新增

- 涡轮增压器现在可以对**养殖笼**使用：笼内每一只活体动物各推进一天
- 涡轮增压器现在可以对**单只活体动物**（老鼠等）使用：该动物推进一天

### 实现

- 资格判定新增两类目标：`AnimalCage.CAGE_TAG`（要求笼内有活体）与 `ANIMAL_ITEM_TAG`（要求未死亡）
- 包装三档涡轮的 `onThisTargetItemFunc`，对养殖笼额外执行一次「笼内逐个过一天」，随后交还原版逻辑（音效 / 销毁 / 扣充能）

### 备注

- 空笼子、死动物不可选中，避免浪费涡轮
- 笼子自身的耐久衰减仍按原版执行一次

## [1.0.0] - 2026-11-12

### 新增

- 涡轮增压器现在可以对**循环水养殖系统（RAS）**使用：整缸推进一天

### 实现

- `MachineTurboBoosterAdv.IsTurboBoostEligible` 后缀补丁：带 `MachineRAS.RAS_TAG` 的机器视为可被涡轮加速
- 依赖原版已有委托链：`GeneratePower` → `TryDrawCyclePower` → `AquacultureHelper.TickTank` → `UpdateSprite`
