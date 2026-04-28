# AI Agent Guide: SpikeCatcher

## Project Overview
SpikeCatcher is a **high-frequency trading bot** built for cTrader, a .NET-based forex and CFD trading platform. It detects price spikes and opens positions using technical analysis (ATR, breakout confirmation, candle body analysis). The bot runs as a `[Robot]` component in cTrader via the `cAlgo.API` framework.

**Key fact**: This is NOT a standalone executable—it's a trading algorithm plugin that executes within the cTrader platform (similar to an MQL4 EA for MT4).

## Architecture: Single-File, Parameter-Driven Design
- **Single component**: `SpikeCatcher.cs` contains the entire trading logic
- **No external services**: Uses only cTrader's broker data and order management APIs
- **Configuration**: All strategy parameters are exposed via `[Parameter]` attributes (UI inputs in cTrader)
- **Multiple profiles**: `.cbotset` files store different parameter configurations for different symbols/timeframes:
  - `hft_eurusd_m1.cbotset` - EUR/USD on 1-minute bars
  - `hft_spotbrent_m1.cbotset` - Brent crude oil
  - `hft_us500_m1.cbotset` - S&P 500 index

## Critical Data Flows

### Entry Logic (`UpdatePendingOrders()`)
1. **Spike detection**: Identifies moves outside the recent price range (last N candles)
2. **Confirmation filters**:
   - Candle body must be >55% of range (strong directional move)
   - ATR must exceed minimum volatility threshold (4 pips default)
   - Breakout must confirm by 1.5 pips beyond threshold
3. **Position spacing**: Prevents clustering—new positions must be 50 pips away from nearest existing position
4. **Stop order placement**: Sets pending buy/sell stops at offset price, waits for spike to trigger

### Exit Strategy (`ManageTrailing()`)
- **Break-even stop**: Once position profits 0.5+ pips, moves stop to entry price (+0.1 pip buffer)
- **Trailing stop**: Once position profits 20+ pips, trails 8 pips below market
- **Take profit**: Auto-closes at 500 pips profit (configurable per .cbotset)
- **No manual SL/TP**: Exit mgmt is entirely automated

### Position Management
- **Long/Short tracking**: Separate `LongPositions` and `ShortPositions` properties filter by `PositionPrefix` label
- **Max limits**: Can hold up to 3 long + 0 short by default (configurable)
- **Position cooldown**: Won't open new positions within 3 minutes of last entry (prevents overtrading)

## Developer Workflows

### Building
```bash
dotnet build SpikeCatcher.sln
# Outputs: SpikeCatcher/bin/Debug/net6.0/SpikeCatcher.dll
```

### Deploying to cTrader
1. Build the project
2. The DLL is automatically packaged into `.algo` format by cTrader SDK
3. Copy `SpikeCatcher.algo` to cTrader's bots folder (platform-specific)
4. Load in cTrader → select `.cbotset` profile → Run

### Testing Strategy Parameters
- **Use `.cbotset` files**: Don't edit code to test parameters—create new `.cbotset` profile with different values
- **Backtesting workflow**: Use cTrader's native backtester (GUI-based, not command-line)
- **Example**: To test stricter spike confirmation, adjust `MinCandleBodyPercent` in UI, save as new profile

## Code Patterns & Conventions

### Position Filtering
All positions are identified by two criteria:
- `Label.StartsWith(PositionPrefix)` - prefix matching (not exact equality)
- `SymbolName == SymbolName` - only current symbol to avoid cross-symbol issues

**Example** (line 135-138):
```csharp
p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName && p.TradeType == TradeType.Sell
```

### Indicator Access Pattern
Indicators are calculated once in `OnStart()` and accessed via `.Result.LastValue`:
```csharp
_atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);  // OnStart
var atrPips = _atr.Result.LastValue / Symbol.PipSize;  // OnBar
```

### Dynamic Offset Calculation
Offset pips (distance from current price for pending orders) can be:
- **Fixed**: Always 5 pips (default)
- **Dynamic**: Calculated from ATR × multiplier (1.0 default), clamped between 3-20 pips

### Timeframe-Aware Price Range
The bot evaluates price direction on a **different timeframe** than the entry bars:
```csharp
var logtimebasrs = MarketData.GetBars(EvaluatingTimeFrame, SymbolName);  // Typically 1-hour
var maxPriceInEvaluationRange = logtimebasrs.TakeLast(EvaluationgCandles).Max(i => Math.Max(i.Close, i.Open));
```
This creates a **trend filter** so spikes only trigger within significant moves.

### Internationalized Comments
Code contains Italian comments (heritage code):
- "Fuori fascia oraria di trading" = Outside trading window
- "Spread troppo alto" = Spread too high
- These should be preserved during refactoring for consistency

## Key Parameter Categories

| Group | Purpose | Example |
|-------|---------|---------|
| **Orders** | Position sizing & limits | MinVolumeLots, MaxLongPositions |
| **Triggering Offsets** | Spike detection sensitivity | MinCandleBodyPercent, BreakoutConfirmationPips |
| **Take Profit** | Exit management | TrailingTriggerPips, BrEvenTriggerPips |

## Common Modifications

### Adding a New Filter
1. Implement logic in `HasValidSpikeConfirmation()` (line 183)
2. Add a `[Parameter]` control property at the top
3. Update both `allowLong` and `allowShort` conditions in `UpdatePendingOrders()`

### Changing Exit Strategy
Edit `ManageTrailing()` (line 376) to modify break-even or trailing stop logic. Currently uses **hard-coded trigger values** from parameters—if changing the trigger thresholds, update the condition checks (lines 384, 396).

### Debugging Print Statements
Use `Print()` calls (not `Console.WriteLine`)—output goes to cTrader's journal. Search for "Rannge" (sic) at lines 192, 201 for debug examples.

## External Dependencies
- **cTrader.Automate NuGet**: cAlgo.API + broker connectivity (version: auto-fetch, see `.csproj`)
- **No other dependencies**: Pure .NET 6.0, no third-party libraries
- **Framework**: .NET 6.0 (hardcoded in `.csproj`)

## Gotchas
1. **`EvaluationgCandles` typo**: Parameter has deliberate typo in property name (line 60)—don't "fix" without checking UI bindings
2. **Pending order cancellation**: All previous pending orders are **purged each bar** (line 258)—by design, keeps only latest signal
3. **Friday close disabled**: Line 350-351 is commented out—position closing logic is stubbed, only `Print()` executes
4. **MinValue constraint**: All volume/pips parameters have `MinValue` to prevent invalid config (e.g., `MaxVolumeLots` can't be below `MinVolumeLots`)

