using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
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
    internal static TrainerBehaviour? Instance { get; private set; }

    private bool _showPanel;
    private Vector2 _scrollPos;
    private readonly List<AttrEntry> _attrs = new();
    private PlayerManager? _player;

    private readonly Dictionary<string, string> _inputBuffers = new();
    // 每个属性通过修改器累计增加的数值
    private readonly Dictionary<string, float> _deltaF = new();
    private readonly Dictionary<string, int> _deltaI = new();
    private readonly Dictionary<string, long> _deltaL = new();
    // 缓存当前值，避免每帧反射读取
    private readonly Dictionary<string, string> _currentValues = new();
    private float _lastCleanWriteTime;
    private bool _saveDataDiagLogged;
    private float _lastSaveMomentCheck;
    private GUIStyle? _ls, _hs, _ts;

    private void Awake()
    {
        Instance = this;
        Application.wantsToQuit += OnQuit;
        HarmonySaveHook.Apply();
    }

    private void OnDestroy()
    {
        Application.wantsToQuit -= OnQuit;
        ResetAll();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit()
    {
        Scan();
        ResetAll();
    }

    private bool OnQuit()
    {
        ResetAll();
        return true;
    }

    private void Update()
    {
        // Home 键切换
        if (Input.GetKeyDown(KeyCode.Home))
        {
            _showPanel = !_showPanel;
            if (_showPanel)
            {
                if (_player == null) _player = UnityEngine.Object.FindObjectOfType<PlayerManager>();
                if (_attrs.Count == 0 || _player == null)
                    Scan();
                else
                    RefreshCurrentValues();
            }
        }

        // 缓存 PlayerManager（避免每帧 FindObjectOfType）
        if (_player == null) _player = UnityEngine.Object.FindObjectOfType<PlayerManager>();

// D：定时把“干净值”写进存档，防止游戏在任意时刻把脏运行值同步进存档
        if ((_deltaF.Count > 0 || _deltaI.Count > 0 || _deltaL.Count > 0) &&
            Time.realtimeSinceStartup - _lastCleanWriteTime >= 1f)
        {
            _lastCleanWriteTime = Time.realtimeSinceStartup;
            WriteCleanAllToSave();
        }

        // 轮询 SaveManager 是否激活（游戏读档/存档瞬间可能 Instance 才非空）
        if (Time.realtimeSinceStartup - _lastSaveMomentCheck >= 0.5f)
        {
            _lastSaveMomentCheck = Time.realtimeSinceStartup;
            TryHandleSaveMoment();
        }
    }

    private void RefreshCurrentValues()
    {
        if (_player == null) return;
        foreach (var a in _attrs)
            _currentValues[a.Key] = a.Get(_player);
    }

    private void OnGUI()
    {
        if (!_showPanel) return;
        if (_ls == null)
        {
            _ls = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            _hs = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _ts = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.skin.textField.fontSize = 20;
            GUI.skin.button.fontSize = 18;
        }

        // 固定紧凑面板，不再占满整个屏幕
        const float PanelX = 20, PanelY = 20, PanelW = 800;
        float PanelH = Mathf.Min(Screen.height - 60, 680);
        var pr = new Rect(PanelX, PanelY, PanelW, PanelH);
        GUI.Box(pr, "");

        GUI.Label(new Rect(PanelX + 20, PanelY + 12, 400, 46), "Shadow Dungeon 修改器", _ts);
        if (GUI.Button(new Rect(PanelX + PanelW - 260, PanelY + 18, 110, 40), "全部重置")) ResetAll();
        if (GUI.Button(new Rect(PanelX + PanelW - 140, PanelY + 18, 110, 40), "重新扫描")) Scan();

        if (_player == null)
        {
            GUI.Label(new Rect(PanelX + 20, PanelY + 80, 500, 30), "未检测到玩家");
            return;
        }

        // 列坐标：属性名 / 当前值 / 修改值 / OK / 重置（横平竖直对齐）
        const float ColName = 0, ColCur = 286, ColInput = 412, ColOk = 568, ColReset = 644;
        const float WName = 280, WCur = 120, WInput = 150, WOk = 70, WReset = 70;

        float areaX = PanelX + 30;
        float areaY = PanelY + 105;
        float areaW = PanelW - 60;
        float areaH = PanelH - 165;

        // 表头
        GUI.Label(new Rect(areaX + ColName, areaY - 38, WName, 30), "属性名", _hs);
        GUI.Label(new Rect(areaX + ColCur, areaY - 38, WCur, 30), "当前值", _hs);
        GUI.Label(new Rect(areaX + ColInput, areaY - 38, WInput, 30), "修改值(+/-)", _hs);
        GUI.Label(new Rect(areaX + ColOk, areaY - 38, WOk, 30), "OK", _hs);
        GUI.Label(new Rect(areaX + ColReset, areaY - 38, WReset, 30), "重置", _hs);

        float contentH = _attrs.Count * 50 + 10;
        _scrollPos = GUI.BeginScrollView(new Rect(areaX, areaY, areaW, areaH), _scrollPos, new Rect(0, 0, areaW, contentH));
        float y = 5;
        foreach (var a in _attrs)
            Row(ref y, a);
        GUI.EndScrollView();
    }

    private void Row(ref float y, AttrEntry a)
    {
        const float XName = 0, XCur = 286, XInput = 412, XOk = 568, XReset = 644;
        const float WName = 280, WCur = 120, WInput = 150, WOk = 70, WReset = 70;
        const float RH = 44, G = 6;

        string nm = a.CN;
        // 属性名
        GUI.Label(new Rect(XName, y, WName, RH), nm, _ls);
        // 当前值（从缓存读取，不反射）
        string cur = _currentValues.TryGetValue(a.Key, out var v) ? v : a.Get(_player!);
        GUI.Label(new Rect(XCur, y, WCur, RH), cur, _ls);
        // 输入框（直接填增减量，没有额外说明文字）
        if (!_inputBuffers.ContainsKey(a.Key)) _inputBuffers[a.Key] = "";
        _inputBuffers[a.Key] = GUI.TextField(new Rect(XInput, y + 4, WInput, RH - 8), _inputBuffers[a.Key]);

        // OK：按增量方式应用
        if (GUI.Button(new Rect(XOk, y + 2, WOk, RH - 4), "OK"))
        {
            string nv = _inputBuffers[a.Key];
            if (!string.IsNullOrEmpty(nv) && nv != "0")
                ApplyOK(a, cur, nv, nm);
        }

        // 重置：当前值 - 该属性所有修改器增量
        if (GUI.Button(new Rect(XReset, y + 2, WReset, RH - 4), "重置"))
        {
            ResetAttr(a);
        }
        y += RH + G;
    }

    private void ApplyOK(AttrEntry a, string cur, string nv, string nm)
    {
        // 最终版：改运行时 + 存档始终写干净值
        string target = a.AddToCurrent(cur, nv);
        if (a.Set(_player!, target))
        {
            a.AddDelta(_deltaF, _deltaI, _deltaL, nv);
            var pd = GetPlayerSaveData();
            if (pd != null)
            {
                string clean = a.SubDelta(a.Get(_player!), _deltaF, _deltaI, _deltaL);
                a.WriteSaveField(pd, clean);
            }
            _currentValues[a.Key] = a.Get(_player!);
            _inputBuffers[a.Key] = "";
            TrainerManager.Log.LogInfo(string.Format("[Trainer] {0}: {1} + {2} -> {3}", nm, cur, nv, target));
        }
    }

    private void ResetAttr(AttrEntry a)
    {
        if (_player == null) return;
        string cur = a.Get(_player!);
        string clean = a.SubDelta(cur, _deltaF, _deltaI, _deltaL);
        bool changed = clean != cur;
        if (changed) a.Set(_player!, clean);
        var pd = GetPlayerSaveData();
        if (pd != null) a.WriteSaveField(pd, clean);
        ClearDelta(a.Key);
        _currentValues[a.Key] = a.Get(_player!);
        _inputBuffers[a.Key] = "";
        TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} reset -> {1}", a.CN, clean));
    }

    internal void ResetAll()
    {
        if (_player == null) _player = UnityEngine.Object.FindObjectOfType<PlayerManager>();
        if (_player == null)
        {
            TrainerManager.Log.LogWarning("[Trainer] ResetAll skipped: player null");
            return;
        }
        bool any = _deltaF.Count > 0 || _deltaI.Count > 0 || _deltaL.Count > 0;
        if (!any) return;
        foreach (var a in _attrs)
        {
            if (HasDelta(a.Key))
                ResetAttr(a);
        }
        TrainerManager.Log.LogInfo("[Trainer] ResetAll done");
    }

    private bool HasDelta(string key)
    {
        return _deltaF.ContainsKey(key) || _deltaI.ContainsKey(key) || _deltaL.ContainsKey(key);
    }

    private void ClearDelta(string key)
    {
        _deltaF.Remove(key);
        _deltaI.Remove(key);
        _deltaL.Remove(key);
    }

    private void Scan()
    {
        _player = FindObjectOfType<PlayerManager>();
        if (_player == null) { TrainerManager.Log.LogWarning("[Trainer] PlayerManager not found"); return; }
        _attrs.Clear(); _inputBuffers.Clear();

        Add("Health",           "生命值",       AttrType.Float);
        Add("Mana",             "法力值",       AttrType.Float);
        Add("Level",            "等级",         AttrType.Int);
        Add("Damage_Bei",       "攻击倍率",     AttrType.Float);
        Add("Damage_Anti",      "伤害减免",     AttrType.Float);
        Add("MVSpeed_Bei",      "移动速度倍率",  AttrType.Float);
        Add("ATSpeed_Bei",      "攻击速度倍率",  AttrType.Float);
        Add("Health_Bei",       "生命倍率",     AttrType.Float);
        Add("Health_Percent",   "生命百分比",   AttrType.Float);
        Add("Mana_Bei",         "法力倍率",     AttrType.Float);
        Add("Mana_Percent",     "法力百分比",   AttrType.Float);
        Add("BJrate",           "暴击率",       AttrType.Float);
        Add("BJDamage",         "暴击伤害",     AttrType.Float);
        Add("JYrate",           "穿透率",       AttrType.Float);
        Add("GeDang",           "格挡",         AttrType.Float);
        Add("FireDamage_Bei",   "火伤倍率",     AttrType.Float);
        Add("FrozenDamage_Bei", "冰伤倍率",     AttrType.Float);
        Add("ThunderDamage_Bei","雷伤倍率",     AttrType.Float);
        Add("PoisonDamage_Bei", "毒伤倍率",     AttrType.Float);
        Add("PhysicsDamage_Bei","物理伤倍率",   AttrType.Float);
        Add("ShadowDamage_Bei", "暗影伤倍率",   AttrType.Float);
        Add("FireChuan",        "火穿透",       AttrType.Float);
        Add("FrozenChuan",      "冰穿透",       AttrType.Float);
        Add("ThunderChuan",     "雷穿透",       AttrType.Float);
        Add("PoisonChuan",      "毒穿透",       AttrType.Float);
        Add("PhysicsChuan",     "物理穿透",     AttrType.Float);
        Add("ShadowChuan",      "暗影穿透",     AttrType.Float);
        Add("FireAnti",         "火抗",         AttrType.Float);
        Add("FrozenAnti",       "冰抗",         AttrType.Float);
        Add("ThunderAnti",      "雷抗",         AttrType.Float);
        Add("PoisonAnti",       "毒抗",         AttrType.Float);
        Add("PhysicsAnti",      "物抗",         AttrType.Float);
        Add("ShadowAnti",       "暗影抗",       AttrType.Float);
        Add("CoolDown",         "冷却缩减",     AttrType.Float);
        Add("ItemDrop_Rate",    "掉落率",       AttrType.Float);
        Add("EXP_Range",        "经验范围",     AttrType.Float);
        TryAddExpRate();
        TryMoney();
        TryTalent();

        Add("Damage_Base",      "基础攻击力",    AttrType.Float);
        Add("MVSpeed_Base",     "移动速度基础",  AttrType.Float);
        Add("ATSpeed_Base",     "攻击速度基础",  AttrType.Float);

        // 立即填充当前值缓存
        _currentValues.Clear();
        foreach (var a in _attrs)
            _currentValues[a.Key] = a.Get(_player);

TrainerManager.Log.LogInfo(string.Format("[Trainer] {0} attrs", _attrs.Count));
    }

    private void Add(string f, string cn, AttrType t) => _attrs.Add(AttrEntry.Make(f, cn, t));

    private void TryAddExpRate()
    {
        // 游戏有“经验获取率提高”的药水，对应字段可能是 Xp_Bei_Tmp / XP_Rate / JY_Rate。
        // 这里把存在的候选字段都加进去，优先 Xp_Bei_Tmp（临时经验倍率，最像药水效果）。
        string[] candidates = { "Xp_Bei_Tmp", "XP_Rate", "JY_Rate" };
        string[] names = { "经验获取倍率(临时)", "经验获取率", "经验获取率(JY)" };
        for (int i = 0; i < candidates.Length; i++)
        {
            var fi = typeof(PlayerManager).GetField(candidates[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null)
                Add(candidates[i], names[i], AttrType.Float);
        }
    }

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
            if (hf != null) _attrs.Add(AttrEntry.Ref("Talent_P_Have", "可用天赋点", AttrType.Int, inst, hf));
            if (bf != null) _attrs.Add(AttrEntry.Ref("Talent_P_Base", "天赋点(基础)", AttrType.Int, inst, bf));
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
            _attrs.Add(new AttrEntry("Money", "金币", AttrType.Long,
                _ => { try { return mref.GetValue(iref)?.ToString() ?? "0"; } catch { return "0"; } },
                (_, v) => { try { if (long.TryParse(v, out var m)) { mref.SetValue(iref, m); return true; } } catch { } return false; }));
        }
        catch { }
    }

    private void WriteCleanAllToSave()
    {
        if (_player == null) return;
        var pd = GetPlayerSaveData();
        if (pd == null)
        {
            TrainerManager.Log.LogWarning("[Trainer] WriteCleanAllToSave: PlayerSaveData NULL");
            return;
        }
        foreach (var a in _attrs)
        {
            if (HasDelta(a.Key))
            {
                string clean = a.SubDelta(a.Get(_player!), _deltaF, _deltaI, _deltaL);
                bool ok = a.WriteSaveField(pd, clean);
                if (!ok)
                    TrainerManager.Log.LogWarning($"[Trainer] periodic save write failed: {a.Key} -> {clean}");
            }
        }
    }

    private object? GetSaveManagerInstance()
    {
        try
        {
            var smt = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (smt == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            string[] names = { "Instance", "I", "Current", "_instance", "instance", "Manager" };
            foreach (var name in names)
            {
                var pi = smt.GetProperty(name, flags);
                if (pi != null)
                {
                    var v = pi.GetValue(null);
                    if (v != null) return v;
                }
                var fi = smt.GetField(name, flags);
                if (fi != null)
                {
                    var v = fi.GetValue(null);
                    if (v != null) return v;
                }
            }
            return null;
        }
        catch { return null; }
    }

    private bool HasAnyDelta()
    {
        return _deltaF.Count > 0 || _deltaI.Count > 0 || _deltaL.Count > 0;
    }

    private void TryHandleSaveMoment()
    {
        if (_player == null) return;
        if (!HasAnyDelta()) return;
        var sm = GetSaveManagerInstance();
        if (sm == null) return;
        var pd = GetPlayerSaveData();
        TrainerManager.Log.LogInfo($"[Trainer] SaveMoment: SaveManager active, PlayerSaveData={(pd == null ? "NULL" : "OK")}");
        if (pd != null)
            ResetAll();
    }

    private object? GetPlayerSaveData()
    {
        try
        {
            // 首选：PlayerManager 上直接挂着 SaveData 字段（日志已确认存在）
            if (_player != null)
            {
                var fi = typeof(PlayerManager).GetField("SaveData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null)
                {
                    var v = fi.GetValue(_player);
                    if (v != null)
                    {
                        LogSaveDataDiag($"PlayerManager.SaveData OK type={v.GetType().FullName}");
                        return v;
                    }
                    LogSaveDataDiag("PlayerManager.SaveData NULL");
                }
                else
                {
                    LogSaveDataDiag("PlayerManager.SaveData field not found");
                }
            }

            // 兜底：SaveManager.Instance -> RuntimeData -> PlayerData
            var sm = GetSaveManagerInstance();
            if (sm == null)
            {
                LogSaveDataDiag("SaveManager.Instance/Field NULL");
                return null;
            }
            var smType = sm.GetType();
            const BindingFlags instFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            var rdProp = smType.GetProperty("RuntimeData", instFlags);
            var rdField = smType.GetField("RuntimeData", instFlags);
            var rd = rdProp?.GetValue(sm) ?? rdField?.GetValue(sm);
            if (rd == null)
            {
                LogSaveDataDiag($"SaveManager.RuntimeData NULL (type={smType.FullName}, prop={rdProp != null}, field={rdField != null})");
                return null;
            }
            var rdType = rd.GetType();
            var pdProp = rdType.GetProperty("PlayerData", instFlags);
            var pdField = rdType.GetField("PlayerData", instFlags);
            object? pd = null;
            if (pdProp != null) pd = pdProp.GetValue(rd);
            else if (pdField != null) pd = pdField.GetValue(rd);
            if (pd == null)
            {
                LogSaveDataDiag($"RuntimeData.PlayerData NULL (runtimeType={rdType.FullName}, prop={pdProp != null}, field={pdField != null})");
                return null;
            }
            LogSaveDataDiag($"PlayerData OK type={pd.GetType().FullName}");
            return pd;
        }
        catch (Exception e)
        {
            LogSaveDataDiag("GetPlayerSaveData exception: " + e.Message);
            return null;
        }
    }

    private void LogSaveDataDiag(string msg)
    {
        if (_saveDataDiagLogged) return;
        _saveDataDiagLogged = true;
        TrainerManager.Log.LogWarning("[Trainer] SaveDataDiag: " + msg);
    }

    private void RefreshFromSave()
    {
        try
        {
            if (_player == null) return;
            string[] names = { "RefreshRuntimeDerivedStats", "InitFromSaveData", "InitializeAfterSaveRestore" };
            foreach (var name in names)
            {
                var mi = typeof(PlayerManager).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi != null)
                {
                    mi.Invoke(_player, null);
                    return;
                }
            }
            TrainerManager.Log.LogWarning("[Trainer] No refresh method found on PlayerManager");
        }
        catch (Exception e)
        {
            TrainerManager.Log.LogWarning("[Trainer] RefreshFromSave failed: " + e.Message);
        }
    }
}

public class AttrEntry
{
    public string Key, CN;
    public AttrType Type;
    private readonly Func<PlayerManager, string> _get;
    private readonly Func<PlayerManager, string, bool> _set;

    public AttrEntry(string key, string cn, AttrType type,
        Func<PlayerManager, string> get, Func<PlayerManager, string, bool> set)
    { Key = key; CN = cn; Type = type; _get = get; _set = set; }

    public static AttrEntry Make(string f, string cn, AttrType t)
    {
        var fi = typeof(PlayerManager).GetField(f, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return new AttrEntry(f, cn, t,
            (p) => ReadField(p, fi, t),
            (p, v) => WriteField(p, fi, t, v));
    }

    public static AttrEntry Ref(string f, string cn, AttrType t, object obj, FieldInfo fi)
    {
        return new AttrEntry(f, cn, t,
            (p) => ReadRef(obj, fi, t),
            (p, v) => WriteRef(obj, fi, t, v));
    }

    public string Get(PlayerManager p) => _get(p);
    public bool Set(PlayerManager p, string v) => _set(p, v);

    public string AddToCurrent(string cur, string delta)
    {
        try
        {
            switch (Type)
            {
                case AttrType.Float:
                    if (float.TryParse(cur, out var cf) && float.TryParse(delta, out var df)) return (cf + df).ToString("F2");
                    break;
                case AttrType.Int:
                    if (int.TryParse(cur, out var ci) && int.TryParse(delta, out var di)) return (ci + di).ToString();
                    break;
                case AttrType.Long:
                    if (long.TryParse(cur, out var cl) && long.TryParse(delta, out var dl)) return (cl + dl).ToString();
                    break;
            }
        }
        catch { }
        return cur;
    }

    public void AddDelta(Dictionary<string, float> dF, Dictionary<string, int> dI, Dictionary<string, long> dL, string input)
    {
        switch (Type)
        {
            case AttrType.Float:
                if (float.TryParse(input, out var nf)) { dF.TryGetValue(Key, out var c); dF[Key] = c + nf; }
                break;
            case AttrType.Int:
                if (int.TryParse(input, out var ni)) { dI.TryGetValue(Key, out var c); dI[Key] = c + ni; }
                break;
            case AttrType.Long:
                if (long.TryParse(input, out var nl)) { dL.TryGetValue(Key, out var c); dL[Key] = c + nl; }
                break;
        }
    }

    public string SubDelta(string cur, Dictionary<string, float> dF, Dictionary<string, int> dI, Dictionary<string, long> dL)
    {
        switch (Type)
        {
            case AttrType.Float:
                if (dF.TryGetValue(Key, out var df) && float.TryParse(cur, out var cf)) return (cf - df).ToString("F2");
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

    public bool WriteSaveField(object pd, string raw)
    {
        try
        {
            var f = pd.GetType().GetField(Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            object? b = Type switch
            {
                AttrType.Int => int.TryParse(raw, out var iv) ? iv : null,
                AttrType.Long => long.TryParse(raw, out var lv) ? lv : null,
                AttrType.Float => float.TryParse(raw, out var fv) ? fv : null,
                _ => null
            };
            if (b == null) return false;
            f.SetValue(pd, b);
            return true;
        }
        catch { return false; }
    }

    private static string ReadField(PlayerManager p, FieldInfo? fi, AttrType t)
    {
        try
        {
            if (p == null || fi == null) return "?";
            var v = fi.GetValue(p);
            return t switch { AttrType.Int => ((int)v).ToString(), AttrType.Long => ((long)v).ToString(), AttrType.Float => ((float)v).ToString("F2"), _ => v?.ToString() ?? "?" };
        }
        catch { return "?"; }
    }

    private static bool WriteField(PlayerManager p, FieldInfo? fi, AttrType t, string v)
    {
        try
        {
            if (p == null || fi == null) return false;
            object? b = t switch { AttrType.Int => int.TryParse(v, out var iv) ? iv : null, AttrType.Long => long.TryParse(v, out var lv) ? lv : null, AttrType.Float => float.TryParse(v, out var fv) ? fv : null, AttrType.Bool => bool.TryParse(v, out var bv) ? bv : null, _ => null };
            if (b == null) return false;
            fi.SetValue(p, b);
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

internal static class HarmonySaveHook
{
    private static bool _applied;

    internal static void Apply()
    {
        if (_applied) return;
        _applied = true;
        try
        {
            var harmony = new Harmony("com.shadowdungeon.trainer.savehook");
            var smt = typeof(PlayerManager).Assembly.GetType("SaveManager");
            if (smt == null)
            {
                TrainerManager.Log.LogWarning("[HarmonySaveHook] SaveManager type not found");
                return;
            }

            string[] methodNames =
            {
                "SaveAndExitBlocking",
                "SaveAndExitAndWaitIfNeeded",
                "QueueExitSaveAfterCurrentSave",
                "SaveAndExitAfterCurrentSaveAsync"
            };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            foreach (var name in methodNames)
            {
                int patched = 0;
                foreach (var m in smt.GetMethods(flags))
                {
                    if (m.Name != name) continue;
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(HarmonySaveHook), nameof(Prefix)));
                    patched++;
                    TrainerManager.Log.LogInfo($"[HarmonySaveHook] Patched {name} ({m.GetParameters().Length} params)");
                }
                if (patched == 0)
                    TrainerManager.Log.LogWarning($"[HarmonySaveHook] Method not found: {name}");
            }
        }
        catch (Exception e)
        {
            TrainerManager.Log.LogWarning("[HarmonySaveHook] Apply failed: " + e);
        }
    }

    private static void Prefix()
    {
        TrainerBehaviour.Instance?.ResetAll();
    }
}

public enum AttrType { Int, Long, Float, Bool }