using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ShadowDungeonTrainer;

/// <summary>
/// 修改器核心管理器：创建 IMGUI MonoBehaviour，Home 键切换面板，
/// 读取 PlayerManager 属性 → 行列布局 → 输入框 → 确定/重置。
/// </summary>
public static class TrainerManager
{
    private static ManualLogSource _log = null!;
    private static bool _initialized;

    internal static ManualLogSource Log => _log;

    public static void Init(ManualLogSource log)
    {
        if (_initialized) return;
        _initialized = true;
        _log = log;

        var go = new GameObject("ShadowDungeonTrainer_HUD");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<TrainerBehaviour>();
        log.LogInfo("[Trainer] HUD GameObject created");
    }
}

/// <summary>
/// MonoBehaviour 宿主：每帧检测 Home 键 + IMGUI 渲染。
/// </summary>
public class TrainerBehaviour : MonoBehaviour
{
    private bool _showPanel;
    private bool _useChinese = true;  // 默认中文
    private Vector2 _scrollPos;
    private readonly List<AttrEntry> _attrs = new();
    private PlayerManager? _player;

    // 输入缓冲 & 原始值
    private readonly Dictionary<string, string> _inputBuffers = new();
    private readonly Dictionary<string, string> _originalValues = new();

    // 缓存的单例引用（通过反射获取）
    private object? _talentMgr;
    private object? _saveMgr;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Home))
        {
            _showPanel = !_showPanel;
            if (_showPanel) RefreshPlayerAndAttrs();
        }
        // 不再自动刷新——避免覆盖用户正在输入的内容
        // 用户需要刷新时手动点「重新扫描属性」
    }

    private void OnGUI()
    {
        if (!_showPanel) return;

        var panelRect = new Rect(20, 20, 720, Screen.height - 40);
        GUI.Box(panelRect, "");

        // 标题 + 中英切换按钮
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        string title = _useChinese ? "Shadow Dungeon 修改器 (Home 切换)" : "Shadow Dungeon TRAINER (Home toggle)";
        GUI.Label(new Rect(20, 25, 600, 30), title, titleStyle);

        // 中/EN 切换按钮
        string langBtn = _useChinese ? "EN" : "中";
        if (GUI.Button(new Rect(660, 28, 60, 26), langBtn))
            _useChinese = !_useChinese;

        if (_player == null)
        {
            string msg = _useChinese ? "未检测到玩家，请先进入游戏关卡" : "PlayerManager not found - enter a game level first";
            GUI.Label(new Rect(20, 70, 700, 30), msg);
            string rescanLabel = _useChinese ? "重新扫描" : "Rescan";
            if (GUI.Button(new Rect(20, 110, 120, 30), rescanLabel))
                RefreshPlayerAndAttrs();
            return;
        }

        // 按钮栏
        GUILayout.BeginArea(new Rect(25, 60, 690, 35));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_useChinese ? "刷新当前值" : "Refresh Values", GUILayout.Width(120)))
            RefreshCurrentDisplayOnly();
        if (GUILayout.Button(_useChinese ? "全部应用" : "Apply All", GUILayout.Width(100)))
            ApplyAll();
        if (GUILayout.Button(_useChinese ? "全部重置" : "Reset All", GUILayout.Width(100)))
            ResetAll();
        GUILayout.FlexibleSpace();
        GUILayout.Label(string.Format("Lv.{0}  HP:{1:F0}  MP:{2:F0}", _player.Level, _player.Health, _player.Mana), GUILayout.Width(300));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // 表头
        float headerY = 98;
        var headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(25, headerY, 200, 20), _useChinese ? "属性名" : "Attribute", headerStyle);
        GUI.Label(new Rect(225, headerY, 120, 20), _useChinese ? "当前值" : "Current", headerStyle);
        GUI.Label(new Rect(345, headerY, 150, 20), _useChinese ? "修改值" : "New Value", headerStyle);
        GUI.Label(new Rect(500, headerY, 60, 20), "OK", headerStyle);
        GUI.Label(new Rect(570, headerY, 60, 20), _useChinese ? "重置" : "Reset", headerStyle);

        // 滚动属性列表
        _scrollPos = GUI.BeginScrollView(
            new Rect(25, 120, 690, panelRect.height - 140),
            _scrollPos,
            new Rect(0, 0, 660, _attrs.Count * 32 + 10));

        float y = 5;
        foreach (var attr in _attrs)
        {
            DrawAttrRow(ref y, attr);
        }

        GUI.EndScrollView();
    }

    private void DrawAttrRow(ref float y, AttrEntry attr)
    {
        const float labelW = 200;
        const float valueW = 120;
        const float inputW = 150;
        const float btnW = 60;
        const float rowH = 28;
        const float gap = 4;

        // 属性名（根据语言切换）
        string displayName = _useChinese ? attr.NameCN : attr.NameEN;
        GUI.Label(new Rect(0, y, labelW, rowH), displayName);

        // 当前值
        var currentVal = attr.GetValue(_player!);
        GUI.Label(new Rect(labelW, y, valueW, rowH), currentVal);

        // 输入框
        if (!_inputBuffers.ContainsKey(attr.Key))
            _inputBuffers[attr.Key] = currentVal;
        _inputBuffers[attr.Key] = GUI.TextField(
            new Rect(labelW + valueW, y, inputW, rowH),
            _inputBuffers[attr.Key]);

        // OK 按钮
        if (GUI.Button(new Rect(labelW + valueW + inputW + gap, y, btnW, rowH), "OK"))
        {
            if (attr.TrySetValue(_player!, _inputBuffers[attr.Key]))
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} -> {1}", displayName, _inputBuffers[attr.Key]));
        }

        // 重置按钮
        string resetLabel = _useChinese ? "重置" : "Reset";
        if (GUI.Button(new Rect(labelW + valueW + inputW + gap * 2 + btnW, y, btnW, rowH), resetLabel))
        {
            if (_originalValues.TryGetValue(attr.Key, out var orig))
            {
                _inputBuffers[attr.Key] = orig;
                attr.TrySetValue(_player!, orig);
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} reset -> {1}", displayName, orig));
            }
        }

        y += rowH + gap;
    }

    private void RefreshPlayerAndAttrs()
    {
        _player = FindObjectOfType<PlayerManager>();
        if (_player == null)
        {
            TrainerManager.Log.LogWarning("[Trainer] PlayerManager not found");
            return;
        }

        _attrs.Clear();
        _inputBuffers.Clear();
        _originalValues.Clear();

        // ── 基础属性 ──
        AddAttr("Health",           "生命值",     "HP",              AttrType.Float);
        AddAttr("Mana",             "法力值",     "MP",              AttrType.Float);
        AddAttr("Level",            "等级",       "Level",           AttrType.Int);
        AddAttr("Xp_Total",         "总经验值",   "Total XP",        AttrType.Float);
        AddAttr("Xp_CurrentLevel",  "当前等级经验","Level XP",       AttrType.Float);

        // ── 攻击 ──
        AddAttr("Damage_Base",      "基础攻击力",  "ATK Base",       AttrType.Float);
        AddAttr("Damage_Bei",       "攻击倍率",    "ATK Multiplier", AttrType.Float);
        AddAttr("Damage_Anti",      "伤害减免",    "DMG Reduction",  AttrType.Float);

        // ── 速度 ──
        AddAttr("MVSpeed_Base",     "移动速度基础","Move Speed Base",AttrType.Float);
        AddAttr("MVSpeed_Bei",      "移动速度倍率","Move Speed Mult",AttrType.Float);
        AddAttr("ATSpeed_Base",     "攻击速度基础","ATK Speed Base", AttrType.Float);
        AddAttr("ATSpeed_Bei",      "攻击速度倍率","ATK Speed Mult", AttrType.Float);

        // ── 暴击/穿透 ──
        AddAttr("BJrate",           "暴击率",     "Crit Rate",       AttrType.Float);
        AddAttr("BJDamage",         "暴击伤害",   "Crit Damage",     AttrType.Float);
        AddAttr("JYrate",           "穿透率",     "Penetration",     AttrType.Float);
        AddAttr("GeDang",           "格挡",       "Block",           AttrType.Float);

        // ── 生命/法力倍率 ──
        AddAttr("Health_Bei",       "生命倍率",   "HP Multiplier",   AttrType.Float);
        AddAttr("Health_Percent",   "生命百分比", "HP Percent",      AttrType.Float);
        AddAttr("Mana_Bei",         "法力倍率",   "MP Multiplier",   AttrType.Float);
        AddAttr("Mana_Percent",     "法力百分比", "MP Percent",      AttrType.Float);

        // ── 元素伤害倍率 ──
        AddAttr("FireDamage_Bei",   "火伤倍率",   "Fire DMG Mult",   AttrType.Float);
        AddAttr("FrozenDamage_Bei", "冰伤倍率",   "Ice DMG Mult",    AttrType.Float);
        AddAttr("ThunderDamage_Bei","雷伤倍率",   "Thunder DMG Mult",AttrType.Float);
        AddAttr("PoisonDamage_Bei", "毒伤倍率",   "Poison DMG Mult", AttrType.Float);
        AddAttr("PhysicsDamage_Bei","物理伤倍率", "Phys DMG Mult",   AttrType.Float);
        AddAttr("ShadowDamage_Bei", "暗影伤倍率", "Shadow DMG Mult", AttrType.Float);

        // ── 元素穿透 ──
        AddAttr("FireChuan",        "火穿透",     "Fire Pen",        AttrType.Float);
        AddAttr("FrozenChuan",      "冰穿透",     "Ice Pen",         AttrType.Float);
        AddAttr("ThunderChuan",     "雷穿透",     "Thunder Pen",     AttrType.Float);
        AddAttr("PoisonChuan",      "毒穿透",     "Poison Pen",      AttrType.Float);
        AddAttr("PhysicsChuan",     "物理穿透",   "Phys Pen",        AttrType.Float);
        AddAttr("ShadowChuan",      "暗影穿透",   "Shadow Pen",      AttrType.Float);

        // ── 元素抗性 ──
        AddAttr("FireAnti",         "火抗",       "Fire Res",        AttrType.Float);
        AddAttr("FrozenAnti",       "冰抗",       "Ice Res",         AttrType.Float);
        AddAttr("ThunderAnti",      "雷抗",       "Thunder Res",     AttrType.Float);
        AddAttr("PoisonAnti",       "毒抗",       "Poison Res",      AttrType.Float);
        AddAttr("PhysicsAnti",      "物抗",       "Phys Res",        AttrType.Float);
        AddAttr("ShadowAnti",       "暗影抗",     "Shadow Res",      AttrType.Float);

        // ── 掉落/冷却 ──
        AddAttr("ItemDrop_Rate",    "掉落率",     "Drop Rate",       AttrType.Float);
        AddAttr("EXP_Range",        "经验范围",   "EXP Range",       AttrType.Float);
        AddAttr("CoolDown",         "冷却缩减",   "CD Reduction",    AttrType.Float);

        // ── 天赋点（通过反射获取单例） ──
        TryAddTalentAttrs();

        // ── 金币（通过反射获取单例） ──
        TryAddMoneyAttr();

        // 记录原始值
        RefreshCurrentValues(true);
        TrainerManager.Log.LogInfo(string.Format("[Trainer] Found PlayerManager, {0} attributes registered", _attrs.Count));
    }

    private void TryAddTalentAttrs()
    {
        try
        {
            var talentType = typeof(PlayerManager).Assembly.GetType("TalentManager");
            if (talentType == null) return;
            var instProp = talentType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (instProp == null) instProp = talentType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            _talentMgr = instProp?.GetValue(null);
            if (_talentMgr == null) return;

            var pHaveField = talentType.GetField("P_Have", BindingFlags.Public | BindingFlags.Instance);
            var pBaseField = talentType.GetField("P_Base", BindingFlags.Public | BindingFlags.Instance);

            if (pHaveField != null)
                _attrs.Add(new AttrEntry("Talent_P_Have", "可用天赋点", "Talent Pts", AttrType.Int,
                    _ => SafeReadInt(_talentMgr, pHaveField),
                    (_, v) => false));

            if (pBaseField != null)
                _attrs.Add(new AttrEntry("Talent_P_Base", "天赋点(基础)", "Talent Base", AttrType.Int,
                    _ => SafeReadInt(_talentMgr, pBaseField),
                    (_, v) => false));
        }
        catch (Exception e)
        {
            TrainerManager.Log.LogWarning("[Trainer] TalentManager access failed: " + e.Message);
        }
    }

    private void TryAddMoneyAttr()
    {
        try
        {
            var saveMgrType = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (saveMgrType == null) return;
            var instProp = saveMgrType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (instProp == null) instProp = saveMgrType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            _saveMgr = instProp?.GetValue(null);
            if (_saveMgr == null) return;

            var runtimeDataProp = saveMgrType.GetProperty("RuntimeData",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var runtimeData = runtimeDataProp?.GetValue(_saveMgr);
            if (runtimeData == null) return;

            var invDataProp = runtimeData.GetType().GetProperty("InventoryData",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var invData = invDataProp?.GetValue(runtimeData);
            if (invData == null) return;

            var moneyField = invData.GetType().GetField("Money",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (moneyField == null) return;

            var invDataRef = invData;
            var moneyFieldRef = moneyField;

            _attrs.Add(new AttrEntry("Money", "金币", "Gold", AttrType.Long,
                _ =>
                {
                    try { return moneyFieldRef.GetValue(invDataRef)?.ToString() ?? "0"; }
                    catch { return "0"; }
                },
                (_, v) =>
                {
                    try
                    {
                        if (long.TryParse(v, out var m))
                        {
                            moneyFieldRef.SetValue(invDataRef, m);
                            return true;
                        }
                    }
                    catch { }
                    return false;
                }));
        }
        catch (Exception e)
        {
            TrainerManager.Log.LogWarning("[Trainer] SaveManager access failed: " + e.Message);
        }
    }

    private static string SafeReadInt(object obj, FieldInfo f)
    {
        try { return f.GetValue(obj)?.ToString() ?? "0"; }
        catch { return "0"; }
    }

    private void RefreshCurrentValues(bool recordOriginal = false)
    {
        if (_player == null) return;
        foreach (var attr in _attrs)
        {
            var val = attr.GetValue(_player);
            _inputBuffers[attr.Key] = val;
            if (recordOriginal)
                _originalValues[attr.Key] = val;
        }
    }

    private void ApplyAll()
    {
        if (_player == null) return;
        foreach (var attr in _attrs)
        {
            if (_inputBuffers.TryGetValue(attr.Key, out var input))
                attr.TrySetValue(_player, input);
        }
        TrainerManager.Log.LogInfo("[Trainer] All attributes applied");
    }

    private void ResetAll()
    {
        if (_player == null) return;
        foreach (var attr in _attrs)
        {
            if (_originalValues.TryGetValue(attr.Key, out var orig))
            {
                _inputBuffers[attr.Key] = orig;
                attr.TrySetValue(_player, orig);
            }
        }
        TrainerManager.Log.LogInfo("[Trainer] All attributes reset to original");
    }

    /// <summary>
    /// 只刷新"当前值"列的显示，不触碰用户输入框和原始值。
    /// </summary>
    private void RefreshCurrentDisplayOnly()
    {
        if (_player == null) return;
        // 重新获取 PlayerManager 实例（可能场景切换后变了）
        var fresh = FindObjectOfType<PlayerManager>();
        if (fresh != null) _player = fresh;
        // 不清空 _inputBuffers 和 _originalValues——用户正在输入的内容保留
        TrainerManager.Log.LogInfo("[Trainer] Current values refreshed");
    }

    private void AddAttr(string field, string nameCN, string nameEN, AttrType type)
    {
        _attrs.Add(new AttrEntry(field, nameCN, nameEN, type,
            p => ReadField(p, field, type),
            (p, v) => WriteField(p, field, type, v)));
    }

    private static string ReadField(PlayerManager p, string field, AttrType type)
    {
        try
        {
            var f = typeof(PlayerManager).GetField(field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return "?";
            var val = f.GetValue(p);
            return type switch
            {
                AttrType.Int => ((int)val).ToString(),
                AttrType.Long => ((long)val).ToString(),
                AttrType.Float => ((float)val).ToString("F2"),
                AttrType.Bool => ((bool)val).ToString(),
                _ => val?.ToString() ?? "?"
            };
        }
        catch { return "?"; }
    }

    private static bool WriteField(PlayerManager p, string field, AttrType type, string value)
    {
        try
        {
            var f = typeof(PlayerManager).GetField(field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            object? boxed = type switch
            {
                AttrType.Int => int.TryParse(value, out var iv) ? iv : null,
                AttrType.Long => long.TryParse(value, out var lv) ? lv : null,
                AttrType.Float => float.TryParse(value, out var fv) ? fv : null,
                AttrType.Bool => bool.TryParse(value, out var bv) ? bv : null,
                _ => null
            };
            if (boxed == null) return false;
            f.SetValue(p, boxed);
            return true;
        }
        catch { return false; }
    }
}

public class AttrEntry
{
    public string Key { get; }
    public string NameCN { get; }
    public string NameEN { get; }
    public AttrType Type { get; }
    private readonly Func<PlayerManager, string> _getter;
    private readonly Func<PlayerManager, string, bool> _setter;

    public AttrEntry(string key, string nameCN, string nameEN, AttrType type,
        Func<PlayerManager, string> getter,
        Func<PlayerManager, string, bool> setter)
    {
        Key = key;
        NameCN = nameCN;
        NameEN = nameEN;
        Type = type;
        _getter = getter;
        _setter = setter;
    }

    public string GetValue(PlayerManager p) => _getter(p);
    public bool TrySetValue(PlayerManager p, string v) => _setter(p, v);
}

public enum AttrType
{
    Int,
    Long,
    Float,
    Bool
}
