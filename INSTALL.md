# 安装说明

## 方法一：通过 Mod 管理器（推荐）

> 💡 **强烈建议**使用以下管理器，自动处理 BepInEx 安装、隔离模式、插件部署：

| 管理器 | 链接 | 说明 |
|--------|------|------|
| **BepInEx-Manager** | [GitHub](https://github.com/HHHEEEWWW/bepinex-manager) | 支持多游戏隔离档案，自动匹配 BepInEx 版本 |
| **r2modman** | [GitHub](https://github.com/ebkr/r2modmanPlus) · [Thunderstore](https://thunderstore.io/package/ebkr/r2modman/) | 通用 Mod 管理器，支持数百款游戏 |

### 使用 BepInEx-Manager
1. 下载安装 [BepInEx-Manager](https://github.com/HHHEEEWWW/bepinex-manager)
2. 添加 Shadow Dungeon 游戏档案
3. 将 `ShadowDungeonTrainer.dll` 放入档案的 `BepInEx/plugins/` 目录
4. 启动游戏

### 使用 r2modman
1. 下载安装 [r2modman](https://github.com/ebkr/r2modmanPlus/releases/latest)
2. 选择 Shadow Dungeon（如不支持则手动配置）
3. 将 `ShadowDungeonTrainer.dll` 放入 profile 的 `BepInEx/plugins/` 目录
4. 启动游戏

## 方法二：手动安装

1. 下载 [BepInEx 5.x (Unity Mono, Windows x64)](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
2. 解压到游戏目录
3. 启动一次游戏生成 interop
4. 将 `ShadowDungeonTrainer.dll` 复制到 `BepInEx/plugins/`
5. 重启游戏

## 使用方法

- 按 **Home** 键打开/关闭修改器面板
- 面板显示玩家所有属性的当前值
- 在输入框中输入新数值，点 **OK** 确定修改
- 点 **重置** 恢复修改前的原始值
- 右上角 **中/EN** 按钮切换语言
