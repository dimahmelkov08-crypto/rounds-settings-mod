using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Режим тестирования для игры в одиночку (Локально -> Песочница):
// запуск выбора карт как в раунде, выдача любой карты, восстановление здоровья.
namespace RoundsSettingsMod
{
    public static class TestTools
    {
        public static string Status = "";

        public static Player Me()
        {
            var pm = PlayerManager.instance;
            if (pm == null || pm.players == null || pm.players.Count == 0) return null;
            return pm.players[0];
        }

        public static bool HasPlayer { get { return Me() != null; } }

        // выбор карты ровно как в раунде (учитывает «Карт за выбор», реролл, число карт на выбор и т.д.)
        public static void StartPick()
        {
            try
            {
                var me = Me();
                if (me == null) { Status = "Нет игрока: Локально → Песочница"; return; }
                var cc = CardChoice.instance;
                if (Traverse.Create(cc).Field("IsPicking").GetValue<bool>()) { Status = "Выбор карты уже идёт"; return; }
                int pid = me.PlayerID;
                typeof(CardChoiceVisuals).GetMethod("Show").Invoke(CardChoiceVisuals.instance, new object[] { pid, true });
                var m = typeof(CardChoice).GetMethod("DoPick");
                var pt = Enum.ToObject(m.GetParameters()[2].ParameterType, 1);
                cc.StartCoroutine((IEnumerator)m.Invoke(cc, new object[] { 1, pid, pt }));
                Status = "Выбор карты запущен";
            }
            catch (Exception e) { Status = "Ошибка: " + e.Message; RoundsSettingsPlugin.Warn("TestTools.StartPick: " + e); }
        }

        // выдать карту сразу
        public static void GiveCard(CardInfo ci, bool quiet = false)
        {
            try
            {
                var me = Me();
                if (me == null) { Status = "Нет игрока: Локально → Песочница"; return; }
                var cc = CardChoice.instance;
                var spawn = typeof(CardChoice).GetMethod("Spawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var go = (GameObject)spawn.Invoke(cc, new object[] { ci.gameObject, me.transform.position, Quaternion.identity });
                var acs = go.GetComponentInChildren<ApplyCardStats>();
                var pick = typeof(ApplyCardStats).GetMethod("Pick");
                var pt = Enum.ToObject(pick.GetParameters()[2].ParameterType, 1);
                pick.Invoke(acs, new object[] { me.PlayerID, false, pt });
                cc.StartCoroutine(DestroyLater(go));
                if (!quiet) Status = "Выдана: " + ci.CardName;
            }
            catch (Exception e) { Status = "Ошибка: " + e.Message; RoundsSettingsPlugin.Warn("TestTools.GiveCard: " + e); }
        }

        static IEnumerator DestroyLater(GameObject go)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (go != null) RoundsSettingsPlugin.KillCard(go);
        }

        // ---- отмена выбранных карт ----
        public static List<CardInfo> Current()
        {
            var me = Me();
            return me == null ? new List<CardInfo>() : new List<CardInfo>(me.data.currentCards);
        }

        static void ResetAll()
        {
            var me = Me();
            Traverse.Create(me).Method("FullReset").GetValue();
            me.data.currentCards.Clear();
            if (CardBarHandler.instance != null) CardBarHandler.instance.ResetCardBards();
        }

        // оставить только указанные карты (сброс + повторная выдача)
        static void Rebuild(List<CardInfo> keep)
        {
            ResetAll();
            foreach (var c in keep) GiveCard(c, true);
            try { Me().data.healthHandler.Heal(999999f); } catch (Exception) { }
        }

        public static void RemoveAt(int index)
        {
            try
            {
                if (Me() == null) { Status = "Нет игрока"; return; }
                var list = Current();
                if (index < 0 || index >= list.Count) return;
                list.RemoveAt(index);
                Rebuild(list);
                Status = "Карта отменена";
            }
            catch (Exception e) { Status = "Ошибка: " + e.Message; RoundsSettingsPlugin.Warn("TestTools.RemoveAt: " + e); }
        }

        public static void UndoLast()
        {
            var n = Current().Count;
            if (n == 0) { Status = "Нет карт"; return; }
            RemoveAt(n - 1);
        }

        public static void ClearCards()
        {
            try
            {
                if (Me() == null) { Status = "Нет игрока"; return; }
                ResetAll();
                try { Me().data.healthHandler.Heal(999999f); } catch (Exception) { }
                Status = "Все карты сброшены";
            }
            catch (Exception e) { Status = "Ошибка: " + e.Message; RoundsSettingsPlugin.Warn("TestTools.ClearCards: " + e); }
        }

        // ---- лёгкий бот ----
        // добавляет второго игрока под управлением ИИ (нужен хотя бы один игрок: на экране «Поединок» после присоединения или в игре)
        public static void AddEasyBot()
        {
            try
            {
                var pa = PlayerAssigner.instance;
                if (pa == null) { Status = "Нет PlayerAssigner"; return; }
                int before = PlayerManager.instance.players.Count;
                var m = typeof(PlayerAssigner).GetMethod("CreatePlayer");
                m.Invoke(pa, new object[] { null, true });
                RoundsSettingsPlugin.Instance.StartCoroutine(EaseBot(before));
                Status = "Бот добавлен";
            }
            catch (Exception e) { Status = "Ошибка: " + (e.InnerException != null ? e.InnerException.Message : e.Message); RoundsSettingsPlugin.Warn("TestTools.AddEasyBot: " + e); }
        }

        static IEnumerator EaseBot(int countBefore)
        {
            yield return new WaitForSecondsRealtime(1f);
            try
            {
                var pm = PlayerManager.instance;
                if (pm.players.Count <= countBefore) yield break;
                var bot = pm.players[pm.players.Count - 1];
                var gun = Traverse.Create(bot.data.weaponHandler).Field("gun").GetValue<Gun>();
                if (gun != null) { gun.damage *= 0.35f; gun.attackSpeed *= 2f; }
                Status = "Лёгкий бот готов (урон ×0.35, стреляет реже)";
            }
            catch (Exception e) { RoundsSettingsPlugin.Warn("EaseBot: " + e.Message); }
        }

        // ---- достижения Steam ----
        public static bool AchConfirm;
        public static void UnlockAllAchievements()
        {
            int ok = 0, fail = 0;
            foreach (FriendlyFoe.Platforms.Achievements a in Enum.GetValues(typeof(FriendlyFoe.Platforms.Achievements)))
            {
                try { FriendlyFoe.Platform.PlatformManager.UnlockAchievement(a); ok++; }
                catch (Exception e) { fail++; RoundsSettingsPlugin.Warn("Achievement " + a + ": " + e.Message); }
            }
            Status = "Достижения: отправлено " + ok + (fail > 0 ? ", ошибок " + fail : "");
        }

        public static void Heal()
        {
            var me = Me();
            if (me == null) { Status = "Нет игрока: Локально → Песочница"; return; }
            try { me.data.healthHandler.Heal(999999f); Status = "Здоровье восстановлено"; }
            catch (Exception e) { Status = "Ошибка: " + e.Message; }
        }

        public static List<CardInfo> AllCards()
        {
            var res = new List<CardInfo>();
            var cc = CardChoice.instance;
            if (cc != null && cc.cards != null) res.AddRange(cc.cards);
            return res;
        }
    }
}
