# Shadow Dungeon 修改器 MOD

为 Steam 游戏《Shadow Dungeon》（OO Cat，Unity 2019.4.39 Mono）开发的 BepInEx 5.x 修改器插件。

## 功能

- **Home 键**切换修改器面板
- 读取并显示玩家所有属性（HP、MP、等级、攻击力、速度、暴击、元素6系等）
- 每个属性右侧输入框，输入新数值后点 **OK** 确定修改
- **重置**按钮恢复为修改前的原始值
- **全部应用** / **全部重置** 批量操作
- **中/EN** 切换按钮，支持中英双语界面
- 金币、天赋点修改（通过 SaveManager / TalentManager 单例）

## 支持修改的属性

| 分类 | 属性 |
|------|------|
| 基础 | HP、MP、等级、总经验、当前等级经验 |
| 攻击 | 基础攻击力、攻击倍率、伤害减免 |
| 速度 | 移动速度基础/倍率、攻击速度基础/倍率 |
| 暴击 | 暴击率、暴击伤害、穿透率、格挡 |
| 生命/法力 | 生命倍率、生命百分比、法力倍率、法力百分比 |
| 元素伤害 | 火/冰/雷/毒/物理/暗影 伤害倍率 |
| 元素穿透 | 火/冰/雷/毒/物理/暗影 穿透 |
| 元素抗性 | 火/冰/雷/毒/物理/暗影 抗性 |
| 其他 | 掉落率、经验范围、冷却缩减 |
| 特殊 | 金币（Money）、可用天赋点、天赋点(基础) |

## 前置条件

- Steam 版《Shadow Dungeon》
- BepInEx 5.x（Mono 版本）— 通过 BepInEx-Manager 隔离模式管理

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
  ├─ tools/                      构建工具（gitignore）
  ├─ docs/                       文档
  ├─ build.ps1                   构建 + 部署一条命令
  └─ README.md
```
