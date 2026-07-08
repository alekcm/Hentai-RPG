using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RPG.Domains
{
    /// <summary>
    /// 8 доменов из ГДД. Каждый класс имеет доступ к своему набору стартовых доменов.
    /// Игрок при создании выбирает 2 домена (один — привязан к классу, второй — свободный)
    /// и 2 карты из 6 доступных (по 3 на домен, 1-й ранг).
    /// </summary>
    public enum DomainType
    {
        Weapon = 0,   // Оружие
        Body = 1,     // Тело
        Magic = 2,    // Магия
        Charm = 3,    // Очарование
        Terror = 4,   // Ужас
        Nature = 5,   // Природа
        Light = 6,    // Свет
        Defense = 7   // Защита
    }

    public static class DomainNames
    {
        public static string GetRussianName(DomainType d) => d switch
        {
            DomainType.Weapon  => "Оружие",
            DomainType.Body    => "Тело",
            DomainType.Magic   => "Магия",
            DomainType.Charm   => "Очарование",
            DomainType.Terror  => "Ужас",
            DomainType.Nature  => "Природа",
            DomainType.Light   => "Свет",
            DomainType.Defense => "Защита",
            _ => d.ToString()
        };
    }

    /// <summary>
    /// Тип активации карты домена.
    /// Manual/Action  — игрок явно выбирает карту как своё Действие в бою.
    /// Reaction       — срабатывает автоматически по триггеру, но всегда через подтверждение игроком.
    /// Passive        — работает постоянно, ни клика, ни подтверждения.
    /// Spellcast      — то же Action, но требует Броска Заклинания против цели/СЛ.
    /// FreeAction     — действие, не тратящее ход (типа Молебна Жреца).
    /// </summary>
    public enum CardActivation
    {
        Manual,
        Action,
        Spellcast,
        Reaction,
        Passive,
        FreeAction
    }

    /// <summary>Что тратит карта при применении (помимо ходов).</summary>
    [Serializable]
    public struct CardCost
    {
        public int hope;         // сколько Надежды
        public int stamina;      // сколько Выносливости у активирующего юнита
        public int chargePerUse; // сколько заряда карты тратится (Высвобождение хаоса)
    }

    /// <summary>Тип триггера для Reaction-карт (когда предлагать использование).</summary>
    public enum CardTrigger
    {
        None,
        OnSelfTakingSeriousDamage,   // Оружие/"Вернуться в строй"
        OnAllyTakingDamage,          // Защита/"Я твой щит"
        OnAllyRollFailedOrFear,      // Свет/"Подбадривание"
        OnAdjacentEnemyLeaves,       // Воин/"Провоцированная атака"
        OnAttackerHits,              // Каратель/"Месть"
        OnHitByAttack,               // Каратель/"Возмездие", Мастер боевых искусств/"Защитная"
        OnKillOrEndOfCombat          // разное
    }

    [Serializable]
    public class DomainCard
    {
        public string cardId;
        public DomainType domain;
        public int rank = 1;
        public string displayName;
        [TextArea(3, 6)] public string description;

        public CardActivation activation = CardActivation.Manual;
        public CardCost cost;
        public CardTrigger trigger = CardTrigger.None;

        // Числовые параметры эффекта — интерпретируются исполнителем карты в CombatManager.
        // Что означают конкретные поля — зависит от cardId (см. DomainCardExecutor).
        public string damageDice;   // например "d10+2", "1d8", "2d6+3", "d6"
        public int intParam;        // радиус, дистанция, число целей и т.д.
        public int intParam2;
        public int difficultyClass; // СЛ для заклинаний, где указано (например "Мистический туман (13)")

        // Для карт с зарядами (например Высвобождение хаоса)
        public bool usesCharges;
        public int chargesBaseFormula; // 1 = 1 + половина уровня
    }

    public static class DomainDatabase
    {
        private static Dictionary<string, DomainCard> byId;
        private static Dictionary<DomainType, List<DomainCard>> byDomain;

        public static void Initialize()
        {
            var cards = BuildAllCards();
            byId = cards.ToDictionary(c => c.cardId, c => c);
            byDomain = cards.GroupBy(c => c.domain).ToDictionary(g => g.Key, g => g.ToList());
        }

        public static DomainCard GetCard(string id)
        {
            if (byId == null) Initialize();
            if (string.IsNullOrEmpty(id)) return null;
            return byId.TryGetValue(id, out var c) ? c : null;
        }

        public static List<DomainCard> GetCardsForDomain(DomainType domain)
        {
            if (byDomain == null) Initialize();
            return byDomain.TryGetValue(domain, out var list) ? new List<DomainCard>(list) : new();
        }

        public static IReadOnlyList<DomainType> AllDomains => (DomainType[])Enum.GetValues(typeof(DomainType));

        // ------------------------------------------------------------
        //  Данные всех 24 стартовых карт 1-го ранга (см. ERP Game.txt)
        // ------------------------------------------------------------
        private static List<DomainCard> BuildAllCards() => new()
        {
            // ---------- Оружие ----------
            new DomainCard {
                cardId = "weapon_1_no_way",
                domain = DomainType.Weapon, displayName = "Так не пойдёт",
                description = "При броске костей урона не может выпасть 1 или 2.",
                activation = CardActivation.Passive
            },
            new DomainCard {
                cardId = "weapon_1_second_wind",
                domain = DomainType.Weapon, displayName = "Вернуться в строй",
                description = "При получении серьёзного урона потратьте 1 Выносливость, чтобы снизить урон на одну шкалу.",
                activation = CardActivation.Reaction,
                trigger = CardTrigger.OnSelfTakingSeriousDamage,
                cost = new CardCost { stamina = 1 }
            },
            new DomainCard {
                cardId = "weapon_1_whirlwind",
                domain = DomainType.Weapon, displayName = "Вихрь",
                description = "После успешной атаки цели в пределах 2 кл. потратьте 1 Надежду, чтобы применить ту же атаку ко всем другим целям в 2 кл. Дополнительные противники, против которых вы преуспели, получают половину урона.",
                activation = CardActivation.Manual,
                cost = new CardCost { hope = 1 },
                intParam = 2 // радиус в клетках
            },

            // ---------- Тело ----------
            new DomainCard {
                cardId = "body_1_elusive",
                domain = DomainType.Body, displayName = "Неуловимый",
                description = "Получите постоянный бонус к Уклонению, равный половине вашего уровня.",
                activation = CardActivation.Passive
            },
            new DomainCard {
                cardId = "body_1_deft_maneuvers",
                domain = DomainType.Body, displayName = "Ловкие манёвры",
                description = "Потратьте 1 Выносливость, чтобы пробежать вдвое дальше обычного. После этого получите +1 к следующему Броску Атаки.",
                activation = CardActivation.Action,
                cost = new CardCost { stamina = 1 }
            },
            new DomainCard {
                cardId = "body_1_evasive",
                domain = DomainType.Body, displayName = "Увёртливость",
                description = "Потратьте 1 Выносливость, чтобы уклониться от любой атаки, совершённой по вам не вплотную.",
                activation = CardActivation.Reaction,
                trigger = CardTrigger.OnHitByAttack,
                cost = new CardCost { stamina = 1 }
            },

            // ---------- Магия ----------
            new DomainCard {
                cardId = "magic_1_book_of_ava",
                domain = DomainType.Magic, displayName = "Книга Авы",
                description = "Силовой толчок: Бросок Заклинания против цели вплотную. При успехе отбрасывает цель до 12 кл. и наносит d10+2 магического урона.\n" +
                              "Доспехи льда: Потратьте 1 Надежду — цель получает +1 к Показателю Брони.\n" +
                              "Обморожение: Бросок Заклинания в 12 кл. При успехе — d6 урона и цель временно Обездвижена.",
                activation = CardActivation.Manual,
                damageDice = "d10+2"
            },
            new DomainCard {
                cardId = "magic_1_book_of_taifar",
                domain = DomainType.Magic, displayName = "Книга Тайфара",
                description = "Дрёма: Бросок Заклинания против цели в 2 кл. При успехе цель Засыпает, пока не получит урон или ведущий не потратит Страх, чтобы закончить состояние.\n" +
                              "Дикое пламя: Бросок Заклинания против до трёх противников вплотную. Успех — 2d6 магического урона и потеря Выносливости.\n" +
                              "Мистический туман: Бросок Заклинания (СЛ 13) — до конца боя создаётся стационарный туман 5×5 кл. с центром на вашей клетке.",
                activation = CardActivation.Manual,
                damageDice = "2d6",
                difficultyClass = 13
            },
            new DomainCard {
                cardId = "magic_1_chaos_release",
                domain = DomainType.Magic, displayName = "Высвобождение хаоса",
                description = "В начале боя заряжает эту карту. Максимальный заряд = 1 + половина уровня.\n" +
                              "Бросок Заклинания: потратьте любое количество заряда — нанесите столько же d10 урона.\n" +
                              "Когда заряд закончился, можно потратить 1 Выносливость, чтобы полностью восстановить его.",
                activation = CardActivation.Spellcast,
                usesCharges = true,
                chargesBaseFormula = 1,
                cost = new CardCost { chargePerUse = 1 }
            },

            // ---------- Очарование ----------
            new DomainCard {
                cardId = "charm_1_provoke",
                domain = DomainType.Charm, displayName = "Провокация",
                description = "Бросок Заклинания против цели в 4 кл. При успехе цель получает помеху на атаки любого, кроме вас. Также можно потратить 1 Выносливость, чтобы цель потратила Выносливость.",
                activation = CardActivation.Spellcast,
                intParam = 4
            },
            new DomainCard {
                cardId = "charm_1_trick",
                domain = DomainType.Charm, displayName = "Уловка",
                description = "Атакуя цель, потратьте 1 Надежду, чтобы сделать её Уязвимой на эту атаку.",
                activation = CardActivation.Manual,
                cost = new CardCost { hope = 1 }
            },
            new DomainCard {
                cardId = "charm_1_inspiring_words",
                domain = DomainType.Charm, displayName = "Вдохновляющие слова",
                description = "Один раз за бой сделайте одно из: восстановить цели 1 шкалу здоровья / восстановить цели Выносливость / получить 1 Надежду.",
                activation = CardActivation.FreeAction,
                intParam = 1 // раз за бой
            },

            // ---------- Ужас ----------
            new DomainCard {
                cardId = "terror_1_voice_of_dread",
                domain = DomainType.Terror, displayName = "Глас ужаса",
                description = "Бросок Заклинания против цели в поле зрения. При успехе цель теряет Выносливость и становится временно Уязвимой.",
                activation = CardActivation.Spellcast
            },
            new DomainCard {
                cardId = "terror_1_withering_strike",
                domain = DomainType.Terror, displayName = "Удар увядания",
                description = "Бросок Заклинания против цели в 12 кл. Успех — d6+1 маг. урона, следующий урон цели по союзнику будет вдвое меньше. Успех со Страхом — d10+1 маг. урона вместо d6+1.",
                activation = CardActivation.Spellcast,
                damageDice = "d6+1",
                intParam = 12
            },
            new DomainCard {
                cardId = "terror_1_expose_weakness",
                domain = DomainType.Terror, displayName = "Выявить слабость",
                description = "Бросок Заклинания. Буря поражает все цели в 2 кл. Успех — d8+2 маг. урона. По Уязвимой цели — ещё 1d8 сверху.",
                activation = CardActivation.Spellcast,
                damageDice = "d8+2",
                intParam = 2
            },

            // ---------- Природа ----------
            new DomainCard {
                cardId = "nature_1_entangling_vines",
                domain = DomainType.Nature, displayName = "Опутывающие лозы",
                description = "Бросок Заклинания против цели в 12 кл. Успех — 1d8+1 физ. урона и цель временно Обездвижена. Можно потратить 1 Надежду, чтобы Обездвижить ещё одного противника в 2 кл. от цели.",
                activation = CardActivation.Spellcast,
                damageDice = "1d8+1",
                intParam = 12
            },
            new DomainCard {
                cardId = "nature_1_regeneration",
                domain = DomainType.Nature, displayName = "Регенерация",
                description = "Коснитесь существа, потратьте 3 Надежды — восполните 3 шкалы здоровья и восстановите Выносливость.",
                activation = CardActivation.Manual,
                cost = new CardCost { hope = 3 }
            },
            new DomainCard {
                cardId = "nature_1_elemental_guardian",
                domain = DomainType.Nature, displayName = "Хранитель стихий",
                description = "Потратьте 1 Выносливость и выберите стихию:\n" +
                              "Огонь — противник рядом получает 1d10 маг. урона, ударив вас.\n" +
                              "Земля — бонус к Порогам Урона = вашему уровню.\n" +
                              "Вода — при вашем следующем ударе вплотную все другие враги в 2 кл. теряют Выносливость.\n" +
                              "Воздух — в след. раунд удвойте передвижение и игнорируйте опасности местности.",
                activation = CardActivation.Manual,
                cost = new CardCost { stamina = 1 }
            },

            // ---------- Свет ----------
            new DomainCard {
                cardId = "light_1_encourage",
                domain = DomainType.Light, displayName = "Подбадривание",
                description = "Один раз за бой при провале или броске со Страхом союзник может перебросить свои кости.",
                activation = CardActivation.Reaction,
                trigger = CardTrigger.OnAllyRollFailedOrFear,
                intParam = 1
            },
            new DomainCard {
                cardId = "light_1_arrow_of_light",
                domain = DomainType.Light, displayName = "Стрела света",
                description = "Бросок Заклинания против цели в 12 кл. При успехе потратьте 1 Надежду — d8+2 маг. урона, цель становится временно Уязвимой.",
                activation = CardActivation.Spellcast,
                cost = new CardCost { hope = 1 },
                damageDice = "d8+2",
                intParam = 12
            },
            new DomainCard {
                cardId = "light_1_healing_touch",
                domain = DomainType.Light, displayName = "Исцеляющее касание",
                description = "Потратьте 2 Надежды, чтобы восстановить цели вплотную 2 шкалы здоровья или 1 шкалу здоровья и Выносливость.",
                activation = CardActivation.Manual,
                cost = new CardCost { hope = 2 }
            },

            // ---------- Защита ----------
            new DomainCard {
                cardId = "defense_1_throwback",
                domain = DomainType.Defense, displayName = "Отбрасывание",
                description = "Атака основным оружием по цели вплотную. Успех — урон + отталкивание до 4 кл. Успех с Надеждой — +d6 к урону. Можно потратить 1 Надежду, чтобы сделать цель временно Уязвимой.",
                activation = CardActivation.Action,
                intParam = 4
            },
            new DomainCard {
                cardId = "defense_1_i_am_your_shield",
                domain = DomainType.Defense, displayName = "Я твой щит",
                description = "Когда союзник в близкой дистанции должен получить урон, потратьте 1 Выносливость, чтобы принять урон на себя. ВЕСЬ этот урон сперва идёт по броне.",
                activation = CardActivation.Reaction,
                trigger = CardTrigger.OnAllyTakingDamage,
                cost = new CardCost { stamina = 1 }
            },
            new DomainCard {
                cardId = "defense_1_defensive_mastery",
                domain = DomainType.Defense, displayName = "Защитное мастерство",
                description = "Увеличьте Пороги Урона на значение вашего уровня.",
                activation = CardActivation.Passive
            }
        };
    }
}
