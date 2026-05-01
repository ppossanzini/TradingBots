# SpikeCatcher Cryptocurrency Configuration Guide

## Quick Start for Crypto Trading

### Step 1: Enable Crypto Mode
When creating a new bot instance on cTrader, configure these parameters:

```
Enable Trading Window: FALSE
Enable Friday Close: FALSE
OrderDirection: Both (or LongOnly if preferred)
```

### Step 2: Symbol-Specific Setup
Select your crypto pair in cTrader (e.g., `BTC/USD`, `ETH/USD`) and follow the recommended parameters below.

### Step 3: Backtest & Optimize
Use cTrader's backtester with the `.cbotset` files provided, or create new ones:
- Run 2-4 week backtests to validate parameters
- Adjust `AtrMultiplier` and `MinAtrFilterPips` based on results
- Test with small position sizes first

---

## Crypto Parameter Profiles

### BTC/USD (High Volatility)
**Best for**: Momentum/spike trading, 24/7 market

```
Min Volume (Lots): 0.01
Max Volume (Lots): 0.05
Margin Percent Divider: 5.0  # Higher risk tolerance
Max Long Positions: 3
Max Short Positions: 2
ATR Period: 12
ATR Multiplier: 1.0
Min ATR Filter Pips: 8
Breakout Confirmation Pips: 2.0
Min Candle Body %: 60
Cooldown Bars: 2
Take Profit Pips: 300-500
Trailing Trigger Pips: 15
Trailing Distance Pips: 8
Distance Between Positions: 40
```

### ETH/USD (Medium-High Volatility)
**Best for**: Active spike catcher with moderate risk

```
Min Volume (Lots): 0.01
Max Volume (Lots): 0.10
Margin Percent Divider: 8.0
Max Long Positions: 3
Max Short Positions: 2
ATR Period: 14
ATR Multiplier: 0.9
Min ATR Filter Pips: 6
Breakout Confirmation Pips: 1.5
Min Candle Body %: 55
Cooldown Bars: 3
Take Profit Pips: 250-400
Trailing Trigger Pips: 12
Trailing Distance Pips: 6
Distance Between Positions: 50
```

### XRP/USD, SOL/USD (Medium Volatility)
**Best for**: Scalping with tighter stops

```
Min Volume (Lots): 0.01
Max Volume (Lots): 0.10
Margin Percent Divider: 10.0
Max Long Positions: 3
Max Short Positions: 1
ATR Period: 14
ATR Multiplier: 0.8
Min ATR Filter Pips: 5
Breakout Confirmation Pips: 1.0
Min Candle Body %: 55
Cooldown Bars: 4
Take Profit Pips: 200-300
Trailing Trigger Pips: 10
Trailing Distance Pips: 5
Distance Between Positions: 60
```

### DOGE/USD (High Volatility/Speculative)
**Best for**: Experienced traders only

```
Min Volume (Lots): 0.01
Max Volume (Lots): 0.05
Margin Percent Divider: 3.0  # Very aggressive
Max Long Positions: 2
Max Short Positions: 1
ATR Period: 10
ATR Multiplier: 1.2
Min ATR Filter Pips: 10
Breakout Confirmation Pips: 2.5
Min Candle Body %: 60
Cooldown Bars: 1
Take Profit Pips: 400-600
Trailing Trigger Pips: 20
Trailing Distance Pips: 10
Distance Between Positions: 30
```

---

## Trading Dynamics Differences: Crypto vs Forex

| Factor | Forex (EUR/USD) | Crypto (BTC/USD) |
|--------|-----------------|-----------------|
| **Market Hours** | London/US overlap busy | 24/7 constant activity |
| **Pip Size** | 0.0001 | Variable (depends on symbol) |
| **Volatility** | ~50-100 pips/hour | 200-500+ pips/hour |
| **Spike Duration** | Minutes to hours | Seconds to minutes |
| **News Impact** | Scheduled (economic calendar) | Continuous (news, social) |
| **Spread** | 0.5-2 pips normal | 0.5-5+ pips (varies) |
| **Best Strategy** | Trend following + support/resistance | Mean reversion + momentum |

---

## Critical Adjustments for Crypto

### 1. Reduce Pip Thresholds
Crypto moves faster. If parameters feel too conservative:
- Lower `BreakoutConfirmationPips` by 30-50%
- Reduce `DistanceBetweenPositionsInPips` by 20-30%
- Example: For BTC, use 40 pips instead of 50

### 2. Increase ATR Sensitivity
Crypto volatility is higher:
- Use `AtrPeriod: 10-12` instead of 14
- Scale `AtrMultiplier` to 0.8-1.2 range (test)
- Monitor `MinAtrFilterPips` — may need 6-10 instead of 4

### 3. Shorten Cooldown
Crypto spikes happen more frequently:
- Set `CooldownBars: 2-4` (vs forex's 3-5)
- This allows catching multiple spike opportunities per hour

### 4. Tighter Take Profit
Crypto can reverse quickly:
- Use `TakeProfitPips: 250-500` (vs forex's 500+)
- Lower `TrailingTriggerPips: 10-15` to lock in quicker profits

### 5. Position Limits
More frequent trades in crypto:
- `MaxLongPositions: 2-3`
- `MaxShortPositions: 1-2`
- Prevents over-leverage during high-volume periods

---

## Risk Management for Crypto

⚠️ **Crypto is 3-5x more volatile than forex. Always:**

1. **Start with minimum position sizes** (`MinVolumeLots: 0.01`)
2. **Use tight stop losses** (no zero `StopLossPips`)
3. **Backtest extensively** (minimum 4 weeks of data)
4. **Monitor spread** (`MaxSpreadPips: 3-5` to avoid slippage)
5. **Have an emergency stop** - manually close if losses exceed 5-10% of account

---

## Backtesting Workflow for Crypto

1. **Select Symbol**: BTC/USD, ETH/USD, etc.
2. **Set Timeframe**: 1-minute (as designed)
3. **Date Range**: Start with 2-4 weeks recent data
4. **Load `.cbotset`**: Use the profile matching your crypto's volatility
5. **Run Test**: Monitor win rate, avg trade profit, max drawdown
6. **Adjust**: If win rate < 45%, increase `MinAtrFilterPips` or `BreakoutConfirmationPips`
7. **Live Test**: Paper trade on demo account for 1 week before real trading

---

## Creating New `.cbotset` Files for Crypto

To save a custom configuration:
1. In cTrader, set all parameters as desired for your crypto
2. Click **Save Parameters** → Name it `crypto_SYMBOL_profile.cbotset`
3. Examples:
   - `crypto_btcusd_aggressive.cbotset`
   - `crypto_ethusd_conservative.cbotset`
   - `crypto_xrpusd_scalp.cbotset`

These files are stored in the `SpikeCatcher` folder and are reusable across instances.

---

## Troubleshooting Crypto Performance

| Problem | Cause | Solution |
|---------|-------|----------|
| Too many false signals | ATR threshold too low | Increase `MinAtrFilterPips` |
| Missing real spikes | Threshold too high | Decrease `BreakoutConfirmationPips` |
| Over-leveraged | Position sizes too large | Increase `MarginPercentDivider` |
| Spread eating profits | Market too wide | Increase `MaxSpreadPips` → don't trade |
| Trailing stop triggers too often | Distance too small | Increase `TrailingDistancePips` |
| No trades at all | Trading window blocked | Ensure `EnableTradingWindow: FALSE` |

---

## Recommended Next Steps

1. ✅ Set `EnableTradingWindow: FALSE` and `EnableFridayClose: FALSE`
2. ✅ Choose a crypto (start with BTC/USD or ETH/USD)
3. ✅ Load one of the parameter profiles above
4. ✅ Backtest for 2-4 weeks
5. ✅ Optimize based on results
6. ✅ Paper trade for 1 week
7. ✅ Go live with minimal position sizes

