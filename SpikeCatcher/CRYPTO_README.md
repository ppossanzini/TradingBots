# SpikeCatcher - Cryptocurrency Edition

## What's New (Crypto Adaptation)

SpikeCatcher ha been successfully adapted to support **24/7 cryptocurrency trading**. The bot now works with both traditional forex/CFD instruments AND crypto pairs like BTC/USD, ETH/USD, XRP/USD, and others.

## Key Changes

### 1. **New Parameters for Crypto Support**

Two new boolean parameters control crypto vs forex behavior:

- **`EnableTradingWindow`** (default: `true`)
  - Set to `FALSE` for 24/7 crypto trading
  - Disables the fixed trading window (e.g., 08:00-22:00)
  - For forex, keep `TRUE` to maintain hourly restrictions

- **`EnableFridayClose`** (default: `true`)
  - Set to `FALSE` for crypto (markets never close)
  - Set to `TRUE` for forex (Friday liquidation enabled)
  - When disabled, `FridayCloseTime` is ignored

### 2. **Updated Configuration Files (.cbotset)**

#### Forex Configurations (unchanged behavior):
- `hft_eurusd_m1.cbotset` - EUR/USD with trading window + Friday close
- `hft_spotbrent_m1.cbotset` - Spot Brent with trading window + Friday close
- `hft_us500_m1.cbotset` - US500 with trading window + Friday close

#### Crypto Configurations (NEW):
Located in `/Configs` folder:
- `crypto_btcusd_m1.cbotset` - Bitcoin (High volatility, aggressive)
- `crypto_ethusd_m1.cbotset` - Ethereum (Medium-high volatility)
- `crypto_xrpusd_m1.cbotset` - Ripple (Medium volatility, conservative)

### 3. **Crypto-Optimized Parameters**

The crypto configurations include:
- Shorter ATR period (12-14 vs forex's 14) for faster volatility detection
- Higher `MinAtrFilterPips` (5-8) to filter out noise in 24/7 market
- Shorter `CooldownBars` (2-4 min vs 1-3) to catch frequent spikes
- Tighter position distances and take profits optimized for crypto speed
- Lower position size limits (2-3 positions) to manage 24/7 risk

## How to Use for Crypto Trading

### Step 1: Configure Parameters
When creating a new robot instance in cTrader:
```
Enable Trading Window: FALSE
Enable Friday Close: FALSE
```

### Step 2: Load Crypto Configuration
Load one of the preset crypto `.cbotset` files from `/Configs`:
- Small account? Start with `crypto_ethusd_m1.cbotset` (medium volatility)
- Experienced? Try `crypto_btcusd_m1.cbotset` (high volatility)
- Conservative? Use `crypto_xrpusd_m1.cbotset` (stable price action)

### Step 3: Backtest
1. Select the crypto symbol (e.g., BTC/USD)
2. Run 2-4 week backtest in cTrader
3. Adjust parameters based on results
4. Paper trade for 1 week before live

## File Structure

```
SpikeCatcher/
├── SpikeCatcher.cs              (Main bot logic - now with crypto support)
├── SpikeCatcher.csproj          (Project file)
├── hft_eurusd_m1.cbotset        (Forex: EUR/USD)
├── hft_spotbrent_m1.cbotset     (Commodity: Brent)
├── hft_us500_m1.cbotset         (Index: US500)
├── AGENTS.md                    (Updated with crypto details)
├── CRYPTO_GUIDE.md              (Detailed crypto trading guide)
└── Configs/
    ├── crypto_btcusd_m1.cbotset (Crypto: BTC/USD)
    ├── crypto_ethusd_m1.cbotset (Crypto: ETH/USD)
    └── crypto_xrpusd_m1.cbotset (Crypto: XRP/USD)
```

## Recommended Crypto Symbols

All require cTrader broker support:
- **BTC/USD** - Bitcoin (most liquid, high volatility)
- **ETH/USD** - Ethereum (liquid, medium-high volatility)
- **XRP/USD** - Ripple (stable, medium volatility)
- **SOL/USD** - Solana (volatile, requires tuning)
- **ADA/USD** - Cardano (stable, conservative)
- **DOGE/USD** - Dogecoin (highly volatile, experienced traders only)

## Build & Deploy

### Build Commands
```powershell
# Debug
dotnet build SpikeCatcher.sln -c Debug

# Release
dotnet build SpikeCatcher.sln -c Release
```

### Output Files
- `SpikeCatcher/bin/Release/net6.0/SpikeCatcher.algo` - Deploy to cTrader
- `SpikeCatcher/bin/Release/net6.0/SpikeCatcher.algo.metadata` - Metadata file

## Backwards Compatibility

✅ **All existing forex configurations work unchanged.** The crypto parameters default to `true`, so:
- Existing `.cbotset` files continue to work for forex
- New crypto traders load the `/Configs` presets and set `EnableTradingWindow=FALSE`
- Single bot instance can trade either forex or crypto by switching `.cbotset`

## Testing Checklist

- [ ] Build succeeds with no errors
- [ ] Backtest with `crypto_btcusd_m1.cbotset` on 2 weeks of data
- [ ] Win rate > 45% on backtest
- [ ] Average trade profit > 2x risk
- [ ] Max drawdown < 10% of account
- [ ] Paper trade on demo for 1 week
- [ ] Monitor spread and slippage on live account (if applicable)

## Additional Resources

See also:
- `AGENTS.md` - Architecture and critical patterns
- `CRYPTO_GUIDE.md` - Detailed crypto parameter profiles and workflows
- cTrader documentation - Symbol availability and CFD specifications

---

**Version**: 1.1  
**Date**: April 2026  
**Change**: Added full cryptocurrency support with 24/7 trading capability

