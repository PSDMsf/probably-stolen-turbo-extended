# 工坊发布

## 一次性准备

1. 安装 [steamcmd](https://developer.valvesoftware.com/wiki/SteamCMD)
2. 在 Steam 网页上接受一次创意工坊法律协议（首次上传时必须）
3. 准备一张预览图 `workshop/preview.png`：建议 512×512 或 16:9，**小于 1 MB**（仓库里放的是占位图，发布前请换掉）

## 打包

```powershell
pwsh -File workshop/package-workshop.ps1 `
     -GameDir "D:\SteamLibrary\steamapps\common\Probably Stolen" `
     -WriteVdf
```

脚本会：

- 编译 `src/`（`-p:DeployToMods=false`，不往游戏目录写）
- 组装 `workshop/content/`：`manifest.xml` + `ras_turbo_boost.dll` + `preview.png`
- 生成 `dist/ras_turbo_boost-<版本>.zip`（GitHub Release 用，解压即得 `Mods\ras_turbo_boost\`）
- 用 `docs/workshop-description.zh.md` 生成 `workshop/workshop.vdf`

## 首次上传

```powershell
steamcmd +login <你的账号> +workshop_build_item "<仓库绝对路径>\workshop\workshop.vdf" +quit
```

- `publishedfileid` 为 `0`（脚本默认）表示**新建**物品
- `visibility` 建议先用 `2`（隐藏）自测，确认没问题再改 `0`（公开）
- steamcmd 成功后会把真实物品 ID 写回 `workshop.vdf`，**保留这个文件**（已在 `.gitignore` 里）；下次同一命令即为更新

## 更新已发布物品

1. 改版本号：`src/manifest.xml` 的 `<ModVersion>`、`src/ras_turbo_boost.csproj` 的 `<Version>`、`CHANGELOG.md`
2. 重新打包（`-PublishId` 用 vdf 里已有的 ID，或直接沿用已被 steamcmd 改写过的 vdf）
3. `steamcmd +login <账号> +workshop_build_item ... +quit`
4. 订阅者会自动收到更新

## 发版前检查

- [ ] `mod-scan ras_turbo_boost` 无未声明能力、无 `DEPRECATED`
- [ ] 能力没有新增（新增能力会让所有玩家重新审核一次）
- [ ] 隐藏可见性上传 → 订阅 → 实机验证 → 再改公开
- [ ] 描述里写清 `不写入存档`，玩家会关心这个
- [ ] 预览图换成了自己的美术
