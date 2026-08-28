using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    // 核心：每个属性的累计变化量
    private readonly Dictionary<string, float> _deltaF = new();
    private readonly Dictionary<string, int> _deltaI = new();
    private readonly Dictionary<string, long> _deltaL = new();

    private readonly Dictionary<string, string> _inputBuffers = new();
    private GUIStyle? _ls, _hs, _ts, _ss, _phs;

    private void Awake()
    {
        Application.wantsToQuit += OnQuit;
        SceneManager.sceneLoaded += OnScene;
    }

    private void OnDestroy()
    {
        Application.wantsToQuit -= OnQuit;
        SceneManager.sceneLoaded -= OnScene;
        ResetAll();
    }

    private void OnApplicationQuit()
    {
        Scan();    // 关闭前扫描一次
        ResetAll();
    }

    private bool OnQuit()
    {
        ResetAll();
        return true;
    }

    private void OnScene(Scene s, LoadSceneMode m)
    {
        ResetAll();
        _deltaF.Clear();
        _deltaI.Clear();
        _deltaL.Clear();
    }

    // ══════ 重置 ══════
    // 只重置基础字段（倍率/百分比），游戏会自动重新计算派生值（HP/攻击力等）
    // 这样退出后：Health_Bei=1（干净），HP=游戏基于1重算的真实值（包含成长）
    private void ResetAll()
    {
        if (_player == null) _player = UnityEngine.Object.FindObjectOfType<PlayerManager>();
        if (_player == null) return;
        bool any = _deltaF.Count > 0 || _deltaI.Count > 0 || _deltaL.Count > 0;
        if (!any) return;

        // 1. 重置所有修改过的 PlayerManager 运行时值
        foreach (var a in _attrs)
        {
            string cur = a.Get(_player!);
            string clean = a.SubDelta(cur, _deltaF, _deltaI, _deltaL);
            if (clean != cur) a.Set(_player!, clean);
        }

        // 2. 重置 PlayerSaveData 基础字段（让游戏重新计算）
        WriteAllToSave();

        // 3. 清空 delta
        _deltaF.Clear();
        _deltaI.Clear();
        _deltaL.Clear();

        TrainerManager.Log.LogInfo("[Trainer] Reset done: base fields cleaned, game will recalc");
    }

    // ══════ 写入 PlayerSaveData ══════
    private void WriteAllToSave()
    {
        try
        {
            var smt = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (smt == null) return;
            var ip = smt.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                  ?? smt.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var sm = ip?.GetValue(null);
            if (sm == null) return;
            var rd = smt.GetProperty("RuntimeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sm);
            if (rd == null) return;
            var pd = rd.GetType().GetProperty("PlayerData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rd);
            if (pd == null) return;
            foreach (var a in _attrs)
                a.WriteSave(pd);
        }
        catch { }
    }

    private float _lastScanTime;
    private float _lastValueRefreshTime;
    // 缓存当前值，避免每帧反射读取
    private readonly Dictionary<string, string> _currentValues = new();

    private void Update()
    {
        // Home 键切换
        if (Input.GetKeyDown(KeyCode.Home))
        {
            _showPanel = !_showPanel;
            if (_showPanel && _attrs.Count == 0) Scan();
        }

        // 缓存 PlayerManager（避免每帧 FindObjectOfType）
        if (_player == null) _player = UnityEngine.Object.FindObjectOfType<PlayerManager>();

        // 定时完整扫描（只在没有属性列表或 PlayerManager 丢失时）
        float interval = _showPanel ? 2f : 5f;
        if (Time.realtimeSinceStartup - _lastScanTime >= interval)
        {
            _lastScanTime = Time.realtimeSinceStartup;
            if (_attrs.Count == 0 || _player == null) Scan();
        }

        // 刷新缓存当前值（比 Scan 轻量，只读字段）
        if (_player != null && Time.realtimeSinceStartup - _lastValueRefreshTime >= 0.5f)
        {
            _lastValueRefreshTime = Time.realtimeSinceStartup;
            foreach (var a in _attrs)
                _currentValues[a.Key] = a.Get(_player!);
        }
    }

    private void OnGUI()
    {
        if (!_showPanel) return;
        if (_ls == null)
        {
            _ls = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            _hs = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _ts = new GUIStyle(GUI.skin.label) { fontSize = 31, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _ss = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
            _phs = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(0.5f, 0.5f, 0.5f) } };
        }
        GUI.skin.textField.fontSize = 21;
        GUI.skin.button.fontSize = 20;

        var pr = new Rect(20, 20, 1520, Screen.height - 40);
        GUI.Box(pr, "");
        GUI.Label(new Rect(20, 25, 1300, 70), _useChinese ? "Shadow Dungeon 修改器" : "Shadow Dungeon TRAINER", _ts);
        if (GUI.Button(new Rect(1380, 30, 120, 52), _useChinese ? "EN" : "中")) _useChinese = !_useChinese;

        if (_player == null)
        {
            GUI.Label(new Rect(20, 70, 700, 30), _useChinese ? "未检测到玩家" : "PlayerManager not found");
            if (GUI.Button(new Rect(20, 110, 120, 30), _useChinese ? "重新扫描" : "Rescan")) Scan();
            return;
        }

        GUILayout.BeginArea(new Rect(25, 100, 1480, 72));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_useChinese ? "全部应用" : "Apply All", GUILayout.Width(200))) ApplyAll();
        if (GUILayout.Button(_useChinese ? "全部重置" : "Reset All", GUILayout.Width(200))) { ResetAll(); foreach (var a in _attrs) _inputBuffers[a.Key] = ""; }
        GUILayout.FlexibleSpace();
        GUILayout.Label(string.Format("Lv.{0}  HP:{1:F0}  MP:{2:F0}", _player.Level, _player.Health, _player.Mana), GUILayout.Width(600));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUI.Label(new Rect(25, 180, 440, 48), _useChinese ? "属性名" : "Attribute", _hs);
        GUI.Label(new Rect(465, 180, 220, 48), _useChinese ? "当前值" : "Current", _hs);
        GUI.Label(new Rect(685, 180, 280, 48), _useChinese ? "修改值" : "New Value", _hs);
        GUI.Label(new Rect(975, 180, 110, 48), "OK", _hs);
        GUI.Label(new Rect(1095, 180, 110, 48), _useChinese ? "重置" : "Reset", _hs);

        _scrollPos = GUI.BeginScrollView(new Rect(25, 230, 1480, pr.height - 250), _scrollPos, new Rect(0, 0, 1400, _attrs.Count * 72 + 140));
        float y = 5;
        var sa = _attrs.Where(a => a.Save == SaveStatus.Saved).ToList();
        var na = _attrs.Where(a => a.Save == SaveStatus.NotSaved).ToList();
        if (sa.Count > 0) { Sec(ref y, _useChinese ? "✅ 退出后保存" : "✅ Persists"); foreach (var a in sa) Row(ref y, a); }
        if (na.Count > 0) { Sec(ref y, _useChinese ? "⚠️ 退出后重置" : "⚠️ Resets on exit"); foreach (var a in na) Row(ref y, a); }
        GUI.EndScrollView();
    }

    private void Sec(ref float y, string t) { GUI.Label(new Rect(0, y, 1200, 56), t, _ss); y += 64; }

    private void Row(ref float y, AttrEntry a)
    {
        const float LW = 308, VW = 154, IW = 196, BW = 77, RH = 45, G = 6;
        string nm = _useChinese ? a.CN : a.EN;
        // 属性名
        GUI.Label(new Rect(0, y, LW, RH), nm, _ls);
        // 当前值（从缓存读取，不反射）
        string cur = _currentValues.TryGetValue(a.Key, out var v) ? v : a.Get(_player!);
        GUI.Label(new Rect(LW, y, VW, RH), cur, _ls);
        // 输入框（只接收用户要修改的值，placeholder 提示）
        if (!_inputBuffers.ContainsKey(a.Key)) _inputBuffers[a.Key] = "";
        GUI.Label(new Rect(LW + VW, y - 2, IW, 14), _useChinese ? "输入修改值" : "New value", _phs);
        _inputBuffers[a.Key] = GUI.TextField(new Rect(LW + VW, y + 12, IW, RH - 12), _inputBuffers[a.Key]);

        // OK：delta += 输入值 - 当前值
        if (GUI.Button(new Rect(LW + VW + IW + G, y, BW, RH), "OK"))
        {
            string nv = _inputBuffers[a.Key];
            if (!string.IsNullOrEmpty(nv) && nv != cur && a.Set(_player!, nv))
            {
                a.AddDelta(_deltaF, _deltaI, _deltaL, cur, nv);
                _inputBuffers[a.Key] = ""; // 清空输入框
                TrainerManager.Log.LogInfo(string.Format("[Trainer] {0}: {1} -> {2}", nm, cur, nv));
            }
        }

        // 重置：当前值 - delta
        if (GUI.Button(new Rect(LW + VW + IW + G * 2 + BW, y, BW, RH), _useChinese ? "重置" : "Reset"))
        {
            string cur2 = a.Get(_player!);
            string clean = a.SubDelta(cur2, _deltaF, _deltaI, _deltaL);
            a.Set(_player!, clean);
            _inputBuffers[a.Key] = "";
            a.WriteSave(_player!);
            TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} reset -> {1}", nm, clean));
        }
        y += RH + G;
    }

    private void Scan()
    {
        _player = FindObjectOfType<PlayerManager>();
        if (_player == null) { TrainerManager.Log.LogWarning("[Trainer] PlayerManager not found"); return; }
        _attrs.Clear(); _inputBuffers.Clear();

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
        TryMoney();
        TryTalent();

        N("Damage_Base",      "基础攻击力",    "ATK Base",       AttrType.Float);
        N("MVSpeed_Base",     "移动速度基础",  "Move Spd Base",  AttrType.Float);
        N("ATSpeed_Base",     "攻击速度基础",  "ATK Spd Base",   AttrType.Float);

        TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} attrs", _attrs.Count));
    }

    private void ApplyAll()
    {
        if (_player == null) return;
        foreach (var a in _attrs)
        {
            if (_inputBuffers.TryGetValue(a.Key, out var v))
            {
                string old = a.Get(_player);
                if (a.Set(_player, v)) a.AddDelta(_deltaF, _deltaI, _deltaL, old, v);
            }
        }
    }

    private void S(string f, string cn, string en, AttrType t) => _attrs.Add(AttrEntry.Make(f, cn, en, t, SaveStatus.Saved));
    private void N(string f, string cn, string en, AttrType t) => _attrs.Add(AttrEntry.Make(f, cn, en, t, SaveStatus.NotSaved));

    private void TryTalent()
    {
        try
        {
            var t = typeof(PlayerManager).Assembly.GetType("TalentManager");
            if (t == null) return;
            var ip = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) ?? t.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var inst = ip?.GetValue(null);
            if (inst == null) return;
            var hf = t.GetField("P_Have", BindingFlags.Public | BindingFlags.Instance);
            var bf = t.GetField("P_Base", BindingFlags.Public | BindingFlags.Instance);
            if (hf != null) _attrs.Add(AttrEntry.Ref("Talent_P_Have", "可用天赋点", "Talent Pts", AttrType.Int, SaveStatus.Saved, inst, hf));
            if (bf != null) _attrs.Add(AttrEntry.Ref("Talent_P_Base", "天赋点(基础)", "Talent Base", AttrType.Int, SaveStatus.Saved, inst, bf));
        }
        catch { }
    }

    private void TryMoney()
    {
        try
        {
            var st = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (st == null) return;
            var ip = st.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) ?? st.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
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
                (_, v) => { try { if (long.TryParse(v, out var m)) { mref.SetValue(iref, m); return true; } } catch { } return false; },
                null, null));
        }
        catch { }
    }
}

public class AttrEntry
{
    public string Key, CN, EN;
    public AttrType Type;
    public SaveStatus Save;
    private readonly Func<PlayerManager, string> _get;
    private readonly Func<PlayerManager, string, bool> _set;
    private readonly object? _refObj;
    private readonly FieldInfo? _refField;

    public AttrEntry(string key, string cn, string en, AttrType type, SaveStatus save,
        Func<PlayerManager, string> get, Func<PlayerManager, string, bool> set, object? refObj, FieldInfo? refField)
    { Key = key; CN = cn; EN = en; Type = type; Save = save; _get = get; _set = set; _refObj = refObj; _refField = refField; }

    public static AttrEntry Make(string f, string cn, string en, AttrType t, SaveStatus s)
    {
        return new AttrEntry(f, cn, en, t, s,
            (p) => ReadPM(f, t),
            (p, v) => WritePM(f, t, v),
            null, null);
    }

    public static AttrEntry Ref(string f, string cn, string en, AttrType t, SaveStatus s, object obj, FieldInfo fi)
    {
        return new AttrEntry(f, cn, en, t, s,
            (p) => ReadRef(obj, fi, t),
            (p, v) => WriteRef(obj, fi, t, v),
            obj, fi);
    }

    public string Get(PlayerManager p) => _get(p);
    public bool Set(PlayerManager p, string v) => _set(p, v);

    // delta += 新值 - 旧值
    public void AddDelta(Dictionary<string, float> dF, Dictionary<string, int> dI, Dictionary<string, long> dL, string old, string nv)
    {
        switch (Type)
        {
            case AttrType.Float:
                if (float.TryParse(old, out var o) && float.TryParse(nv, out var n))
                { dF.TryGetValue(Key, out var c); dF[Key] = c + (n - o); }
                break;
            case AttrType.Int:
                if (int.TryParse(old, out var oi) && int.TryParse(nv, out var ni))
                { dI.TryGetValue(Key, out var c); dI[Key] = c + (ni - oi); }
                break;
            case AttrType.Long:
                if (long.TryParse(old, out var ol) && long.TryParse(nv, out var nl))
                { dL.TryGetValue(Key, out var c); dL[Key] = c + (nl - ol); }
                break;
        }
    }

    // 重置：当前值 - delta
    public string SubDelta(string cur, Dictionary<string, float> dF, Dictionary<string, int> dI, Dictionary<string, long> dL)
    {
        switch (Type)
        {
            case AttrType.Float:
                if (dF.TryGetValue(Key, out var df) && float.TryParse(cur, out var c)) return (c - df).ToString("F2");
                break;
            case AttrType.Int:
                if (dI.TryGetValue(Key, out var di) && int.TryParse(cur, out var ci)) return (ci - di).ToString();
                break;
            case AttrType.Long:
                if (dL.TryGetValue(Key, out var dl) && long.TryParse(cur, out var cl)) return (cl - dl).ToString();
                break;
        }
        return cur;
    }

    // 写入 PlayerSaveData
    public void WriteSave(object pd)
    {
        try
        {
            var f = pd.GetType().GetField(Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return;
            string val = Get(null!);
            object? b = Type switch
            {
                AttrType.Int => int.TryParse(val, out var iv) ? iv : null,
                AttrType.Long => long.TryParse(val, out var lv) ? lv : null,
                AttrType.Float => float.TryParse(val, out var fv) ? fv : null,
                _ => null
            };
            if (b != null) f.SetValue(pd, b);
        }
        catch { }
    }

    public void WriteSave(PlayerManager p)
    {
        try
        {
            var smt = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (smt == null) return;
            var ip = smt.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                  ?? smt.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var sm = ip?.GetValue(null);
            if (sm == null) return;
            var rd = smt.GetProperty("RuntimeData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sm);
            if (rd == null) return;
            var pd = rd.GetType().GetProperty("PlayerData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rd);
            if (pd != null) WriteSave(pd);
        }
        catch { }
    }

    private static string ReadPM(string f, AttrType t)
    {
        try
        {
            var fi = typeof(PlayerManager).GetField(f, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) return "?";
            var v = fi.GetValue(UnityEngine.Object.FindObjectOfType<PlayerManager>());
            return t switch { AttrType.Int => ((int)v).ToString(), AttrType.Long => ((long)v).ToString(), AttrType.Float => ((float)v).ToString("F2"), _ => v?.ToString() ?? "?" };
        }
        catch { return "?"; }
    }

    private static bool WritePM(string f, AttrType t, string v)
    {
        try
        {
            var fi = typeof(PlayerManager).GetField(f, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) return false;
            object? b = t switch { AttrType.Int => int.TryParse(v, out var iv) ? iv : null, AttrType.Long => long.TryParse(v, out var lv) ? lv : null, AttrType.Float => float.TryParse(v, out var fv) ? fv : null, AttrType.Bool => bool.TryParse(v, out var bv) ? bv : null, _ => null };
            if (b == null) return false;
            fi.SetValue(UnityEngine.Object.FindObjectOfType<PlayerManager>(), b);
            return true;
        }
        catch { return false; }
    }

    private static string ReadRef(object o, FieldInfo fi, AttrType t)
    {
        try { var v = fi.GetValue(o); return t switch { AttrType.Int => ((int)v).ToString(), AttrType.Long => ((long)v).ToString(), AttrType.Float => ((float)v).ToString("F2"), _ => v?.ToString() ?? "?" }; }
        catch { return "?"; }
    }

    private static bool WriteRef(object o, FieldInfo fi, AttrType t, string v)
    {
        try { object? b = t switch { AttrType.Int => int.TryParse(v, out var iv) ? iv : null, AttrType.Long => long.TryParse(v, out var lv) ? lv : null, AttrType.Float => float.TryParse(v, out var fv) ? fv : null, _ => null }; if (b == null) return false; fi.SetValue(o, b); return true; }
        catch { return false; }
    }
}

public enum AttrType { Int, Long, Float, Bool }
public enum SaveStatus { Saved, NotSaved }
