# AGENTS.md - SpikeCatcher cAlgo Bot Guide

## Project Overview
**SpikeCatcher** is a C# cAlgo algorithmic trading bot for the cTrader platform. It trades **forex/CFD instruments AND cryptocurrencies** (EUR/USD, Spot Brent, US500, BTC/USD, ETH/USD, etc.) on 1-minute timeframes by detecting price spikes at support/resistance levels and placing stop-loss pending orders with trailing stop management.

**Ecosystem**: .NET 6.0 C# bot for cTrader (cAlgo.API), single-file monolithic design.

**Supported Asset Classes**:
- **Forex**: EUR/USD, GBP/USD, etc.
- **Commodities**: Spot Brent, Gold, etc.
- **Indices**: US500, DAX, etc.
- **Cryptocurrencies**: BTC/USD, ETH/USD, XRP/USD, SOL/USD, and other crypto pairs available on cTrader

---

## Architecture & Key Components

### Core Trading Logic Flow
1. **Initialization (`OnStart`)**: Loads ATR indicator, records last bar time, updates pending orders
2. **Bar Loop (`OnBar`)**: Checks Friday close-time → updates pending orders → manages trailing stops
3. **Order Management**: Cancelable pending stop orders placed above/below price thresholds (no auto-closes)
4. **Position Lifecycle**: Manual opening via pending orders → break-even + trailing protection → manual close or Friday liquidation

### Critical Patterns

#### Position Filtering Pattern
The bot filters positions by `PositionPrefix` label and `SymbolName`:
```csharp
private Position[] LongPositions => Positions
    .Where(p => p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName && p.TradeType == TradeType.Buy)
    .ToArray();
```
**Important**: All position queries MUST check both label prefix AND symbol to avoid affecting other robots or accounts. The `PositionPrefix` parameter defaults to "Spike Catcher" but is configurable per instance.

#### Dynamic Volume Calculation
Volume scales with free margin percentage divided by `MarginPercentDivider`:
```csharp
normalized = (Account.FreeMargin / Account.Equity) / MarginPercentDivider;  // Clamped 0-1
lots = MinVolumeLots + (MaxVolumeLots - MinVolumeLots) * normalized;
```
After calculation, volume MUST be normalized with `Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down)`.

#### Offset Calculation Modes
- **Fixed Mode (`UseDynamicOffset=false`)**: Uses constant `FixedOffsetPips` offset from current Ask/Bid
- **Dynamic Mode (`UseDynamicOffset=true`)**: Scales offset from ATR * `AtrMultiplier`, clamped between `MinDynamicOffsetPips` and `MaxDynamicOffsetPips`

#### Spike Confirmation Logic
Triple validation in `HasValidSpikeConfirmation()`:
1. ATR filter: Candle volatility must exceed `MinAtrFilterPips`
2. Body ratio: Candle body % must exceed `MinCandleBodyPercent` (default 55%)
3. Breakout confirmation: Close must break threshold by `BreakoutConfirmationPips`

---

## Build & Deployment Workflow

### Build Commands (PowerShell)
```powershell
# Build Debug
dotnet build SpikeCatcher.sln -c Debug

# Build Release
dotnet build SpikeCatcher.sln -c Release

# Output DLL location
# Debug: SpikeCatcher/bin/Debug/net6.0/SpikeCatcher.dll
# Release: SpikeCatcher/bin/Release/net6.0/SpikeCatcher.dll
```

### Deployment for cTrader
The `.algo` file (compiled bot) is auto-generated in `bin/Debug` and `bin/Release` directories. Upload `SpikeCatcher.algo` + `SpikeCatcher.algo.metadata` to cTrader platform.

### Configuration Files
Three `.cbotset` files contain pre-saved parameter configurations per instrument:
- `hft_eurusd_m1.cbotset` - EUR/USD 1min settings
- `hft_spotbrent_m1.cbotset` - Spot Brent 1min settings  
- `hft_us500_m1.cbotset` - US500 1min settings

These are cTrader-specific JSON files—load via cTrader UI to apply settings or inspect to understand parameter tuning history.

---

## Parameters & Configuration Groups

### Trading Window (Time-based)
- `TradingStartTime`, `TradingEndTime`: HH:MM format; wraps across midnight if start > end
- `EnableTradingWindow`: Disable for 24/7 trading (required for cryptocurrencies)
- `EnableFridayClose`: Disable for cryptocurrencies (crypto markets don't close on Friday)
- `FridayCloseTime`: Only used if `EnableFridayClose` is true

### Orders Group (Position Sizing & Triggering)
- `OrderDirection`: `Both`, `LongOnly`, or `ShortOnly` enum
- `Min/MaxVolumeLots`: Position size range; scales dynamically with margin
- `MarginPercentDivider`: Risk factor (10.0 = 1/10th of free margin)
- `MaxLongPositions`, `MaxShortPositions`: Max concurrent positions per direction
- `EvaluatingTimeFrame`, `EvaluationgCandles` (sic), `EvaluatingRange`: Multi-timeframe threshold calculation
  - Evaluates higher timeframe (default 1H) price range, calculates entry thresholds at `EvaluatingRange%` from boundaries

### Triggering Offsets Group
- `MaxSpreadPips`: Cancels order placement if spread exceeds this (market too volatile)
- `FixedOffsetPips` vs `UseDynamicOffset`: Two offset modes (see Architecture section)
- `AtrPeriod`, `AtrMultiplier`: ATR calculation and scaling for dynamic offset
- `DistanceBetweenPositionsInPips`: Minimum distance between new orders and nearest existing position
- `MinAtrFilterPips`, `BreakoutConfirmationPips`, `MinCandleBodyPercent`: Spike validation thresholds
- `CooldownBars`: Minutes between entry attempts (prevents order spam)

### Take Profit Group
- `TakeProfitPips`: Fixed profit target for all positions
- `TrailingTriggerPips`, `TrailingDistancePips`: Activates trailing stop after `TrailingTriggerPips` profit
- `StopLossPips`: Fixed initial stop loss (can be 0 for manual management)
- `BrEvenTriggerPips`, `BrEvenDistancePips`: Break-even activation when `BrEvenTriggerPips` profit reached

---

## Key Developer Workflows

### Debugging Techniques
1. **Print() statements**: Uses cAlgo's `Print()` method to log to bot journal. Examples in code:
   - `Print("Fuori fascia oraria di trading, nessun nuovo ordine piazzato.");` (Italian comments indicate trading window logic)
   - Spike confirmation debug prints at "Rannge 1" and "Rannge 2" (note typo: "Rannge")

2. **Backtesting**: Use cTrader's built-in backtester with `.cbotset` files to validate parameter changes

3. **Live Testing**: Deploy to a practice account using the generated `.algo` file

### Common Modifications
- **Adjust thresholds**: Edit parameter defaults or use `.cbotset` files without recompiling
- **Add indicators**: Instantiate in `OnStart()` (like ATR); example: `_atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple)`
- **Change position logic**: Modify `LongPositions`/`ShortPositions` filters or `UpdatePendingOrders()` logic
- **Add new bars management**: Extend `OnBar()` or add `OnTick()` for sub-bar logic

---

## Project-Specific Conventions

### Italian Localization
Code contains Italian UI strings and comments (e.g., "Posizione aperta", "Fuori fascia oraria"). Keep comments bilingual (Italian UI strings, English code logic) for consistency.

### Label-Based Tracking
Instead of tracking position ID arrays, the bot uses `Label` string prefixes:
- Format: `"{PositionPrefix}-{Label}"` e.g., `"Spike Catcher-SpikeCatcher"`
- Always filter by both label AND symbol when querying positions

### Time Handling
Uses `Server.Time` (UTC, enforced by `[Robot(TimeZone = TimeZones.UTC)]`) and `TimeSpan.Parse()` for HH:MM strings. Midnight wrap-around is manually checked (line 331-334).

### Pending Order Strategy
Bot places **pending stop orders** (not market orders):
- Buy stops at `Symbol.Ask + offset * PipSize` (triggered by upward price)
- Sell stops at `Symbol.Bid - offset * PipSize` (triggered by downward price)
- Orders are **regenerated every bar** (old orders cancelled, new ones placed) based on current logic

---

## Integration Points & Dependencies

### cAlgo.API Dependencies
- **Core**: `Robot` base class, `Position`, `PendingOrder`, `Symbol`, `Account`
- **Data**: `Bars` (candlesticks), `MarketData` (multi-timeframe access)
- **Indicators**: `AverageTrueRange` (ATR)
- **Actions**: `PlaceStopOrder()`, `ModifyPosition()`, `ClosePosition()`

### Symbol-Specific Behavior
- `Symbol.PipSize`: 0.0001 for EUR/USD, requires pips→price conversion (multiply by `Symbol.PipSize`)
- `Symbol.PipValue`: Used for PnL calculations
- `Symbol.Ask`, `Symbol.Bid`: Current market prices for offset calculations

### Multi-Timeframe Logic
Bot evaluates spikes on 1-minute bars but checks support/resistance on configurable higher timeframe (`EvaluatingTimeFrame`):
```csharp
var logtimebasrs = MarketData.GetBars(EvaluatingTimeFrame, SymbolName);  // Must specify symbol
var maxPrice = logtimebasrs.TakeLast(EvaluationgCandles).Max(i => Math.Max(i.Close, i.Open));
```
**Critical**: `GetBars()` requires explicit symbol name for cross-symbol queries.

---

## Known Issues & Quirks

1. **Typo in parameter**: `EvaluationgCandles` (should be `EvaluatingCandles`)—DO NOT FIX without updating all references
2. **Commented code**: `CloseFridayPositions()` body is commented out (line 350-351); only prints message
3. **Debug prints**: "Rannge 1" and "Rannge 2" typos in spike validation (lines 192, 201)
4. **Italian comments**: Preserve localization; indicates code was developed by Italian developer
5. **Disabled position management**: `ManageOpenPositions()` call is commented out (line 179)—logic not implemented

---

## Cryptocurrency-Specific Configuration

### For 24/7 Crypto Trading
Set these parameters:
- `EnableTradingWindow`: `false` (crypto markets never close)
- `EnableFridayClose`: `false` (no Friday close in crypto)
- `OrderDirection`: Typically `Both` (crypto volatility favors bidirectional trading)

### Recommended Crypto Parameters (Starting Point)
**For volatile cryptos (BTC, ETH):**
- `AtrPeriod`: 10-14 (higher volatility requires shorter ATR window)
- `MinAtrFilterPips`: 5-10 (crypto pip values differ; test for your symbol)
- `AtrMultiplier`: 0.8-1.2 (adjust for crypto volatility profile)
- `DistanceBetweenPositionsInPips`: 30-50 (tighter for faster-moving crypto)
- `CooldownBars`: 2-5 (crypto can have multiple spike opportunities)
- `TrailingDistancePips`: 5-10 (crypto moves faster than forex)

**For stable cryptos (USDT, USDC):**
- Use conservative parameters similar to EUR/USD
- May require higher `MinAtrFilterPips` threshold

### Testing Crypto Symbols
Recommended symbols to test on cTrader:
- `BTC/USD` - High volatility, large spikes
- `ETH/USD` - Medium-high volatility
- `XRP/USD`, `SOL/USD` - Lower volatility alternatives
- `ADA/USD`, `DOGE/USD` - Speculative/volatile

---

## Quick Reference: Essential Files
- **Main logic**: `SpikeCatcher/SpikeCatcher.cs` (417 lines, single file)
- **Project config**: `SpikeCatcher/SpikeCatcher.csproj`
- **Solution**: `SpikeCatcher.sln`
- **Build output**: `SpikeCatcher/bin/Debug` or `Release/net6.0/SpikeCatcher.dll`
- **Parameter configs**: `*.cbotset` files (cTrader JSON format)

