using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ShadowDungeonTrainer;

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
        log.LogInfo("[Trainer] HUD created");
    }
}

public class TrainerBehaviour : MonoBehaviour
{
    private bool _showPanel;
    private bool _useChinese = true;
    private Vector2 _scrollPos;
    private readonly List<AttrEntry> _attrs = new();
    private PlayerManager? _player;

    // 核心：基准值（确认后的状态）+ 修改器值（用户输入的）
    private readonly Dictionary<string, string> _baseValues = new();   // 基准 = 上一次确认的真实值
    private readonly Dictionary<string, string> _inputBuffers = new(); // 修改器输入框

    private GUIStyle? _labelStyle, _headerStyle, _titleStyle, _sectionStyle;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Home))
        {
            _showPanel = !_showPanel;
            if (_showPanel) FullScan();
        }
    }

    private void OnGUI()
    {
        if (!_showPanel) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
        }
        GUI.skin.textField.fontSize = 15;
        GUI.skin.button.fontSize = 14;

        var panelRect = new Rect(20, 20, 780, Screen.height - 40);
        GUI.Box(panelRect, "");

        // 标题
        string title = _useChinese ? "Shadow Dungeon 修改器 (Home 切换)" : "Shadow Dungeon TRAINER (Home toggle)";
        GUI.Label(new Rect(20, 25, 660, 35), title, _titleStyle);
        if (GUI.Button(new Rect(690, 28, 60, 26), _useChinese ? "EN" : "中"))
            _useChinese = !_useChinese;

        if (_player == null)
        {
            GUI.Label(new Rect(20, 70, 700, 30), _useChinese ? "未检测到玩家，请先进入游戏关卡" : "PlayerManager not found");
            if (GUI.Button(new Rect(20, 110, 120, 30), _useChinese ? "重新扫描" : "Rescan"))
                FullScan();
            return;
        }

        // 按钮栏
        GUILayout.BeginArea(new Rect(25, 62, 740, 36));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_useChinese ? "刷新基准" : "Sync Base", GUILayout.Width(110)))
            SyncBaseToCurrent();
        if (GUILayout.Button(_useChinese ? "全部应用" : "Apply All", GUILayout.Width(100)))
            ApplyAll();
        if (GUILayout.Button(_useChinese ? "全部重置" : "Reset All", GUILayout.Width(100)))
            ResetAll();
        GUILayout.FlexibleSpace();
        GUILayout.Label(string.Format("Lv.{0}  HP:{1:F0}  MP:{2:F0}", _player.Level, _player.Health, _player.Mana), GUILayout.Width(300));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // 表头
        GUI.Label(new Rect(25, 102, 220, 24), _useChinese ? "属性名" : "Attribute", _headerStyle);
        GUI.Label(new Rect(245, 102, 110, 24), _useChinese ? "当前值" : "Current", _headerStyle);
        GUI.Label(new Rect(355, 102, 140, 24), _useChinese ? "修改值" : "New Value", _headerStyle);
        GUI.Label(new Rect(505, 102, 55, 24), "OK", _headerStyle);
        GUI.Label(new Rect(570, 102, 55, 24), _useChinese ? "重置" : "Reset", _headerStyle);

        // 滚动列表
        _scrollPos = GUI.BeginScrollView(
            new Rect(25, 130, 740, panelRect.height - 150),
            _scrollPos,
            new Rect(0, 0, 710, _attrs.Count * 36 + 70));

        float y = 5;
        var savedAttrs = _attrs.Where(a => a.Save == SaveStatus.Saved).ToList();
        var notSavedAttrs = _attrs.Where(a => a.Save == SaveStatus.NotSaved).ToList();

        if (savedAttrs.Count > 0)
        {
            DrawSection(ref y, _useChinese ? "✅ 退出后保存" : "✅ Persists after exit");
            foreach (var a in savedAttrs) DrawRow(ref y, a);
        }
        if (notSavedAttrs.Count > 0)
        {
            DrawSection(ref y, _useChinese ? "⚠️ 退出后重置（当前生效）" : "⚠️ Resets on exit");
            foreach (var a in notSavedAttrs) DrawRow(ref y, a);
        }

        GUI.EndScrollView();
    }

    // ── 分组标题 ──
    private void DrawSection(ref float y, string text)
    {
        GUI.Label(new Rect(0, y, 600, 28), text, _sectionStyle);
        y += 32;
    }

    // ── 属性行 ──
    private void DrawRow(ref float y, AttrEntry attr)
    {
        const float LW = 220, VW = 110, IW = 140, BW = 55, RH = 32, G = 4;

        string name = _useChinese ? attr.NameCN : attr.NameEN;
        GUI.Label(new Rect(0, y, LW, RH), name, _labelStyle);

        string current = attr.GetValue(_player!);
        GUI.Label(new Rect(LW, y, VW, RH), current, _labelStyle);

        if (!_inputBuffers.ContainsKey(attr.Key))
            _inputBuffers[attr.Key] = current;
        _inputBuffers[attr.Key] = GUI.TextField(new Rect(LW + VW, y, IW, RH), _inputBuffers[attr.Key]);

        // OK：记录基准 = 改之前的值，然后应用
        if (GUI.Button(new Rect(LW + VW + IW + G, y, BW, RH), "OK"))
        {
            string before = attr.GetValue(_player!);
            if (attr.TrySetValue(_player!, _inputBuffers[attr.Key]))
            {
                _baseValues[attr.Key] = before;
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0}: {1} -> {2}", name, before, _inputBuffers[attr.Key]));
            }
        }

        // 重置：回到基准值
        if (GUI.Button(new Rect(LW + VW + IW + G * 2 + BW, y, BW, RH), _useChinese ? "重置" : "Reset"))
        {
            if (_baseValues.TryGetValue(attr.Key, out var bas))
            {
                _inputBuffers[attr.Key] = bas;
                attr.TrySetValue(_player!, bas);
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} reset -> {1}", name, bas));
            }
        }

        y += RH + G;
    }

    // ── 全量扫描（首次进入 / 重新扫描）──
    private void FullScan()
    {
        _player = FindObjectOfType<PlayerManager>();
        if (_player == null) { TrainerManager.Log.LogWarning("[Trainer] PlayerManager not found"); return; }

        _attrs.Clear();
        _baseValues.Clear();
        _inputBuffers.Clear();

        // ══ ✅ 退出后保存 ══
        S("Health",           "生命值",       "HP",              AttrType.Float);
        S("Mana",             "法力值",       "MP",              AttrType.Float);
        S("Level",            "等级",         "Level",           AttrType.Int);
        S("Xp_Total",         "总经验值",     "Total XP",        AttrType.Float);
        S("Xp_CurrentLevel",  "当前等级经验",  "Level XP",       AttrType.Float);
        S("Damage_Bei",       "攻击倍率",     "ATK Mult",        AttrType.Float);
        S("Damage_Anti",      "伤害减免",     "DMG Reduction",   AttrType.Float);
        S("MVSpeed_Bei",      "移动速度倍率",  "Move Spd Mult",  AttrType.Float);
        S("ATSpeed_Bei",      "攻击速度倍率",  "ATK Spd Mult",   AttrType.Float);
        S("Health_Bei",       "生命倍率",     "HP Mult",         AttrType.Float);
        S("Health_Percent",   "生命百分比",   "HP Percent",      AttrType.Float);
        S("Mana_Bei",         "法力倍率",     "MP Mult",         AttrType.Float);
        S("Mana_Percent",     "法力百分比",   "MP Percent",      AttrType.Float);
        S("BJrate",           "暴击率",       "Crit Rate",       AttrType.Float);
        S("BJDamage",         "暴击伤害",     "Crit Damage",     AttrType.Float);
        S("JYrate",           "穿透率",       "Penetration",     AttrType.Float);
        S("GeDang",           "格挡",         "Block",           AttrType.Float);
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
        S("CoolDown",         "冷却缩减",     "CD Reduction",    AttrType.Float);
        S("ItemDrop_Rate",    "掉落率",       "Drop Rate",       AttrType.Float);
        S("EXP_Range",        "经验范围",     "EXP Range",       AttrType.Float);
        TryAddMoneyAttr();
        TryAddTalentAttrs();

        // ══ ⚠️ 退出后重置 ══
        N("Damage_Base",      "基础攻击力",    "ATK Base",       AttrType.Float);
        N("MVSpeed_Base",     "移动速度基础",  "Move Spd Base",  AttrType.Float);
        N("ATSpeed_Base",     "攻击速度基础",  "ATK Spd Base",   AttrType.Float);

        // 基准值 = 当前真实值
        foreach (var a in _attrs)
            _baseValues[a.Key] = a.GetValue(_player);

        TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} attrs registered", _attrs.Count));
    }

    // ── 刷新基准：把基准值更新为当前真实状态（含升级/换装备）──
    private void SyncBaseToCurrent()
    {
        if (_player == null) return;
        var fresh = FindObjectOfType<PlayerManager>();
        if (fresh != null) _player = fresh;
        foreach (var a in _attrs)
        {
            string val = a.GetValue(_player);
            _baseValues[a.Key] = val;
            _inputBuffers[a.Key] = val;
        }
        TrainerManager.Log.LogInfo("[Trainer] Base synced to current state");
    }

    // ── 全部应用 ──
    private void ApplyAll()
    {
        if (_player == null) return;
        foreach (var a in _attrs)
        {
            if (_inputBuffers.TryGetValue(a.Key, out var input))
            {
                string before = a.GetValue(_player);
                if (a.TrySetValue(_player, input))
                    _baseValues[a.Key] = before;
            }
        }
        TrainerManager.Log.LogInfo("[Trainer] All applied");
    }

    // ── 全部重置：回到基准值 ──
    private void ResetAll()
    {
        if (_player == null) return;
        foreach (var a in _attrs)
        {
            if (_baseValues.TryGetValue(a.Key, out var bas))
            {
                _inputBuffers[a.Key] = bas;
                a.TrySetValue(_player, bas);
            }
        }
        TrainerManager.Log.LogInfo("[Trainer] All reset to base");
    }

    // ── 便捷方法 ──
    private void S(string f, string cn, string en, AttrType t) => AddAttr(f, cn, en, t, SaveStatus.Saved);
    private void N(string f, string cn, string en, AttrType t) => AddAttr(f, cn, en, t, SaveStatus.NotSaved);

    private void AddAttr(string field, string cn, string en, AttrType type, SaveStatus save)
    {
        _attrs.Add(new AttrEntry(field, cn, en, type, save,
            p => ReadField(p, field, type),
            (p, v) => WriteField(p, field, type, v)));
    }

    private void TryAddTalentAttrs()
    {
        try
        {
            var t = typeof(PlayerManager).Assembly.GetType("TalentManager");
            if (t == null) return;
            var ip = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                  ?? t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var inst = ip?.GetValue(null);
            if (inst == null) return;
            var hf = t.GetField("P_Have", BindingFlags.Public | BindingFlags.Instance);
            var bf = t.GetField("P_Base", BindingFlags.Public | BindingFlags.Instance);
            if (hf != null) _attrs.Add(new AttrEntry("Talent_P_Have", "可用天赋点", "Talent Pts", AttrType.Int, SaveStatus.Saved,
                _ => SafeRead(inst, hf), (_, v) => false));
            if (bf != null) _attrs.Add(new AttrEntry("Talent_P_Base", "天赋点(基础)", "Talent Base", AttrType.Int, SaveStatus.Saved,
                _ => SafeRead(inst, bf), (_, v) => false));
        }
        catch { }
    }

    private void TryAddMoneyAttr()
    {
        try
        {
            var st = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (st == null) return;
            var ip = st.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                  ?? st.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var sm = ip?.GetValue(null);
            if (sm == null) return;
            var rd = st.GetProperty("RuntimeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sm);
            if (rd == null) return;
            var inv = rd.GetType().GetProperty("InventoryData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rd);
            if (inv == null) return;
            var mf = inv.GetType().GetField("Money", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mf == null) return;
            var iref = inv; var mref = mf;
            _attrs.Add(new AttrEntry("Money", "金币", "Gold", AttrType.Long, SaveStatus.Saved,
                _ => { try { return mref.GetValue(iref)?.ToString() ?? "0"; } catch { return "0"; } },
                (_, v) => { try { if (long.TryParse(v, out var m)) { mref.SetValue(iref, m); return true; } } catch { } return false; }));
        }
        catch { }
    }

    private static string SafeRead(object o, FieldInfo f) { try { return f.GetValue(o)?.ToString() ?? "0"; } catch { return "0"; } }

    private static string ReadField(PlayerManager p, string field, AttrType type)
    {
        try
        {
            var f = typeof(PlayerManager).GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return "?";
            var v = f.GetValue(p);
            return type switch
            {
                AttrType.Int => ((int)v).ToString(),
                AttrType.Long => ((long)v).ToString(),
                AttrType.Float => ((float)v).ToString("F2"),
                AttrType.Bool => ((bool)v).ToString(),
                _ => v?.ToString() ?? "?"
            };
        }
        catch { return "?"; }
    }

    private static bool WriteField(PlayerManager p, string field, AttrType type, string value)
    {
        try
        {
            var f = typeof(PlayerManager).GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            object? b = type switch
            {
                AttrType.Int => int.TryParse(value, out var iv) ? iv : null,
                AttrType.Long => long.TryParse(value, out var lv) ? lv : null,
                AttrType.Float => float.TryParse(value, out var fv) ? fv : null,
                AttrType.Bool => bool.TryParse(value, out var bv) ? bv : null,
                _ => null
            };
            if (b == null) return false;
            f.SetValue(p, b);
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

    public AttrEntry(string key, string cn, string en, AttrType type, SaveStatus save,
        Func<PlayerManager, string> getter, Func<PlayerManager, string, bool> setter)
    {
        Key = key; NameCN = cn; NameEN = en; Type = type; Save = save;
        _getter = getter; _setter = setter;
    }

    public string GetValue(PlayerManager p) => _getter(p);
    public bool TrySetValue(PlayerManager p, string v) => _setter(p, v);
}

public enum AttrType { Int, Long, Float, Bool }
public enum SaveStatus { Saved, NotSaved }
