using System;
using System.Collections.Generic;
using System.Linq;
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
    private bool _useChinese = true;
    private Vector2 _scrollPos;
    private readonly List<AttrEntry> _attrs = new();
    private PlayerManager? _player;

    private readonly Dictionary<string, string> _inputBuffers = new();
    // ⚠️ 原始值：只在首次扫描时捕获一次，永不覆盖
    private readonly Dictionary<string, string> _trueOriginalValues = new();
    private bool _originalCaptured;

    private object? _talentMgr;
    private object? _saveMgr;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Home))
        {
            _showPanel = !_showPanel;
            if (_showPanel) RefreshPlayerAndAttrs();
        }
    }

    private void OnGUI()
    {
        if (!_showPanel) return;

        var panelRect = new Rect(20, 20, 760, Screen.height - 40);
        GUI.Box(panelRect, "");

        // 标题 + 中英切换
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        string title = _useChinese ? "Shadow Dungeon 修改器 (Home 切换)" : "Shadow Dungeon TRAINER (Home toggle)";
        GUI.Label(new Rect(20, 25, 640, 30), title, titleStyle);

        string langBtn = _useChinese ? "EN" : "中";
        if (GUI.Button(new Rect(680, 28, 60, 26), langBtn))
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
        GUILayout.BeginArea(new Rect(25, 60, 720, 35));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_useChinese ? "刷新当前值" : "Refresh", GUILayout.Width(100)))
            RefreshCurrentDisplayOnly();
        if (GUILayout.Button(_useChinese ? "全部应用" : "Apply All", GUILayout.Width(100)))
            ApplyAll();
        if (GUILayout.Button(_useChinese ? "恢复原始值" : "Restore Original", GUILayout.Width(110)))
            RestoreOriginal();
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
        GUI.Label(new Rect(225, headerY, 100, 20), _useChinese ? "当前值" : "Current", headerStyle);
        GUI.Label(new Rect(325, headerY, 130, 20), _useChinese ? "修改值" : "New Value", headerStyle);
        GUI.Label(new Rect(465, headerY, 50, 20), "OK", headerStyle);
        GUI.Label(new Rect(525, headerY, 50, 20), _useChinese ? "重置" : "Reset", headerStyle);

        // 滚动区域
        _scrollPos = GUI.BeginScrollView(
            new Rect(25, 120, 730, panelRect.height - 140),
            _scrollPos,
            new Rect(0, 0, 700, _attrs.Count * 32 + 60));

        float y = 5;

        // ── 分组：先画「退出保存」的属性 ──
        var savedAttrs = _attrs.Where(a => a.Save == SaveStatus.Saved).ToList();
        var notSavedAttrs = _attrs.Where(a => a.Save == SaveStatus.NotSaved).ToList();

        if (savedAttrs.Count > 0)
        {
            DrawSectionHeader(ref y, _useChinese ? "✅ 退出后保存" : "✅ Persists after exit");
            foreach (var attr in savedAttrs)
                DrawAttrRow(ref y, attr);
        }

        if (notSavedAttrs.Count > 0)
        {
            DrawSectionHeader(ref y, _useChinese ? "⚠️ 退出后重置（当前生效）" : "⚠️ Resets on exit (current session only)");
            foreach (var attr in notSavedAttrs)
                DrawAttrRow(ref y, attr);
        }

        GUI.EndScrollView();
    }

    private void DrawSectionHeader(ref float y, string text)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.8f, 0.9f, 1f) }
        };
        GUI.Label(new Rect(0, y, 600, 24), text, style);
        y += 26;
    }

    private void DrawAttrRow(ref float y, AttrEntry attr)
    {
        const float labelW = 200;
        const float valueW = 100;
        const float inputW = 130;
        const float btnW = 50;
        const float rowH = 28;
        const float gap = 4;

        string displayName = _useChinese ? attr.NameCN : attr.NameEN;
        GUI.Label(new Rect(0, y, labelW, rowH), displayName);

        var currentVal = attr.GetValue(_player!);
        GUI.Label(new Rect(labelW, y, valueW, rowH), currentVal);

        if (!_inputBuffers.ContainsKey(attr.Key))
            _inputBuffers[attr.Key] = currentVal;
        _inputBuffers[attr.Key] = GUI.TextField(
            new Rect(labelW + valueW, y, inputW, rowH),
            _inputBuffers[attr.Key]);

        if (GUI.Button(new Rect(labelW + valueW + inputW + gap, y, btnW, rowH), "OK"))
        {
            if (attr.TrySetValue(_player!, _inputBuffers[attr.Key]))
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} -> {1}", displayName, _inputBuffers[attr.Key]));
        }

        string resetLabel = _useChinese ? "重置" : "Reset";
        if (GUI.Button(new Rect(labelW + valueW + inputW + gap * 2 + btnW, y, btnW, rowH), resetLabel))
        {
            if (_trueOriginalValues.TryGetValue(attr.Key, out var orig))
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
        _trueOriginalValues.Clear();
        _originalCaptured = false;

        // ══════════════════════════════════════════
        //  ✅ 退出后保存的属性
        // ══════════════════════════════════════════

        // 基础
        S("Health",           "生命值",       "HP",              AttrType.Float);
        S("Mana",             "法力值",       "MP",              AttrType.Float);
        S("Level",            "等级",         "Level",           AttrType.Int);
        S("Xp_Total",         "总经验值",     "Total XP",        AttrType.Float);
        S("Xp_CurrentLevel",  "当前等级经验",  "Level XP",       AttrType.Float);

        // 倍率（存档中，加载不被覆盖）
        S("Damage_Bei",       "攻击倍率",     "ATK Mult",        AttrType.Float);
        S("Damage_Anti",      "伤害减免",     "DMG Reduction",   AttrType.Float);
        S("MVSpeed_Bei",      "移动速度倍率",  "Move Spd Mult",  AttrType.Float);
        S("ATSpeed_Bei",      "攻击速度倍率",  "ATK Spd Mult",   AttrType.Float);
        S("Health_Bei",       "生命倍率",     "HP Mult",         AttrType.Float);
        S("Health_Percent",   "生命百分比",   "HP Percent",      AttrType.Float);
        S("Mana_Bei",         "法力倍率",     "MP Mult",         AttrType.Float);
        S("Mana_Percent",     "法力百分比",   "MP Percent",      AttrType.Float);

        // 暴击/穿透/格挡
        S("BJrate",           "暴击率",       "Crit Rate",       AttrType.Float);
        S("BJDamage",         "暴击伤害",     "Crit Damage",     AttrType.Float);
        S("JYrate",           "穿透率",       "Penetration",     AttrType.Float);
        S("GeDang",           "格挡",         "Block",           AttrType.Float);

        // 元素倍率/穿透/抗性
        S("FireDamage_Bei",   "火伤倍率",     "Fire DMG Mult",   AttrType.Float);
        S("FrozenDamage_Bei", "冰伤倍率",     "Ice DMG Mult",    AttrType.Float);
        S("ThunderDamage_Bei","雷伤倍率",     "Thunder DMG Mult",AttrType.Float);
        S("PoisonDamage_Bei", "毒伤倍率",     "Poison DMG Mult", AttrType.Float);
        S("PhysicsDamage_Bei","物理伤倍率",   "Phys DMG Mult",   AttrType.Float);
        S("ShadowDamage_Bei", "暗影伤倍率",   "Shadow DMG Mult", AttrType.Float);

        S("FireChuan",        "火穿透",       "Fire Pen",        AttrType.Float);
        S("FrozenChuan",      "冰穿透",       "Ice Pen",         AttrType.Float);
        S("ThunderChuan",     "雷穿透",       "Thunder Pen",     AttrType.Float);
        S("PoisonChuan",      "毒穿透",       "Poison Pen",      AttrType.Float);
        S("PhysicsChuan",     "物理穿透",     "Phys Pen",        AttrType.Float);
        S("ShadowChuan",      "暗影穿透",     "Shadow Pen",      AttrType.Float);

        S("FireAnti",         "火抗",         "Fire Res",        AttrType.Float);
        S("FrozenAnti",       "冰抗",         "Ice Res",         AttrType.Float);
        S("ThunderAnti",      "雷抗",         "Thunder Res",     AttrType.Float);
        S("PoisonAnti",       "毒抗",         "Poison Res",      AttrType.Float);
        S("PhysicsAnti",      "物抗",         "Phys Res",        AttrType.Float);
        S("ShadowAnti",       "暗影抗",       "Shadow Res",      AttrType.Float);

        // 其他
        S("CoolDown",         "冷却缩减",     "CD Reduction",    AttrType.Float);
        S("ItemDrop_Rate",    "掉落率",       "Drop Rate",       AttrType.Float);
        S("EXP_Range",        "经验范围",     "EXP Range",       AttrType.Float);

        // 金币
        TryAddMoneyAttr();

        // 天赋点
        TryAddTalentAttrs();

        // ══════════════════════════════════════════
        //  ⚠️ 退出后重置的属性（当前生效）
        // ══════════════════════════════════════════

        N("Damage_Base",      "基础攻击力",    "ATK Base",       AttrType.Float);
        N("MVSpeed_Base",     "移动速度基础",  "Move Spd Base",  AttrType.Float);
        N("ATSpeed_Base",     "攻击速度基础",  "ATK Spd Base",   AttrType.Float);

        RefreshCurrentValues(true);
        TrainerManager.Log.LogInfo(string.Format("[Trainer] Found PlayerManager, {0} attributes ({1} saved, {2} temp)",
            _attrs.Count,
            _attrs.Count(a => a.Save == SaveStatus.Saved),
            _attrs.Count(a => a.Save == SaveStatus.NotSaved)));
    }

    // ── 便捷方法：保存/不保存 ──
    private void S(string field, string cn, string en, AttrType type) =>
        AddAttr(field, cn, en, type, SaveStatus.Saved);

    private void N(string field, string cn, string en, AttrType type) =>
        AddAttr(field, cn, en, type, SaveStatus.NotSaved);

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
                _attrs.Add(new AttrEntry("Talent_P_Have", "可用天赋点", "Talent Pts", AttrType.Int, SaveStatus.Saved,
                    _ => SafeReadInt(_talentMgr, pHaveField), (_, v) => false));

            if (pBaseField != null)
                _attrs.Add(new AttrEntry("Talent_P_Base", "天赋点(基础)", "Talent Base", AttrType.Int, SaveStatus.Saved,
                    _ => SafeReadInt(_talentMgr, pBaseField), (_, v) => false));
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

            _attrs.Add(new AttrEntry("Money", "金币", "Gold", AttrType.Long, SaveStatus.Saved,
                _ =>
                {
                    try { return moneyFieldRef.GetValue(invDataRef)?.ToString() ?? "0"; }
                    catch { return "0"; }
                },
                (_, v) =>
                {
                    try
                    {
                        if (long.TryParse(v, out var m)) { moneyFieldRef.SetValue(invDataRef, m); return true; }
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

    private void RefreshCurrentDisplayOnly()
    {
        if (_player == null) return;
        var fresh = FindObjectOfType<PlayerManager>();
        if (fresh != null) _player = fresh;
        TrainerManager.Log.LogInfo("[Trainer] Current values refreshed");
    }

    private void RefreshCurrentValues(bool recordOriginal = false)
    {
        if (_player == null) return;
        foreach (var attr in _attrs)
        {
            var val = attr.GetValue(_player);
            _inputBuffers[attr.Key] = val;
            // ⚠️ 原始值只捕获一次，之后永不覆盖
            if (recordOriginal && !_originalCaptured)
                _trueOriginalValues[attr.Key] = val;
        }
        if (recordOriginal) _originalCaptured = true;
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
            if (_trueOriginalValues.TryGetValue(attr.Key, out var orig))
            {
                _inputBuffers[attr.Key] = orig;
                attr.TrySetValue(_player, orig);
            }
        }
        TrainerManager.Log.LogInfo("[Trainer] All attributes reset to original");
    }

    /// <summary>
    /// 恢复所有属性到首次扫描时的原始值（完全未修改的状态）。
    /// </summary>
    private void RestoreOriginal()
    {
        if (_player == null) return;
        foreach (var attr in _attrs)
        {
            if (_trueOriginalValues.TryGetValue(attr.Key, out var orig))
            {
                _inputBuffers[attr.Key] = orig;
                attr.TrySetValue(_player, orig);
            }
        }
        TrainerManager.Log.LogInfo("[Trainer] All attributes restored to TRUE original (first scan)");
    }

    private void AddAttr(string field, string nameCN, string nameEN, AttrType type, SaveStatus save)
    {
        _attrs.Add(new AttrEntry(field, nameCN, nameEN, type, save,
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
    public SaveStatus Save { get; }
    private readonly Func<PlayerManager, string> _getter;
    private readonly Func<PlayerManager, string, bool> _setter;

    public AttrEntry(string key, string nameCN, string nameEN, AttrType type, SaveStatus save,
        Func<PlayerManager, string> getter,
        Func<PlayerManager, string, bool> setter)
    {
        Key = key;
        NameCN = nameCN;
        NameEN = nameEN;
        Type = type;
        Save = save;
        _getter = getter;
        _setter = setter;
    }

    public string GetValue(PlayerManager p) => _getter(p);
    public bool TrySetValue(PlayerManager p, string v) => _setter(p, v);
}

public enum AttrType { Int, Long, Float, Bool }
public enum SaveStatus { Saved, NotSaved }
