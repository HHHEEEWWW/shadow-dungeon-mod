using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace ShadowDungeonTrainer;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin  // BepInEx 5.x Mono: BaseUnityPlugin (MonoBehaviour)
{
    public const string Guid = "com.shadowdungeon.trainer";
    public const string Name = "ShadowDungeonTrainer";
    public const string Version = "0.1.0";

    private void Awake()
    {
        Logger.LogInfo($"[{Name}] Plugin loaded! Guid={Guid}");
        Logger.LogInfo($"[{Name}] Unity version: {Application.version}");
        Logger.LogInfo($"[{Name}] Data path: {Application.dataPath}");

        // 注册 IMGUI 面板行为（Home 键切换 + 属性修改）
        TrainerManager.Init(Logger);
    }
}
