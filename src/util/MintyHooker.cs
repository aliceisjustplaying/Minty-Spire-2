using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MintySpire2.relicreminders;
using MintySpire2.relicreminders.endturnbutton;

namespace MintySpire2.util;

public class MintyHooker : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;

    // Rest site render
    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        RestHPRender.CatchHPChange(creature);
        return Task.CompletedTask;
    }

    // End turn relics
    public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        if(LocalContext.IsMe(creature))
            EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterEnergySpent(CardModel card, int amount)
    {
        EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side == CombatSide.Player)
            EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    public override Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if(side == CombatSide.Player)
            EndTurnRelicReminderService.NotifyRemindersMayHaveChanged();
        return Task.CompletedTask;
    }

    // History Course
    public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        HistoryCourseTooltip.HistoryStartPulse(Wiz.p()?.GetRelic<HistoryCourse>(), cardPlay);
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        HistoryCourseTooltip.HistoryStopPulseOnCombatEnd(Wiz.p()?.GetRelic<HistoryCourse>());
        return Task.CompletedTask;
    }
}