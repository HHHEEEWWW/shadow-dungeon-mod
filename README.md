# Shadow Dungeon 修改器 MOD

为 Steam 游戏《Shadow Dungeon》（OO Cat，Unity 2019.4.39 Mono）开发的 BepInEx 5.x 修改器插件。

## 功能

- **Home 键**切换修改器面板
- 读取并显示玩家所有属性（HP、MP、等级、攻击力、速度、暴击、元素6系等）
- 每个属性右侧输入框，输入新数值后点 **OK** 确定修改
- **重置**按钮恢复为修改前的原始值
- **全部应用** / **全部重置** 批量操作
- **中/EN** 切换按钮，支持中英双语界面
- 属性按 **退出保存** / **退出重置** 分组显示，一目了然

## 属性保存机制

> 游戏加载时会从 **装备 + 天赋 + 等级** 重新计算 Base 值（基础值），因此直接修改 Base 会被覆盖。Bei（倍率）类字段保存在存档中，修改后退出重进仍生效。

修改器面板中每个属性都标注了保存状态：

| 状态 | 含义 |
|------|------|
| ✅ 退出后保存 | 修改后退出游戏再进，数值仍然生效 |
| ⚠️ 退出后重置 | 仅当前游戏会话生效，退出后被游戏重新计算覆盖 |

### ✅ 退出后保存的属性

| 属性 | 说明 |
|------|------|
| 生命值 / 法力值 | 当前 HP/MP |
| 等级 / 经验值 | Level、总经验、当前等级经验 |
| 攻击倍率 / 伤害减免 | Damage_Bei、Damage_Anti |
| 移动速度倍率 / 攻击速度倍率 | MVSpeed_Bei、ATSpeed_Bei |
| 生命倍率 / 生命百分比 | Health_Bei、Health_Percent |
| 法力倍率 / 法力百分比 | Mana_Bei、Mana_Percent |
| 暴击率 / 暴击伤害 / 穿透率 / 格挡 | BJrate、BJDamage、JYrate、GeDang |
| 元素伤害倍率（火/冰/雷/毒/物理/暗影） | 6 系元素 Damage_Bei |
| 元素穿透（火/冰/雷/毒/物理/暗影） | 6 系元素 Chuan |
| 元素抗性（火/冰/雷/毒/物理/暗影） | 6 系元素 Anti |
| 冷却缩减 / 掉落率 / 经验范围 | CoolDown、ItemDrop_Rate、EXP_Range |
| 金币 | Money（通过 SaveManager） |
| 天赋点 | 可用天赋点、天赋点(基础) |

### ⚠️ 退出后重置的属性（当前生效）

| 属性 | 说明 |
|------|------|
| 基础攻击力 | Damage_Base（从武器+装备重算） |
| 移动速度基础 | MVSpeed_Base（从等级/装备重算） |
| 攻击速度基础 | ATSpeed_Base（从等级/装备重算） |

## 前置条件

- Steam 版《Shadow Dungeon》
- BepInEx 5.x（Mono 版本）

### 推荐 Mod 管理器

> 💡 **强烈建议**使用以下管理器安装和管理 BepInEx，自动处理隔离模式、版本匹配、插件部署：

| 管理器 | 链接 | 说明 |
|--------|------|------|
| **BepInEx-Manager** | [GitHub](https://github.com/HHHEEEWWW/bepinex-manager) | 作者自用管理器，支持多游戏隔离档案 |
| **r2modman** | [GitHub](https://github.com/ebkr/r2modmanPlus) · [Thunderstore](https://thunderstore.io/package/ebkr/r2modman/) | 通用 Mod 管理器，支持数百款游戏 |

## 构建与部署

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

脚本逻辑：读游戏目录 `doorstop_config.ini` 的 `target_assembly` 反推出档案 BepInEx 根 →
`dotnet build -p:BepDir=... -p:GameManaged=...` → 复制 DLL 到 `plugins/`。

## 技术栈

- BepInEx 5.4.23.5（Mono）
- HarmonyX（补丁框架）
- IMGUI（运行时 UI）
- 反射访问游戏类（PlayerManager、TalentManager、SaveManager）

## 项目结构

```
shadow-dungeon/
  ├─ src/ShadowDungeonTrainer/   插件源码（net472）
  │   ├─ Plugin.cs               BepInEx 入口
  │   └─ TrainerManager.cs       IMGUI 面板 + 属性读写
  ├─ releases/                   构建产物
  │   └─ ShadowDungeonTrainer.dll
  ├─ tools/                      构建工具（gitignore）
  ├─ docs/                       文档
  ├─ build.ps1                   构建 + 部署一条命令
  └─ README.md
```
