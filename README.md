# 涡轮增压扩展（Probably Stolen Mod）

让《深空当铺：可能是偷的》里的**涡轮增压器**（`turbo_booster` / 高级 / 超科技）可以对养殖相关目标使用，使用后**推进一天**：

| 目标 | 原版 | 装了这个模组之后 |
|---|---|---|
| 循环水养殖系统（RAS） | 涡轮指不上去 | 整缸鱼过一天 |
| 养殖笼 `animal_cage` | 涡轮指不上去 | 笼内**每只**活体动物各过一天 |
| 单只活体动物（老鼠等） | 涡轮指不上去 | 这一只过一天 |

> English: A mod for **Probably Stolen** (AppID 4348910) that makes the Turbo Booster work on the recirculating aquaculture system, animal cages, and individual animals — advancing them by one day each. See [English section](#english) below.

---

## 装给玩家：怎么用

**Steam 创意工坊（推荐）**：订阅 → 游戏内「模组」菜单启用 → 重启游戏。

**手动安装**：

1. 下载 `ras_turbo_boost-<版本>.zip`（GitHub Releases）
2. 解压到 `<游戏目录>\Mods\ras_turbo_boost\`，确保该目录下直接就是 `manifest.xml` 和 `ras_turbo_boost.dll`
3. 启动游戏 → 「模组」菜单 → 启用 → 重启

游戏目录：Steam 库里右键游戏 → 管理 → 浏览本地文件。

### 它做了什么

- 对**循环水养殖系统**用涡轮：整缸推进一天（生长、摄食、繁殖、死亡判定）
- 对**养殖笼**用涡轮：笼里每一只活着的动物各推进一天（不是只推一只，也不是推两天）
- 对**单只动物**用涡轮：这一只推进一天

### 边界条件（都是有意为之）

- **消耗照旧**：基础款用完即毁，高级/超科技扣充能
- **空笼子不可选**：笼里没有活体动物时涡轮指不上去，避免浪费
- **死的不算**：死掉的动物不可选，笼内的死个体自动跳过
- **笼子耐久**：对笼子用涡轮会照原版扣一次耐久（和正常过夜一致）
- **环境规则不变**：断电的 RAS 依然让鱼掉健康，缺水缺料依然按原版处理
- **一天就是一天**：不会跳生长阶段；连用多个涡轮 = 推进多天
- **不写存档**：模组本身不保存任何数据，卸载不会损坏存档

### 兼容性

- 只改「涡轮能指向谁」和「对笼子用涡轮时额外推进笼内动物」，不动养鱼/养鼠的成长逻辑
- 不需要任何能力声明（`<Capabilities />` 为空），玩家启用时无确认框
- 与只读/展示类模组天然兼容；若其他模组也改了涡轮判定，注意叠加顺序
- 目标游戏版本：**`0.46D`**（写在 `manifest.xml` 的 `<GameVersions>` 里，游戏更新后需要同步，否则模组菜单会显示版本不匹配警告）
- 游戏更新后若相关方法签名变化，需要重新适配（见下方「原理」）

---

## 开发者：怎么编译

需要：**.NET SDK 6.0+**（本项目用 9.0 验证）、**游戏本体**（要它的托管程序集）、C# 基础。

```bash
git clone <this repo>
cd src
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Probably Stolen" -p:DeployToMods=false
```

产物：`src/bin/Release/netstandard2.1/ras_turbo_boost.dll`

游戏目录也可以用环境变量 `PROBABLY_STOLEN_DIR` 指定；`-p:DeployToMods=true`（默认）会在编译后直接装进 `Mods\`。

改完代码在游戏里验证：

1. 游戏根目录放一个空 `dev.txt`（注意别变成 `dev.txt.txt`），重启游戏解锁开发者工具
2. `F8` 打开控制台，执行 `mod-scan ras_turbo_boost`：要求扫描器检测到的能力为空、没有 `DEPRECATED`
3. `F9` 打开开发菜单，生成 RAS / 养殖笼 / 老鼠做场景
4. 日志在 `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`

### 发布工坊

```powershell
pwsh -File workshop/package-workshop.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Probably Stolen"
# 然后按脚本输出，填好 workshop/workshop.vdf 的账号相关项，执行 steamcmd
```

详见 [workshop/README.md](workshop/README.md)。

---

## 原理

一句话：**原版把「谁能被涡轮指到」写成了一个标签白名单，而养殖系列不在名单里；「推进一天」的委托其实早就挂好了（笼子例外）。**

- `MachineTurboBoosterAdv.IsTurboBoostEligible` 只认 `STANDARD_MACHINE_TAG` / `TURBO_BOOST_ELIGIBLE_TAG`
- RAS 与动物各自的 `onCycleEndSlotItemFunc` 已经是「过一天」的实现
- 养殖笼的那个回调只管笼子耐久，动物是各自在 `PlayerStore.EndNight` 里过夜的

模组因此只需要：**(1) 扩展资格判定；(2) 在使用涡轮时给笼子补一次「笼内逐个过一天」**。
完整证据链（含反编译出的 IL）和设计取舍见 [docs/how-it-works.md](docs/how-it-works.md)。

---

## 常见问题

**Q：为什么不直接给养殖笼加每日回调？**
A：因为正常过夜时 `PlayerStore.EndNight` 会递归遍历笼内所有物品、让每只动物各自过夜。再给笼子挂一个会推进动物的回调，就会一天涨两天。所以推进动作只挂在「涡轮使用」这一个时机上。

**Q：为什么 Mod ID 叫 `ras_turbo_boost`？**
A：1.0 版本只做 RAS，ID 就是那时定的。ID 决定玩家磁盘上的文件夹名，改了对已安装玩家是断档，所以保留。

**Q：和其他涡轮类模组冲突吗？**
A：如果对方也改 `IsTurboBoostEligible` 或包装 `onThisTargetItemFunc`，行为取决于 Harmony 补丁顺序，可能出现重复推进。有疑问就只装一个。

**Q：会影响成就吗？**
A：不涉及成就系统，也不会写入存档数据。

---

## English

A mod for **Probably Stolen - Cyberpunk Shopkeeper Sim** (AppID 4348910) that makes the **Turbo Booster** (basic / advanced / hypertech) usable on husbandry targets, advancing them by one day:

- **Recirculating aquaculture system (RAS)** — the whole tank ticks one day.
- **Animal cage** — every live animal inside advances one day (not just one, not two).
- **A single live animal** (e.g. a rat) — that animal advances one day.

Vanilla gates turbo targeting behind a tag whitelist (`STANDARD_MACHINE_TAG` / `TURBO_BOOST_ELIGIBLE_TAG`) that husbandry items do not carry, while the "one day" callbacks already exist on the RAS and on each animal. The cage is the exception: its daily callback only wears down its durability, and animals are aged individually during `PlayerStore.EndNight`. So the mod only needs to (1) widen the eligibility check and (2) advance caged animals at the moment a turbo is used.

Requirements: .NET SDK 6.0+, the game installed (its managed assemblies are required to compile). No capability declarations; nothing is written to save files.

Build: `dotnet build -c Release -p:GameDir="<game dir>" -p:DeployToMods=false`

---

## License

MIT — 见 [LICENSE](LICENSE)。

本模组是非官方作品，与 Questing Goose Studio / tinyBuild 无关。《Probably Stolen》的一切权利归其各自所有者。
