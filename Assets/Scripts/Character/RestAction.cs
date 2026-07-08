using UnityEngine;
using RPG.Core;

namespace RPG.Character
{
    /// <summary>
    /// Действие «Отдых» вне боя (по ГДД):
    /// «Бросает монетку страха. При выпадении Надежды, вместо Надежды восстанавливает Выносливость».
    ///
    /// Интерпретация: 50/50; при «Надежде» — +1 Выносливость; при «Страхе» — эффект нейтральный/тратим час времени.
    /// Может использоваться в диалоге, в лагере, из debug-панели.
    /// </summary>
    public static class RestAction
    {
        public static RestResult Perform(CharacterBase c)
        {
            if (c == null) return new RestResult { message = "Некому отдыхать." };
            bool hopeSide = Random.value < 0.5f;
            if (hopeSide)
            {
                int before = c.stats.currentStamina;
                c.stats.RestoreStamina(1);
                int gained = c.stats.currentStamina - before;
                GameManager.Instance?.AdvanceTime(0.5f);
                return new RestResult
                {
                    hopeSide = true,
                    gainedStamina = gained,
                    message = gained > 0
                        ? $"{c.displayName} отдыхает: восстановлено {gained} Выносливости."
                        : $"{c.displayName} отдыхает, но Выносливость уже полна."
                };
            }
            else
            {
                GameManager.Instance?.AdvanceTime(0.5f);
                return new RestResult
                {
                    hopeSide = false,
                    gainedStamina = 0,
                    message = $"{c.displayName} пытался отдохнуть, но не смог сосредоточиться — время потрачено впустую."
                };
            }
        }
    }

    public struct RestResult
    {
        public bool hopeSide;
        public int gainedStamina;
        public string message;
    }
}
