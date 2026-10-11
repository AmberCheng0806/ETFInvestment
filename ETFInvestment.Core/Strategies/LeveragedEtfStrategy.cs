using ETFInvestment.Core.Models;
namespace ETFInvestment.Core.Strategies;

public static class LeveragedEtfStrategy
{
    public static RebalanceSignal Evaluate(RebalanceState state, decimal currentPrice, decimal upwardTrigger = 0.50m,
        decimal downwardTrigger = 0.50m)
    {
        if (state.Units < 0m || state.Cash < 0m || state.BasePrice <= 0m ||
            currentPrice <= 0m || upwardTrigger <= 0m || downwardTrigger <= 0m || downwardTrigger >= 1m)
            return new RebalanceSignal(RebalanceAction.AwaitingData, 0, 0, 0, 0, "Invalid portfolio, base price, or market price.");
        var stock = state.Units * currentPrice;
        if (state.Units == 0 || (currentPrice < state.BasePrice * (1 + upwardTrigger) &&
                                  currentPrice > state.BasePrice * (1 - downwardTrigger)))
            return new RebalanceSignal(RebalanceAction.None, 0, state.Units, stock, state.Cash, "No price trigger.");
        var targetStock = (stock + state.Cash) / 2m;
        var delta = targetStock - stock; // positive => buy, negative => sell
        return new RebalanceSignal(delta > 0m ? RebalanceAction.Buy : delta < 0m ? RebalanceAction.Sell : RebalanceAction.None,
            Math.Abs(delta), targetStock / currentPrice, targetStock, targetStock,
            "Rebalance to 50:50; execute only after user confirmation. Excludes fees and integer-share rounding.");
    }
}
