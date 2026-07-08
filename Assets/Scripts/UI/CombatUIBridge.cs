using System;
using RPG.Combat;

namespace RPG.UI
{
    /// <summary>
    /// Тонкая обёртка над CombatUI для внешних UI-панелей (например CardsPanel),
    /// чтобы они могли попросить пользователя выбрать цель кликом по портрету.
    /// </summary>
    public static class CombatUIBridge
    {
        public static void RequestTargetPick(Predicate<CombatUnit> targetFilter,
                                              Action<CombatUnit> onPicked,
                                              Action onCancel = null)
        {
            CombatUI.RequestTargetPickPublic(targetFilter, onPicked, onCancel);
        }
    }
}
