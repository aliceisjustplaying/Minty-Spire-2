using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace MintySpire2.combat;

internal sealed partial class DamageProjection
{
    public IReadOnlyList<Creature> GetProjectedHittableEnemies()
    {
        return Player.CombatState!.HittableEnemies.Where(IsProjectedAlive).ToList();
    }

    public Creature? GetRandomProjectedEnemy()
    {
        return combatTargetRng.NextItem(GetProjectedHittableEnemies());
    }

    public IReadOnlyList<CardModel> GetProjectedHandCards()
    {
        return projectedHandCards;
    }

    public void ClearProjectedHand()
    {
        projectedHandCards.Clear();
    }

    public void AutoPlayProjectedAttackCards(int amount)
    {
        for (var index = 0; index < amount; index++)
        {
            var attackCards = projectedHandCards
                .Where(card => card.Type == CardType.Attack && !card.Keywords.Contains(CardKeyword.Unplayable))
                .ToList();
            var selectedCard = handShuffleRng.NextItem(attackCards);
            if (selectedCard == null)
                return;

            projectedHandCards.Remove(selectedCard);
        }
    }


}
