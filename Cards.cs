using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Новые карты. Каждая карта — клон простой карты «Tank» со своими характеристиками
// (изменяются только Gun и CharacterStatModifiers) и русскими названием/описанием.
namespace RoundsSettingsMod
{
    public class Eff
    {
        public char Comp;      // 'G' = Gun, 'P' = CharacterStatModifiers
        public string Field;
        public float Value;
        public string Label;
        public string Amount;
        public bool Positive;
    }

    public class CardDef
    {
        public string Id, Name, Desc;
        public string Rarity;
        public List<Eff> Effs = new List<Eff>();
        public int Num; // номер идеи в списке (0 = дополнительная карта)
    }

    public static class CardCatalog
    {
        public static readonly List<CardDef> All = new List<CardDef>();
        public static readonly Dictionary<string, CardDef> ById = new Dictionary<string, CardDef>();

        const string Common = "Common", Unc = "Uncommon", Rare = "Rare";

        static string Pct(float f) { return (f >= 0 ? "+" : "") + Mathf.RoundToInt(f * 100f) + "%"; }
        static string F1(float x) { return x.ToString("0.#").Replace(',', '.'); }

        static Eff Mul(char c, string f, float v, string label, bool higherIsGood)
        {
            return new Eff { Comp = c, Field = f, Value = v, Label = label, Amount = Pct(v - 1f), Positive = higherIsGood ? v >= 1f : v <= 1f };
        }
        // attackSpeed — задержка между выстрелами: меньше = быстрее
        static Eff Rate(float v) { return new Eff { Comp = 'G', Field = "attackSpeed", Value = v, Label = "Скорострельность", Amount = Pct(1f / v - 1f), Positive = v <= 1f }; }
        static Eff Add(char c, string f, float v, string label, string amount, bool positive)
        {
            return new Eff { Comp = c, Field = f, Value = v, Label = label, Amount = amount, Positive = positive };
        }

        static Eff Dmg(float v) { return Mul('G', "damage", v, "Урон", true); }
        static Eff Hp(float v) { return Mul('P', "health", v, "Здоровье", true); }
        static Eff Speed(float v) { return Mul('P', "movementSpeed", v, "Скорость", true); }
        static Eff Jump(float v) { return Mul('P', "jump", v, "Высота прыжка", true); }
        static Eff Grav(float v) { return Mul('P', "gravity", v, "Гравитация", false); }
        static Eff Size(float v) { return Mul('P', "sizeMultiplier", v, "Размер", false); }
        static Eff Spread(float v) { return Mul('G', "multiplySpread", v, "Разброс", false); }
        static Eff BSpeed(float v) { return Mul('G', "projectileSpeed", v, "Скорость пули", true); }
        static Eff Knock(float v) { return Mul('G', "knockback", v, "Отбрасывание", true); }
        static Eff Reload(float v) { return Mul('G', "reloadTime", v, "Перезарядка", false); }
        static Eff ReloadAdd(float s) { return Add('G', "reloadTimeAdd", s, "Перезарядка", (s >= 0 ? "+" : "") + F1(s) + "с", s <= 0f); }
        static Eff Ammo(int n) { return Add('G', "ammo", n, "Патроны", (n >= 0 ? "+" : "") + n, n >= 0); }
        static Eff Bullets(int n) { return Add('G', "numberOfProjectiles", n, "Пули", (n >= 0 ? "+" : "") + n, n >= 0); }
        static Eff Bounce(int n) { return Add('G', "reflects", n, "Отскоки", (n >= 0 ? "+" : "") + n, n >= 0); }
        static Eff Jumps(int n) { return Add('P', "numberOfJumps", n, "Прыжки", (n >= 0 ? "+" : "") + n, n >= 0); }
        static Eff Regen(float x) { return Add('P', "regen", x, "Регенерация", "+" + F1(x) + " HP/с", true); }
        static Eff Steal(float x) { return Add('P', "lifeSteal", x, "Вампиризм", Pct(x), true); }
        static Eff Respawn(int n) { return Add('P', "respawns", n, "Воскрешения", "+" + n, true); }
        static Eff Slow(float x) { return Add('G', "slow", x, "Замедление цели", Pct(x), true); }

        static void C(int num, string id, string name, string desc, string rarity, params Eff[] effs)
        {
            var d = new CardDef { Num = num, Id = "RS_" + id, Name = name, Desc = desc, Effs = new List<Eff>(effs) };
            d.Rarity = rarity;
            All.Add(d); ById[d.Id] = d;
        }

        static CardCatalog()
        {
            // --- из первого списка (1–50) ---
            C(1, "shotgun", "Дробовик", "Четыре дополнительные пули за выстрел", Unc, Bullets(4), Spread(2f));
            C(2, "needle", "Игла", "Сильный, но медленный выстрел", Common, Dmg(1.5f), Rate(1.3f));
            C(3, "ricochet", "Рикошетчик", "Пули отскакивают от стен", Common, Bounce(3), Dmg(0.8f));
            C(4, "sniper", "Снайпер", "Очень быстрая пуля, долгая перезарядка", Unc, BSpeed(2f), ReloadAdd(1f));
            C(5, "heavycharge", "Тяжёлый заряд", "Мощные медленные пули, сильно отбрасывают", Unc, Dmg(1.8f), Knock(1.8f), BSpeed(0.7f));
            C(6, "burstmag", "Очередь", "Больше патронов в магазине, но хуже кучность", Common, Ammo(3), Spread(1.3f));
            C(8, "parasite2", "Кровосос", "Часть урона возвращается здоровьем", Unc, Steal(0.15f), Hp(0.8f));
            C(13, "ironskin", "Железная кожа", "Крепче, но медленнее", Common, Hp(1.5f), Speed(0.9f));
            C(14, "secondwind", "Второе дыхание", "Одно воскрешение за раунд", Rare, Respawn(1), Hp(0.7f));
            C(25, "triplejump", "Тройной прыжок", "Два дополнительных прыжка", Unc, Jumps(2), Jump(0.85f));
            C(27, "feather", "Лёгкое перо", "Падаешь медленно и летишь выше", Common, Grav(0.6f), Hp(0.85f));
            C(28, "heavyweight", "Тяжеловес", "Тяжёлый, но бьёшь сильнее", Unc, Grav(1.5f), Dmg(1.3f));
            C(40, "berserk", "Берсерк", "Двойной урон, но хрупкий", Rare, Dmg(2f), Hp(0.7f));
            C(41, "glassgun", "Стеклянная пушка", "Тройной урон, но почти без здоровья", Rare, Dmg(3f), Hp(0.3f));
            // --- из второго списка (51–150) ---
            C(51, "laser", "Лазерный прицел", "Пули летят точнее", Common, Spread(0.5f), BSpeed(1.1f));
            C(59, "blunt", "Тупой выстрел", "Крупная пуля попадает чаще, но бьёт слабее", Common, Dmg(0.65f), Spread(0.9f));
            C(61, "vampire", "Вампирик", "Сильный вампиризм, но меньше здоровья", Rare, Steal(0.3f), Hp(0.75f));
            C(70, "machinegun", "Пулемёт", "Очень быстрая стрельба, слабые пули", Unc, Rate(0.5f), Dmg(0.6f));
            C(71, "bodyarmor", "Бронежилет", "Больше здоровья, ниже прыжок", Common, Hp(1.25f), Jump(0.85f));
            C(72, "medkit", "Аптечка", "Заметно больше здоровья", Common, Hp(1.3f), Dmg(0.9f));
            C(73, "regeneration", "Регенерация", "Здоровье восстанавливается само", Unc, Regen(1f), Speed(0.95f));
            C(88, "oak", "Дуб", "Много здоровья, но не прыгнуть высоко", Common, Hp(1.5f), Jump(0.9f));
            C(103, "sprinter", "Спринтер", "Быстро бегаешь, но прыгаешь ниже", Common, Speed(1.3f), Jump(0.8f));
            C(104, "jumper", "Прыгун", "Прыгаешь очень высоко, бегаешь медленнее", Common, Jump(1.5f), Speed(0.85f));
            C(115, "brake", "Тормоз", "Попадания замедляют цель, пули медленнее", Common, Slow(0.2f), BSpeed(0.8f));
            // --- дополнительные карты по тем же идеям ---
            C(0, "wideburst", "Веер", "Больше пуль веером, слабее каждая", Unc, Bullets(2), Dmg(0.75f), Spread(1.4f));
            C(0, "longmag", "Длинный магазин", "Больше патронов, медленнее перезарядка", Common, Ammo(4), Reload(1.4f));
            C(0, "quickhands", "Быстрые руки", "Быстрая перезарядка, меньше урона", Common, Reload(0.6f), Dmg(0.85f));
            C(0, "tinyman", "Малыш", "Маленький размер, мало здоровья", Unc, Size(0.7f), Hp(0.8f), Speed(1.1f));
            C(0, "giant", "Великан", "Большой и очень крепкий", Unc, Size(1.4f), Hp(1.7f), Speed(0.85f));
            C(0, "bulletstorm", "Шквал", "Много патронов и скорострельность, слабый урон", Rare, Ammo(6), Rate(0.7f), Dmg(0.7f));
            C(0, "deadeye", "Меткий глаз", "Точнее, быстрее пули, чуть слабее", Common, Spread(0.6f), BSpeed(1.3f), Dmg(0.9f));
            C(0, "slowpoke", "Увалень", "Бьёшь сильнее, но ходишь медленнее", Common, Dmg(1.35f), Speed(0.8f));
            C(0, "ghostly", "Призрак", "Очень лёгкий и быстрый, но хрупкий", Unc, Grav(0.5f), Speed(1.25f), Hp(0.7f));
            C(0, "bouncebomb", "Мячик", "Много отскоков, слабый урон", Unc, Bounce(5), Dmg(0.7f));
            C(0, "gunslinger", "Стрелок", "Больше скорострельность и урон, меньше здоровья", Unc, Rate(0.8f), Dmg(1.15f), Hp(0.8f));
            C(0, "jugglernaut", "Громила", "Бьёшь сильнее и крепче, но стреляешь медленнее", Rare, Dmg(1.4f), Hp(1.3f), Rate(1.5f));
            C(0, "leech2", "Пиявка", "Лечит от попаданий, но стреляет реже", Unc, Steal(0.2f), Rate(1.25f));
            C(0, "phoenix2", "Феникс-лайт", "Возрождение, но слабее урон", Rare, Respawn(1), Dmg(0.8f));
            C(0, "airwalker", "Воздушный ходок", "Лишний прыжок и лёгкость, меньше урона", Common, Jumps(1), Grav(0.8f), Dmg(0.9f));
            C(0, "knockout", "Нокаут", "Сильно отбрасывает цель, но стреляет медленнее", Common, Knock(2.5f), Rate(1.2f));
            C(0, "snail", "Улитка-снайпер", "Огромный урон, медленная пуля и перезарядка", Rare, Dmg(2.2f), BSpeed(0.6f), Reload(1.5f));
            C(0, "turtle", "Черепаха", "Здоровья вдвое больше, скорость вдвое ниже", Unc, Hp(2f), Speed(0.5f));
            C(0, "hummingbird", "Колибри", "Скорость и прыжок, но здоровья почти нет", Unc, Speed(1.4f), Jump(1.25f), Hp(0.5f));
            C(0, "marksman", "Стрелок-ветеран", "Больше патронов и меткость", Common, Ammo(2), Spread(0.8f));
            C(0, "ironwill", "Железная воля", "Регенерация и здоровье, но слабее урон", Rare, Regen(0.8f), Hp(1.2f), Dmg(0.8f));
        }
    }

    public static class CardEngine
    {
        static GameObject holder;

        public static void Register(CardChoice cc)
        {
            if (!RoundsSettingsPlugin.CustomCards.Value) return;
            foreach (var c in cc.cards) if (c.gameObject.name.StartsWith("RS_")) return; // уже добавлено
            CardInfo tank = null;
            foreach (var c in cc.cards) if (c.gameObject.name == "Tank") tank = c;
            if (tank == null) { RoundsSettingsPlugin.Warn("Cards: source card Tank not found"); return; }

            holder = new GameObject("RS_CardHolder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);

            var list = new List<CardInfo>(cc.cards);
            var tankStat0 = tank.cardStats[0];
            var locField = Traverse.Create(tankStat0).Field("m_localizedStat").GetValue();
            int ok = 0;
            foreach (var def in CardCatalog.All)
            {
                try
                {
                    var go = UnityEngine.Object.Instantiate(tank.gameObject, holder.transform);
                    go.name = def.Id;
                    var ci = go.GetComponent<CardInfo>();
                    typeof(CardInfo).GetField("rarity").SetValue(ci, Enum.Parse(typeof(CardInfo).GetField("rarity").FieldType, def.Rarity));
                    ci.sourceCard = ci;
                    var stats = new CardInfoStat[def.Effs.Count];
                    for (int i = 0; i < stats.Length; i++)
                    {
                        var e = def.Effs[i];
                        var st = new CardInfoStat();
                        st.stat = e.Label; st.amount = e.Amount; st.positive = e.Positive;
                        Traverse.Create(st).Field("m_localizedStat").SetValue(locField);
                        stats[i] = st;
                    }
                    ci.cardStats = stats;

                    var gun = go.GetComponent<Gun>();
                    var pl = go.GetComponent<CharacterStatModifiers>();
                    gun.reloadTimeAdd = 0f; gun.attackSpeed = 1f; pl.health = 1f;
                    foreach (var e in def.Effs)
                    {
                        object target = e.Comp == 'G' ? (object)gun : pl;
                        var f = target.GetType().GetField(e.Field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f == null) { RoundsSettingsPlugin.Warn("Cards: no field " + e.Field); continue; }
                        if (f.FieldType == typeof(int)) f.SetValue(target, (int)e.Value); else f.SetValue(target, e.Value);
                    }
                    list.Add(ci);
                    ok++;
                }
                catch (Exception ex) { RoundsSettingsPlugin.Warn("Cards: failed " + def.Id + ": " + ex); }
            }
            cc.cards = list.ToArray();
            RoundsSettingsPlugin.Warn("Cards: added " + ok + " custom cards (total " + cc.cards.Length + ")");
        }

        static string Strip(string n) { return n.Replace("(Clone)", "").Trim(); }

        public static CardDef Lookup(CardInfo ci)
        {
            CardDef d;
            return CardCatalog.ById.TryGetValue(Strip(ci.gameObject.name), out d) ? d : null;
        }

        static void SetTmp(Transform t, string text)
        {
            var tm = t.GetComponent<TMPro.TMP_Text>();
            if (tm == null) return;
            foreach (var mb in t.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "UILocalizedString") UnityEngine.Object.Destroy(mb);
            tm.text = text;
        }

        // после Awake карты: подменяем название, описание и строки статов
        public static void FixTexts(CardInfo ci)
        {
            var def = Lookup(ci);
            if (def == null) return;
            foreach (var t in ci.transform.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Text_Name") SetTmp(t, def.Name.ToUpper());
                else if (t.name == "EffectText") SetTmp(t, def.Desc);
                else if (t.name == "Grid")
                {
                    int idx = 0;
                    foreach (Transform row in t)
                    {
                        if (!row.name.StartsWith("StatObject")) continue;
                        if (idx >= def.Effs.Count) { row.gameObject.SetActive(false); continue; }
                        var e = def.Effs[idx++];
                        var stat = row.Find("Stat"); var val = row.Find("Value");
                        if (stat != null) SetTmp(stat, e.Label);
                        if (val != null) SetTmp(val, e.Amount);
                    }
                }
            }
        }
    }

    static class CardPatches
    {
        [HarmonyPatch(typeof(CardChoice), "Start")]
        [HarmonyPrefix] static void CardChoiceStartPre(CardChoice __instance)
        {
            try { CardEngine.Register(__instance); }
            catch (Exception e) { RoundsSettingsPlugin.Warn("Cards.Register: " + e); }
        }

        [HarmonyPatch(typeof(CardInfo), "Awake")]
        [HarmonyPostfix] static void CardAwakePost(CardInfo __instance)
        {
            try { CardEngine.FixTexts(__instance); }
            catch (Exception e) { RoundsSettingsPlugin.Warn("Cards.FixTexts: " + e); }
        }

        [HarmonyPatch(typeof(CardInfo), "get_CardName")]
        [HarmonyPostfix] static void NamePost(CardInfo __instance, ref string __result)
        {
            var d = CardEngine.Lookup(__instance); if (d != null) __result = d.Name;
        }

        [HarmonyPatch(typeof(CardInfo), "get_CardDescription")]
        [HarmonyPostfix] static void DescPost(CardInfo __instance, ref string __result)
        {
            var d = CardEngine.Lookup(__instance); if (d != null) __result = d.Desc;
        }
    }
}
