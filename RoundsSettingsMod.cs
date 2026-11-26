using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

// ROUNDS — Round Settings (BepInEx 5 / HarmonyX)
// Меню (F8) для настройки: сколько раундов нужно выиграть, сколько очков в раунде
// и лимит времени на одно очко (по истечении умирает игрок с меньшим HP).
namespace RoundsSettingsMod
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class RoundsSettingsPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.dmah.roundssettings";
        public const string NAME = "Round Settings";
        public const string VERSION = "1.0.0";

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> RoundsToWin;   // раундов до победы в матче
        public static ConfigEntry<int> PointsToWin;   // очков до победы в раунде
        public static ConfigEntry<int> CardsPerRound; // карт за выбор
        public static ConfigEntry<bool> CustomCards; // новые карты (нужен перезапуск, у всех игроков одинаково)
        public static ConfigEntry<bool> ShowHp; // показывать здоровье числами над игроками
        public static ConfigEntry<int> HmelJump; // бонус прыжка суперкарты Хмеля, %
        public static ConfigEntry<int> HealthPercent; // максимальное здоровье игроков, % от обычного
        public static ConfigEntry<int> OfferedCards; // сколько карт показывается на выбор
        public static ConfigEntry<int> RerollsPerPick; // сколько раз можно обновить предложенные карты
        public static ConfigEntry<KeyCode> RerollKey;
        public static ConfigEntry<int> TimeLimit;     // сек на очко, 0 = без лимита
        public static ConfigEntry<KeyCode> MenuKey;
        public static RoundsSettingsPlugin Instance;

        // меню
        bool menuOpen;
        Rect win = new Rect(40, 60, 380, 610);
        GUIStyle titleStyle, hintStyle;

        // таймер
        float roundTimer;
        bool wasActive;
        bool timeUpFired;

        public static void Warn(string m) { Instance.Logger.LogWarning(m); }

        void Awake()
        {
            Instance = this;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            Enabled = Config.Bind("General", "Enabled", true, "Включить мод");
            RoundsToWin = Config.Bind("General", "RoundsToWin", 2, "Раундов до победы в матче (в игре по умолчанию 2)");
            PointsToWin = Config.Bind("General", "PointsToWin", 2, "Очков до победы в раунде (в игре по умолчанию 2)");
            CardsPerRound = Config.Bind("General", "CardsPerRound", 1, "[ЭКСПЕРИМЕНТАЛЬНО] Сколько карт (способностей) выбирает игрок за один выбор (в игре по умолчанию 1)");
            CustomCards = Config.Bind("Cards", "CustomCards", true, "Добавить в игру новые карты мода. Требует перезапуска игры и должно совпадать у всех игроков в онлайне");
            ShowHp = Config.Bind("General", "ShowHealthNumbers", true, "Показывать здоровье числами над игроками");
            HmelJump = Config.Bind("Cards", "HmelJumpBonusPercent", 100, "Бонус к высоте прыжка у суперкарты Хмеля, % (100 = прыжок в 2 раза выше)");
            HealthPercent = Config.Bind("General", "HealthPercent", 100, "Максимальное здоровье игроков в процентах от обычного (100 = как в игре)");
            OfferedCards = Config.Bind("General", "OfferedCards", 5, "Сколько карт показывается на выбор (2-9)");
            RerollsPerPick = Config.Bind("General", "RerollsPerPick", 0, "[ЭКСПЕРИМЕНТАЛЬНО] Сколько раз за выбор можно обновить предложенные карты (0 = выключено)");
            RerollKey = Config.Bind("General", "RerollKey", KeyCode.R, "Клавиша обновления карт");
            TimeLimit = Config.Bind("General", "TimeLimitSeconds", 0, "Лимит времени на очко, сек. 0 = выключен");
            MenuKey = Config.Bind("General", "MenuKey", KeyCode.F8, "Клавиша меню");

            // версия настроек: при обновлении сбрасываем экспериментальные параметры на безопасные значения
            var ver = Config.Bind("Meta", "SettingsVersion", 0, "Служебный параметр, не менять");
            if (ver.Value < 2)
            {
                CardsPerRound.Value = 1; RerollsPerPick.Value = 0; OfferedCards.Value = 5;
                ver.Value = 2; Config.Save();
            }

            var hh = new Harmony(GUID); hh.PatchAll(typeof(Patches)); hh.PatchAll(typeof(CardPatches));
            Logger.LogInfo(NAME + " " + VERSION + " loaded. Menu key: " + MenuKey.Value);
        }

        void Update()
        {
            if (Input.GetKeyDown(MenuKey.Value)) menuOpen = !menuOpen;
            TickRoundTimer();
            TickReroll();
        }

        // ---------- синхронизация (хост = источник правды) ----------
        const string K_ON = "rs_on", K_R = "rs_r", K_P = "rs_p", K_T = "rs_t", K_C = "rs_c", K_X = "rs_x", K_O = "rs_o", K_H = "rs_h";
        public static bool Online { get { return PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode; } }
        public static bool IsClient { get { return Online && !PhotonNetwork.IsMasterClient; } }

        static int Prop(string k, int def)
        {
            var pr = PhotonNetwork.CurrentRoom.CustomProperties;
            return pr.ContainsKey(k) ? Convert.ToInt32(pr[k]) : def;
        }
        public static bool EffEnabled { get { return IsClient ? Prop(K_ON, 0) == 1 : Enabled.Value; } }
        public static int EffRounds { get { return IsClient ? Prop(K_R, 2) : RoundsToWin.Value; } }
        public static int EffPoints { get { return IsClient ? Prop(K_P, 2) : PointsToWin.Value; } }
        public static int EffCards { get { return IsClient ? Prop(K_C, 1) : CardsPerRound.Value; } }
        public static int EffHealth { get { return IsClient ? Prop(K_H, 100) : HealthPercent.Value; } }
        public static int EffOffered { get { return IsClient ? Prop(K_O, 5) : OfferedCards.Value; } }
        public static int EffRerolls { get { return IsClient ? Prop(K_X, 0) : RerollsPerPick.Value; } }
        public static int EffTime { get { return IsClient ? Prop(K_T, 0) : TimeLimit.Value; } }

        static void Publish()
        {
            if (!Online || !PhotonNetwork.IsMasterClient) return;
            var pr = PhotonNetwork.CurrentRoom.CustomProperties;
            int on = Enabled.Value ? 1 : 0;
            if (Prop(K_ON, -1) == on && Prop(K_R, -1) == RoundsToWin.Value && Prop(K_P, -1) == PointsToWin.Value && Prop(K_T, -1) == TimeLimit.Value && Prop(K_C, -1) == CardsPerRound.Value && Prop(K_X, -1) == RerollsPerPick.Value && Prop(K_O, -1) == OfferedCards.Value && Prop(K_H, -1) == HealthPercent.Value) return;
            var h = new ExitGames.Client.Photon.Hashtable();
            h[K_ON] = on; h[K_R] = RoundsToWin.Value; h[K_P] = PointsToWin.Value; h[K_T] = TimeLimit.Value; h[K_C] = CardsPerRound.Value; h[K_X] = RerollsPerPick.Value; h[K_O] = OfferedCards.Value; h[K_H] = HealthPercent.Value;
            PhotonNetwork.CurrentRoom.SetCustomProperties(h);
        }

        // ---------- применение настроек ----------
        public static void Apply()
        {
            var gm = GM_ArmsRace.instance;
            if (gm == null) return;
            Publish();
            if (!EffEnabled) return;
            gm.roundsToWinGame = Mathf.Max(1, EffRounds);
            Traverse.Create(gm).Field("pointsToWinRound").SetValue(Mathf.Max(1, EffPoints));
        }

        static void ApplyAndRefreshUI()
        {
            Apply();
            try
            {
                var gm = GM_ArmsRace.instance;
                if (gm != null && UIHandler.instance != null && EffEnabled)
                    Traverse.Create(UIHandler.instance).Method("SetNumberOfRounds", new object[] { gm.roundsToWinGame }).GetValue();
            }
            catch (Exception e) { Instance.Logger.LogWarning("UI refresh: " + e.Message); }
        }

        // ---------- число предлагаемых карт ----------
        static Transform[] origSlots;

        public static void ResizeSlots(CardChoice cc)
        {
            var tr = Traverse.Create(cc);
            var cur = tr.Field("children").GetValue<Transform[]>();
            if (cur == null || cur.Length == 0) return;
            if (origSlots == null)
            {
                origSlots = (Transform[])cur.Clone();
                var sb = new System.Text.StringBuilder("Card slots: " + origSlots.Length);
                foreach (var t in origSlots) sb.Append(" " + t.position);
                Instance.Logger.LogInfo(sb.ToString());
            }
            if (!EffEnabled) { tr.Field("children").SetValue(origSlots); return; }
            int n = Mathf.Clamp(EffOffered, 2, 9);
            int L = origSlots.Length;
            if (n == L) { tr.Field("children").SetValue(origSlots); return; }
            var res = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                float f = (n == 1 ? 0f : (float)i / (n - 1)) * (L - 1);
                int a = Mathf.Min((int)f, L - 2); float u = f - a;
                Vector3 pos = Vector3.Lerp(origSlots[a].position, origSlots[a + 1].position, u);
                Quaternion rot = Quaternion.Slerp(origSlots[a].rotation, origSlots[a + 1].rotation, u);
                Transform t;
                if (i < L) t = origSlots[i];
                else
                {
                    t = new GameObject("RS_Slot" + i).transform;
                    t.SetParent(origSlots[0].parent, false);
                    UnityEngine.Object.DontDestroyOnLoad(t.gameObject);
                }
                t.position = pos; t.rotation = rot;
                res[i] = t;
            }
            tr.Field("children").SetValue(res);
        }

        // удалить карту так, чтобы она пропала и у второго игрока (карты выбора — сетевые объекты)
        public static void KillCard(GameObject go)
        {
            if (go == null) return;
            var pv = go.GetComponent<PhotonView>();
            if (pv != null && PhotonNetwork.InRoom && pv.IsMine) PhotonNetwork.Destroy(go);
            else UnityEngine.Object.Destroy(go);
        }

        // ---------- реролл карт ----------
        public static int RerollsLeft;
        float lastReroll;

        static bool Picking()
        {
            var cc = CardChoice.instance;
            return cc != null && EffEnabled && Traverse.Create(cc).Field("IsPicking").GetValue<bool>();
        }

        static bool LocalIsPicker()
        {
            if (PhotonNetwork.OfflineMode || !PhotonNetwork.InRoom) return true;
            var cc = CardChoice.instance;
            int id = Traverse.Create(cc).Field("pickrID").GetValue<int>();
            var pl = Traverse.Create(PlayerManager.instance).Method("GetPlayerWithID", new object[] { id }).GetValue<Player>();
            return pl != null && pl.data.view.IsMine;
        }

        void TickReroll()
        {
            if (!Picking() || RerollsLeft <= 0) return;
            if (!Input.GetKeyDown(RerollKey.Value) || Time.unscaledTime - lastReroll < 1f) return;
            if (Traverse.Create(CardChoice.instance).Field("isPlaying").GetValue<bool>()) return; // карты ещё появляются/исчезают
            if (!LocalIsPicker()) return;
            try
            {
                var cc = CardChoice.instance;
                var tr = Traverse.Create(cc);
                // убираем текущие карты (с синхронизацией по сети), затем обычным способом раздаём новый набор
                var spawned = tr.Field("spawnedCards").GetValue<System.Collections.IList>();
                var old = new System.Collections.Generic.List<GameObject>();
                foreach (var o in spawned) { var g = o as GameObject; if (g != null) old.Add(g); }
                foreach (var g in old) KillCard(g);
                spawned.Clear();
                tr.Field("picks").SetValue(tr.Field("picks").GetValue<int>() + 1); // ReplaceCards уменьшает на 1
                var it = (IEnumerator)typeof(CardChoice).GetMethod("ReplaceCards", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Invoke(cc, new object[] { null, false });
                cc.StartCoroutine(it);
                RerollsLeft--;
                lastReroll = Time.unscaledTime;
            }
            catch (Exception e) { Logger.LogWarning("Reroll failed: " + e); }
        }

        // ---------- таймер раунда ----------
        public static float TimeLeft
        {
            get { return Mathf.Max(0f, EffTime - Instance.roundTimer); }
        }

        static bool RoundActive()
        {
            var gm = GM_ArmsRace.instance;
            var gmgr = GameManager.instance;
            var pm = PlayerManager.instance;
            if (gm == null || gmgr == null || pm == null) return false;
            var tr = Traverse.Create(gm);
            if (!gmgr.battleOngoing || tr.Field("isTransitioning").GetValue<bool>() || gm.isPicking || gm.pickPhase || tr.Field("isWaiting").GetValue<bool>()) return false;
            if (pm.players == null || pm.players.Count < 2) return false;
            int alive = 0;
            foreach (var p in pm.players) if (p != null && !p.data.dead) alive++;
            return alive >= 2;
        }

        void TickRoundTimer()
        {
            if (Online && PhotonNetwork.IsMasterClient) Publish();
            bool active = EffEnabled && EffTime > 0 && RoundActive();
            if (!active) { wasActive = false; roundTimer = 0f; timeUpFired = false; return; }
            if (!wasActive) { wasActive = true; roundTimer = 0f; timeUpFired = false; }
            roundTimer += Time.deltaTime;
            if (!timeUpFired && roundTimer >= EffTime)
            {
                timeUpFired = true;
                // решение принимает хост (или офлайн)
                if (PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient) KillWeakest();
            }
        }

        static void KillWeakest()
        {
            Player loser = null; float hp = float.MaxValue; int ties = 0;
            foreach (var p in PlayerManager.instance.players)
            {
                if (p == null || p.data.dead) continue;
                float h = p.data.health;
                if (h < hp - 0.01f) { hp = h; loser = p; ties = 1; }
                else if (Mathf.Abs(h - hp) <= 0.01f) { ties++; if (UnityEngine.Random.value < 1f / ties) loser = p; }
            }
            if (loser == null) return;
            var hh = loser.data.healthHandler;
            var pv = hh.GetComponent<PhotonView>();
            if (pv != null && !PhotonNetwork.OfflineMode) pv.RPC("RPCA_Die", RpcTarget.All, new object[] { Vector2.zero });
            else Traverse.Create(hh).Method("RPCA_Die", new object[] { Vector2.zero }).GetValue();
        }

        // ---------- GUI ----------
        void OnGUI()
        {
            float k = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float sw = Screen.width / k;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            }

            // здоровье числами над игроками
            if (ShowHp.Value)
            {
                try
                {
                    var cam = Camera.main; var pm = PlayerManager.instance;
                    if (cam != null && pm != null && pm.players != null)
                    {
                        var hs = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                        foreach (var p in pm.players)
                        {
                            if (p == null || p.data == null || p.data.dead) continue;
                            if (Online && !p.data.view.IsMine) continue; // в онлайне — только своё здоровье
                            Vector3 sp = cam.WorldToScreenPoint(p.transform.position + Vector3.up * 2.2f);
                            if (sp.z < 0) continue;
                            float cur = Mathf.Max(0f, p.data.health), max = p.data.MaxHealth;
                            hs.normal.textColor = cur / Mathf.Max(1f, max) < 0.3f ? Color.red : Color.white;
                            var r = new Rect(sp.x / k - 60, (Screen.height - sp.y) / k - 12, 120, 24);
                            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), Mathf.CeilToInt(cur) + " / " + Mathf.CeilToInt(max), new GUIStyle(hs) { normal = { textColor = Color.black } });
                            GUI.Label(r, Mathf.CeilToInt(cur) + " / " + Mathf.CeilToInt(max), hs);
                        }
                    }
                }
                catch (Exception) { }
            }

            // таймер на экране
            if (EffEnabled && EffTime > 0 && wasActive)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                s.normal.textColor = TimeLeft <= 10f ? Color.red : Color.white;
                GUI.Label(new Rect(sw / 2f - 100, 8, 200, 44), Mathf.CeilToInt(TimeLeft).ToString(), s);
            }

            if (Picking() && EffRerolls > 0)
            {
                var rs = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                rs.normal.textColor = RerollsLeft > 0 ? Color.white : Color.gray;
                GUI.Label(new Rect(sw / 2f - 250, 56, 500, 34), "[" + RerollKey.Value + "] обновить карты: осталось " + RerollsLeft, rs);
            }

            if (!menuOpen)
            {
                if (GUI.Button(new Rect(8, 8, 150, 24), "Раунды [" + MenuKey.Value + "]")) menuOpen = true;
                return;
            }
            win = GUI.Window(8841, win, DrawWindow, "Настройки раундов");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            if (IsClient)
            {
                GUILayout.Label("Вы не хост: настройки задаёт хост комнаты.", titleStyle);
                GUILayout.Label("Раундов до победы: " + EffRounds + (EffEnabled ? "" : " (мод у хоста выкл.)"));
                GUILayout.Label("Очков до победы в раунде: " + EffPoints);
                GUILayout.Label("Карт за выбор: " + EffCards);
                GUILayout.Label("Обновлений карт за выбор: " + EffRerolls);
                GUILayout.Label("Карт на выбор: " + EffOffered);
                GUILayout.Label("Здоровье игроков: " + EffHealth + "%");
                GUILayout.Label("Лимит времени на очко: " + (EffTime == 0 ? "выкл." : EffTime + " сек"));
                if (GUILayout.Button("Закрыть")) menuOpen = false;
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 22));
                return;
            }
            Enabled.Value = GUILayout.Toggle(Enabled.Value, " Мод включён");
            ShowHp.Value = GUILayout.Toggle(ShowHp.Value, " Показывать здоровье числами");
            CustomCards.Value = GUILayout.Toggle(CustomCards.Value, " Новые карты (нужен перезапуск игры)");
            GUILayout.Space(6);

            RoundsToWin.Value = IntSlider("Раундов до победы в матче", RoundsToWin.Value, 1, 1000);
            PointsToWin.Value = IntSlider("Очков до победы в раунде", PointsToWin.Value, 1, 1000);

            CardsPerRound.Value = IntSlider("[эксперим.] Карт за выбор", CardsPerRound.Value, 1, 100);

            HealthPercent.Value = IntSlider("Здоровье игроков, %", HealthPercent.Value, 10, 1000);
            OfferedCards.Value = IntSlider("Карт показывается на выбор", OfferedCards.Value, 2, 9);
            RerollsPerPick.Value = IntSlider("[эксперим.] Обновлений карт [" + RerollKey.Value + "]", RerollsPerPick.Value, 0, 20);

            int t = TimeLimit.Value;
            GUILayout.Label("Лимит времени на очко: " + (t == 0 ? "выкл." : t + " сек"));
            t = Mathf.RoundToInt(GUILayout.HorizontalSlider(t, 0, 300) / 5f) * 5;
            TimeLimit.Value = t;
            GUILayout.Label("По истечении времени умирает игрок с меньшим HP (при равенстве — случайный).", hintStyle);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Стандарт (2/2)")) { RoundsToWin.Value = 2; PointsToWin.Value = 2; CardsPerRound.Value = 1; TimeLimit.Value = 0; }
            if (GUILayout.Button("Быстро (1/1, 30с)")) { RoundsToWin.Value = 1; PointsToWin.Value = 1; CardsPerRound.Value = 1; TimeLimit.Value = 30; }
            if (GUILayout.Button("Марафон (5/3)")) { RoundsToWin.Value = 5; PointsToWin.Value = 3; CardsPerRound.Value = 1; TimeLimit.Value = 0; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Применить сейчас")) { ApplyAndRefreshUI(); Config.Save(); }
            if (GUILayout.Button("Закрыть")) { menuOpen = false; Config.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Значения подхватываются на старте матча и после каждого очка. В онлайне настройки хоста автоматически передаются всем (мод нужен у всех).", hintStyle);
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        static int IntSlider(string label, int v, int min, int max)
        {
            GUILayout.Label(label + ": " + v);
            GUILayout.BeginHorizontal();
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(v, min, max));
            string txt = GUILayout.TextField(nv.ToString(), 4, GUILayout.Width(50));
            int parsed;
            if (txt != nv.ToString() && int.TryParse(txt, out parsed)) nv = parsed;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(nv, min, max);
        }
    }

    static class Patches
    {
        // здоровье: хранимое значение делим на множитель, возвращаемое — умножаем,
        // чтобы карты (MaxHealth *= x) не накапливали множитель
        static float HpMult { get { return RoundsSettingsPlugin.EffEnabled ? Mathf.Max(10, RoundsSettingsPlugin.EffHealth) / 100f : 1f; } }

        [HarmonyPatch(typeof(CharacterData), "get_MaxHealth")]
        [HarmonyPostfix] static void GetMaxHp(ref float __result) { __result *= HpMult; }

        [HarmonyPatch(typeof(CharacterData), "set_MaxHealth")]
        [HarmonyPrefix] static void SetMaxHp(ref float value) { value /= HpMult; }

        [HarmonyPatch(typeof(CardChoice), "StartPick")]
        [HarmonyPrefix] static void StartPickPre(CardChoice __instance)
        {
            try { RoundsSettingsPlugin.ResizeSlots(__instance); }
            catch (Exception e) { RoundsSettingsPlugin.Warn("ResizeSlots: " + e); }
        }

        // сколько карт выбирает игрок за один выбор: подряд запускаем обычный выбор N раз
        static bool inMulti;

        [HarmonyPatch(typeof(CardChoice), "DoPick")]
        [HarmonyPrefix] static bool DoPickPre(CardChoice __instance, object[] __args, ref System.Collections.IEnumerator __result)
        {
            RoundsSettingsPlugin.RerollsLeft = RoundsSettingsPlugin.EffEnabled ? RoundsSettingsPlugin.EffRerolls : 0;
            int n = RoundsSettingsPlugin.EffEnabled ? Mathf.Clamp(RoundsSettingsPlugin.EffCards, 1, 100) : 1;
            if (inMulti || n <= 1) return true;
            __result = MultiPick(__instance, __args, n);
            return false;
        }

        // убрать с экрана все карты предыдущих наборов (у карт выбора есть компонент PublicInt)
        public static void ClearPickCards()
        {
            try
            {
                foreach (var ci in UnityEngine.Object.FindObjectsOfType<CardInfo>())
                {
                    if (ci == null || ci.GetComponent("PublicInt") == null) continue;
                    RoundsSettingsPlugin.KillCard(ci.gameObject);
                }
                var list = Traverse.Create(CardChoice.instance).Field("spawnedCards").GetValue<System.Collections.IList>();
                if (list != null) list.Clear();
            }
            catch (Exception e) { RoundsSettingsPlugin.Warn("ClearPickCards: " + e.Message); }
        }

        static System.Collections.IEnumerator MultiPick(CardChoice cc, object[] args, int n)
        {
            var m = typeof(CardChoice).GetMethod("DoPick");
            for (int k = 0; k < n; k++)
            {
                if (k > 0)
                {
                    yield return new WaitForSecondsRealtime(0.6f);
                    ClearPickCards();
                    try { typeof(CardChoiceVisuals).GetMethod("Show").Invoke(CardChoiceVisuals.instance, new object[] { args[1], true }); }
                    catch (Exception) { }
                }
                System.Collections.IEnumerator it;
                inMulti = true;
                try { it = (System.Collections.IEnumerator)m.Invoke(cc, new object[] { 1, args[1], args[2] }); }
                finally { inMulti = false; }
                while (it.MoveNext()) yield return it.Current;
            }
        }

        [HarmonyPatch(typeof(GM_ArmsRace), "OnEnable")]
        [HarmonyPrefix] static void OnEnablePre() { RoundsSettingsPlugin.Apply(); }

        [HarmonyPatch(typeof(GM_ArmsRace), "DoContinue")]
        [HarmonyPrefix] static void ContinuePre() { RoundsSettingsPlugin.Apply(); }

        [HarmonyPatch(typeof(GM_ArmsRace), "StartGame")]
        [HarmonyPrefix] static void StartPre() { RoundsSettingsPlugin.Apply(); }

        [HarmonyPatch(typeof(GM_ArmsRace), "ResetMatch")]
        [HarmonyPrefix] static void ResetPre() { RoundsSettingsPlugin.Apply(); }

        // перед проверкой «набрано ли нужное число очков/раундов»
        [HarmonyPatch(typeof(GM_ArmsRace), "RPCA_NextRound")]
        [HarmonyPrefix] static void NextRoundPre() { RoundsSettingsPlugin.Apply(); }
    }
}
