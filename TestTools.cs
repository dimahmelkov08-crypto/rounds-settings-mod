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
        public static void GiveCard(CardInfo ci)
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
                Status = "Выдана: " + ci.CardName;
            }
            catch (Exception e) { Status = "Ошибка: " + e.Message; RoundsSettingsPlugin.Warn("TestTools.GiveCard: " + e); }
        }

        static IEnumerator DestroyLater(GameObject go)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (go != null) RoundsSettingsPlugin.KillCard(go);
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
