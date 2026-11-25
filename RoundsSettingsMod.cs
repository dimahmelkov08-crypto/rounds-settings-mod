using System;
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
        public static ConfigEntry<int> TimeLimit;     // сек на очко, 0 = без лимита
        public static ConfigEntry<KeyCode> MenuKey;
        public static RoundsSettingsPlugin Instance;

        // меню
        bool menuOpen;
        Rect win = new Rect(40, 60, 380, 330);
        GUIStyle titleStyle, hintStyle;

        // таймер
        float roundTimer;
        bool wasActive;
        bool timeUpFired;

        void Awake()
        {
            Instance = this;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            Enabled = Config.Bind("General", "Enabled", true, "Включить мод");
            RoundsToWin = Config.Bind("General", "RoundsToWin", 2, "Раундов до победы в матче (в игре по умолчанию 2)");
            PointsToWin = Config.Bind("General", "PointsToWin", 2, "Очков до победы в раунде (в игре по умолчанию 2)");
            TimeLimit = Config.Bind("General", "TimeLimitSeconds", 0, "Лимит времени на очко, сек. 0 = выключен");
            MenuKey = Config.Bind("General", "MenuKey", KeyCode.F8, "Клавиша меню");

            new Harmony(GUID).PatchAll(typeof(Patches));
            Logger.LogInfo(NAME + " " + VERSION + " loaded. Menu key: " + MenuKey.Value);
        }

        void Update()
        {
            if (Input.GetKeyDown(MenuKey.Value)) menuOpen = !menuOpen;
            TickRoundTimer();
        }

        // ---------- синхронизация (хост = источник правды) ----------
        const string K_ON = "rs_on", K_R = "rs_r", K_P = "rs_p", K_T = "rs_t";
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
        public static int EffTime { get { return IsClient ? Prop(K_T, 0) : TimeLimit.Value; } }

        static void Publish()
        {
            if (!Online || !PhotonNetwork.IsMasterClient) return;
            var pr = PhotonNetwork.CurrentRoom.CustomProperties;
            int on = Enabled.Value ? 1 : 0;
            if (Prop(K_ON, -1) == on && Prop(K_R, -1) == RoundsToWin.Value && Prop(K_P, -1) == PointsToWin.Value && Prop(K_T, -1) == TimeLimit.Value) return;
            var h = new ExitGames.Client.Photon.Hashtable();
            h[K_ON] = on; h[K_R] = RoundsToWin.Value; h[K_P] = PointsToWin.Value; h[K_T] = TimeLimit.Value;
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

            // таймер на экране
            if (EffEnabled && EffTime > 0 && wasActive)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                s.normal.textColor = TimeLeft <= 10f ? Color.red : Color.white;
                GUI.Label(new Rect(sw / 2f - 100, 8, 200, 44), Mathf.CeilToInt(TimeLeft).ToString(), s);
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
                GUILayout.Label("Лимит времени на очко: " + (EffTime == 0 ? "выкл." : EffTime + " сек"));
                if (GUILayout.Button("Закрыть")) menuOpen = false;
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 22));
                return;
            }
            Enabled.Value = GUILayout.Toggle(Enabled.Value, " Мод включён");
            GUILayout.Space(6);

            RoundsToWin.Value = IntSlider("Раундов до победы в матче", RoundsToWin.Value, 1, 1000);
            PointsToWin.Value = IntSlider("Очков до победы в раунде", PointsToWin.Value, 1, 1000);

            int t = TimeLimit.Value;
            GUILayout.Label("Лимит времени на очко: " + (t == 0 ? "выкл." : t + " сек"));
            t = Mathf.RoundToInt(GUILayout.HorizontalSlider(t, 0, 300) / 5f) * 5;
            TimeLimit.Value = t;
            GUILayout.Label("По истечении времени умирает игрок с меньшим HP (при равенстве — случайный).", hintStyle);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Стандарт (2/2)")) { RoundsToWin.Value = 2; PointsToWin.Value = 2; TimeLimit.Value = 0; }
            if (GUILayout.Button("Быстро (1/1, 30с)")) { RoundsToWin.Value = 1; PointsToWin.Value = 1; TimeLimit.Value = 30; }
            if (GUILayout.Button("Марафон (5/3)")) { RoundsToWin.Value = 5; PointsToWin.Value = 3; TimeLimit.Value = 0; }
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
