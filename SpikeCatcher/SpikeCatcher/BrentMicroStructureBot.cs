using cAlgo.API;
using cAlgo.API.Internals;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace cAlgo.Robots
{
  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class BrentMicroStructureBot : Robot
  {
    public enum DirectionBias
    {
      Both,
      LongOnly,
      ShortOnly
    }

    [Parameter("Position Prefix", Group = "General", DefaultValue = "BrentMicro")]
    public string PositionPrefix { get; set; }

    [Parameter("Direction", Group = "General", DefaultValue = DirectionBias.Both)]
    public DirectionBias Direction { get; set; }

    [Parameter("Use Trading Window", Group = "General", DefaultValue = true)]
    public bool UseTradingWindow { get; set; }

    [Parameter("Start Time (HH:mm)", Group = "General", DefaultValue = "07:00")]
    public string StartTime { get; set; }

    [Parameter("End Time (HH:mm)", Group = "General", DefaultValue = "21:30")]
    public string EndTime { get; set; }

    [Parameter("Max Open Positions", Group = "Risk", DefaultValue = 1, MinValue = 1, Step = 1)]
    public int MaxOpenPositions { get; set; }

    [Parameter("Lots", Group = "Risk", DefaultValue = 0.02, MinValue = 0.01, Step = 0.01)]
    public double Lots { get; set; }

    [Parameter("Stop Loss (pips)", Group = "Risk", DefaultValue = 120, MinValue = 0)]
    public double StopLossPips { get; set; }

    [Parameter("Take Profit (pips)", Group = "Risk", DefaultValue = 220, MinValue = 0)]
    public double TakeProfitPips { get; set; }

    [Parameter("Max Spread (pips)", Group = "Filters", DefaultValue = 7.0, MinValue = 0.1)]
    public double MaxSpreadPips { get; set; }

    [Parameter("Min Ticks In 60s", Group = "Filters", DefaultValue = 35, MinValue = 10, Step = 1)]
    public int MinTicks60 { get; set; }

    [Parameter("Min Tick-Rate Acceleration", Group = "Filters", DefaultValue = 1.15, MinValue = 0.5)]
    public double MinTickRateAcceleration { get; set; }

    [Parameter("Min Range 60s (pips)", Group = "Filters", DefaultValue = 35, MinValue = 1)]
    public double MinRange60Pips { get; set; }

    [Parameter("Range Expansion Factor", Group = "Filters", DefaultValue = 1.30, MinValue = 0.5)]
    public double MinRangeExpansionFactor { get; set; }

    [Parameter("Min Volatility 60s (pips)", Group = "Filters", DefaultValue = 2.5, MinValue = 0.1)]
    public double MinVolatility60Pips { get; set; }

    [Parameter("Volatility Expansion Factor", Group = "Filters", DefaultValue = 1.15, MinValue = 0.5)]
    public double MinVolatilityExpansionFactor { get; set; }

    [Parameter("Long Imbalance 10s", Group = "Entry", DefaultValue = 0.20, MinValue = -1.0, MaxValue = 1.0)]
    public double LongImbalance10 { get; set; }

    [Parameter("Short Imbalance 10s", Group = "Entry", DefaultValue = -0.20, MinValue = -1.0, MaxValue = 1.0)]
    public double ShortImbalance10 { get; set; }

    [Parameter("Min Directional Run 10s", Group = "Entry", DefaultValue = 3, MinValue = 1, Step = 1)]
    public int MinDirectionalRun10 { get; set; }

    [Parameter("Long Drift 30s (pips)", Group = "Entry", DefaultValue = 6, MinValue = -50, MaxValue = 50)]
    public double LongDrift30Pips { get; set; }

    [Parameter("Short Drift 30s (pips)", Group = "Entry", DefaultValue = -6, MinValue = -50, MaxValue = 50)]
    public double ShortDrift30Pips { get; set; }

    [Parameter("Use Mean-Reversion Kick", Group = "Entry", DefaultValue = true)]
    public bool UseMeanReversionKick { get; set; }

    [Parameter("Reversion Dip 60s (pips)", Group = "Entry", DefaultValue = 10, MinValue = 0)]
    public double ReversionDip60Pips { get; set; }

    [Parameter("Slope Recovery 10s (pips)", Group = "Entry", DefaultValue = 2, MinValue = 0)]
    public double SlopeRecovery10Pips { get; set; }

    [Parameter("Time Stop (sec)", Group = "Exit", DefaultValue = 300, MinValue = 30, Step = 5)]
    public int TimeStopSeconds { get; set; }

    [Parameter("Min Profit At Time Stop (pips)", Group = "Exit", DefaultValue = 6, MinValue = -50)]
    public double MinProfitAtTimeStopPips { get; set; }

    [Parameter("Min MFE For Retracement Exit (pips)", Group = "Exit", DefaultValue = 18, MinValue = 1)]
    public double MinMfeForRetracementExitPips { get; set; }

    [Parameter("Retracement Exit % of MFE", Group = "Exit", DefaultValue = 0.40, MinValue = 0.05, MaxValue = 0.95)]
    public double RetracementExitPct { get; set; }

    [Parameter("Exit Drift 30s (pips)", Group = "Exit", DefaultValue = 5, MinValue = 0)]
    public double ExitDrift30Pips { get; set; }

    [Parameter("Exit Imbalance 10s", Group = "Exit", DefaultValue = 0.15, MinValue = 0.01, MaxValue = 1.0)]
    public double ExitImbalance10 { get; set; }

    [Parameter("Cooldown After Exit (sec)", Group = "Exit", DefaultValue = 20, MinValue = 0, Step = 1)]
    public int CooldownAfterExitSeconds { get; set; }

    private readonly List<TickSample> _ticks = new();
    private readonly Dictionary<int, PositionRuntime> _positionState = new();

    private const int MaxHistorySeconds = 720;

    private TimeSpan _startWindow;
    private TimeSpan _endWindow;
    private DateTime _lastExitTime;

    protected override void OnStart()
    {
      _lastExitTime = DateTime.MinValue;

      if (!TryParseTime(StartTime, out _startWindow))
        _startWindow = new TimeSpan(7, 0, 0);

      if (!TryParseTime(EndTime, out _endWindow))
        _endWindow = new TimeSpan(21, 30, 0);
    }

    protected override void OnTick()
    {
      CaptureTick();
      CleanupTicks();
      ManagePositions();
      TryEnter();
    }

    private void CaptureTick()
    {
      var now = Server.Time;
      var bid = Symbol.Bid;
      var ask = Symbol.Ask;
      var mid = (ask + bid) * 0.5;
      var spread = ask - bid;

      _ticks.Add(new TickSample(now, mid, spread));
    }

    private void CleanupTicks()
    {
      if (_ticks.Count == 0)
        return;

      var threshold = Server.Time.AddSeconds(-MaxHistorySeconds);
      var idx = _ticks.FindIndex(t => t.Time >= threshold);

      if (idx <= 0)
        return;

      _ticks.RemoveRange(0, idx);
    }

    private void TryEnter()
    {
      if (!IsTradingWindow())
        return;

      if ((Server.Time - _lastExitTime).TotalSeconds < CooldownAfterExitSeconds)
        return;

      var openPositions = GetManagedPositions();
      if (openPositions.Length >= MaxOpenPositions)
        return;

      if (_ticks.Count < 100)
        return;

      var spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
      if (spreadPips > MaxSpreadPips)
        return;

      var f10 = BuildFeatureWindow(10);
      var f30 = BuildFeatureWindow(30);
      var f60 = BuildFeatureWindow(60);
      var f300 = BuildFeatureWindow(300);

      if (!f10.IsValid || !f30.IsValid || !f60.IsValid || !f300.IsValid)
        return;

      if (f60.TickCount < MinTicks60)
        return;

      if (f300.TickRate <= 0)
        return;

      var tickRateAccel = f60.TickRate / f300.TickRate;
      if (tickRateAccel < MinTickRateAcceleration)
        return;

      var range60Pips = f60.Range / Symbol.PipSize;
      var range300Pips = f300.Range / Symbol.PipSize;

      if (range60Pips < MinRange60Pips)
        return;

      if (range300Pips > 0 && (range60Pips / range300Pips) < MinRangeExpansionFactor)
        return;

      var vol60Pips = f60.Volatility / Symbol.PipSize;
      var vol300Pips = f300.Volatility / Symbol.PipSize;

      if (vol60Pips < MinVolatility60Pips)
        return;

      if (vol300Pips > 0 && (vol60Pips / vol300Pips) < MinVolatilityExpansionFactor)
        return;

      var longSignal = CanLong(f10, f30, f60);
      var shortSignal = CanShort(f10, f30, f60);

      if (longSignal && Direction != DirectionBias.ShortOnly)
      {
        if (!HasOpenPosition(TradeType.Buy))
          OpenPosition(TradeType.Buy);
      }

      if (shortSignal && Direction != DirectionBias.LongOnly)
      {
        if (!HasOpenPosition(TradeType.Sell))
          OpenPosition(TradeType.Sell);
      }
    }

    private bool CanLong(FeatureWindow f10, FeatureWindow f30, FeatureWindow f60)
    {
      var directional = f10.Imbalance >= LongImbalance10 && f10.RunUpMax >= MinDirectionalRun10 &&
                        (f30.Drift / Symbol.PipSize) >= LongDrift30Pips;

      if (directional)
        return true;

      if (!UseMeanReversionKick)
        return false;

      var meanReversionKick = (f60.Drift / Symbol.PipSize) <= -ReversionDip60Pips &&
                             (f10.Drift / Symbol.PipSize) >= SlopeRecovery10Pips &&
                             f10.Imbalance > 0;

      return meanReversionKick;
    }

    private bool CanShort(FeatureWindow f10, FeatureWindow f30, FeatureWindow f60)
    {
      var directional = f10.Imbalance <= ShortImbalance10 && f10.RunDownMax >= MinDirectionalRun10 &&
                        (f30.Drift / Symbol.PipSize) <= ShortDrift30Pips;

      if (directional)
        return true;

      if (!UseMeanReversionKick)
        return false;

      var meanReversionKick = (f60.Drift / Symbol.PipSize) >= ReversionDip60Pips &&
                             (f10.Drift / Symbol.PipSize) <= -SlopeRecovery10Pips &&
                             f10.Imbalance < 0;

      return meanReversionKick;
    }

    private void OpenPosition(TradeType tradeType)
    {
      var volume = Symbol.QuantityToVolumeInUnits(Lots);
      volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

      if (volume <= 0)
      {
        Print("Invalid volume computed from lots={0}", Lots);
        return;
      }

      var label = BuildPositionLabel(tradeType);
      var sl = StopLossPips <= 0 ? (double?)null : StopLossPips;
      var tp = TakeProfitPips <= 0 ? (double?)null : TakeProfitPips;

      var result = ExecuteMarketOrder(tradeType, SymbolName, volume, label, sl, tp);
      if (result == null || !result.IsSuccessful || result.Position == null)
      {
        Print("Order failed {0}: {1}", tradeType, result?.Error);
        return;
      }

      _positionState[result.Position.Id] = new PositionRuntime
      {
        EntryTime = Server.Time,
        MaxFavorablePips = Math.Max(0, result.Position.Pips)
      };
    }

    private void ManagePositions()
    {
      var managed = GetManagedPositions();
      if (managed.Length == 0)
        return;

      var f10 = BuildFeatureWindow(10);
      var f30 = BuildFeatureWindow(30);

      foreach (var position in managed)
      {
        if (!_positionState.TryGetValue(position.Id, out var state))
        {
          state = new PositionRuntime
          {
            EntryTime = position.EntryTime,
            MaxFavorablePips = Math.Max(0, position.Pips)
          };
          _positionState[position.Id] = state;
        }

        state.MaxFavorablePips = Math.Max(state.MaxFavorablePips, Math.Max(0, position.Pips));

        var ageSec = (Server.Time - state.EntryTime).TotalSeconds;
        if (ageSec >= TimeStopSeconds && position.Pips < MinProfitAtTimeStopPips)
        {
          CloseManagedPosition(position, "TimeStop");
          continue;
        }

        if (state.MaxFavorablePips >= MinMfeForRetracementExitPips)
        {
          var drawdownFromMfe = state.MaxFavorablePips - position.Pips;
          if (drawdownFromMfe >= state.MaxFavorablePips * RetracementExitPct)
          {
            CloseManagedPosition(position, "MFERetracement");
            continue;
          }
        }

        if (!f10.IsValid || !f30.IsValid)
          continue;

        var drift30Pips = f30.Drift / Symbol.PipSize;

        if (position.TradeType == TradeType.Buy)
        {
          var reversal = drift30Pips <= -ExitDrift30Pips && f10.Imbalance <= -ExitImbalance10;
          if (reversal)
            CloseManagedPosition(position, "MomentumReversalLong");
        }
        else
        {
          var reversal = drift30Pips >= ExitDrift30Pips && f10.Imbalance >= ExitImbalance10;
          if (reversal)
            CloseManagedPosition(position, "MomentumReversalShort");
        }
      }

      CleanupClosedPositionStates();
    }

    private void CloseManagedPosition(Position position, string reason)
    {
      var result = ClosePosition(position);
      if (result != null && result.IsSuccessful)
      {
        _lastExitTime = Server.Time;
        _positionState.Remove(position.Id);
        Print("Closed #{0} {1} due to {2}. Pips={3:F1}", position.Id, position.TradeType, reason, position.Pips);
      }
    }

    private void CleanupClosedPositionStates()
    {
      if (_positionState.Count == 0)
        return;

      var openIds = GetManagedPositions().Select(p => p.Id).ToHashSet();
      var staleIds = _positionState.Keys.Where(id => !openIds.Contains(id)).ToArray();

      foreach (var id in staleIds)
        _positionState.Remove(id);
    }

    private Position[] GetManagedPositions()
    {
      return Positions
        .Where(p => p.SymbolName == SymbolName && p.Label != null && p.Label.StartsWith(PositionPrefix, StringComparison.Ordinal))
        .ToArray();
    }

    private bool HasOpenPosition(TradeType tradeType)
    {
      return Positions.Any(p =>
        p.SymbolName == SymbolName &&
        p.TradeType == tradeType &&
        p.Label != null &&
        p.Label.StartsWith(PositionPrefix, StringComparison.Ordinal));
    }

    private string BuildPositionLabel(TradeType type)
    {
      var side = type == TradeType.Buy ? "L" : "S";
      return string.Format(CultureInfo.InvariantCulture, "{0}-{1}", PositionPrefix, side);
    }

    private bool IsTradingWindow()
    {
      if (!UseTradingWindow)
        return true;

      var now = Server.Time.TimeOfDay;
      if (_startWindow <= _endWindow)
        return now >= _startWindow && now <= _endWindow;

      return now >= _startWindow || now <= _endWindow;
    }

    private FeatureWindow BuildFeatureWindow(int seconds)
    {
      if (_ticks.Count < 2)
        return FeatureWindow.Invalid;

      var now = Server.Time;
      var start = now.AddSeconds(-seconds);
      var window = _ticks.Where(t => t.Time >= start).ToList();

      if (window.Count < 3)
        return FeatureWindow.Invalid;

      var tickCount = window.Count;
      var durationSec = Math.Max(1e-6, (window[^1].Time - window[0].Time).TotalSeconds);

      var firstMid = window[0].Mid;
      var lastMid = window[^1].Mid;
      var range = window.Max(t => t.Mid) - window.Min(t => t.Mid);

      var up = 0;
      var down = 0;
      var runUp = 0;
      var runDown = 0;
      var maxRunUp = 0;
      var maxRunDown = 0;

      var diffs = new List<double>(tickCount - 1);

      for (var i = 1; i < window.Count; i++)
      {
        var d = window[i].Mid - window[i - 1].Mid;
        diffs.Add(d);

        if (d > 0)
        {
          up++;
          runUp++;
          runDown = 0;
          if (runUp > maxRunUp)
            maxRunUp = runUp;
        }
        else if (d < 0)
        {
          down++;
          runDown++;
          runUp = 0;
          if (runDown > maxRunDown)
            maxRunDown = runDown;
        }
        else
        {
          runUp = 0;
          runDown = 0;
        }
      }

      var imbalance = (double)(up - down) / Math.Max(1, up + down);
      var spreadMean = window.Average(t => t.Spread);
      var spreadStd = Std(window.Select(t => t.Spread));
      var vol = Std(diffs);

      return new FeatureWindow(
        true,
        tickCount,
        tickCount / durationSec,
        imbalance,
        maxRunUp,
        maxRunDown,
        lastMid - firstMid,
        range,
        vol,
        spreadMean,
        spreadStd);
    }

    private static bool TryParseTime(string value, out TimeSpan result)
    {
      return TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out result) ||
             TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result);
    }

    private static double Std(IEnumerable<double> values)
    {
      var arr = values as double[] ?? values.ToArray();
      if (arr.Length < 2)
        return 0;

      var mean = arr.Average();
      var sum = 0.0;

      for (var i = 0; i < arr.Length; i++)
      {
        var d = arr[i] - mean;
        sum += d * d;
      }

      return Math.Sqrt(sum / (arr.Length - 1));
    }

    private sealed class PositionRuntime
    {
      public DateTime EntryTime { get; set; }
      public double MaxFavorablePips { get; set; }
    }

    private readonly struct TickSample
    {
      public TickSample(DateTime time, double mid, double spread)
      {
        Time = time;
        Mid = mid;
        Spread = spread;
      }

      public DateTime Time { get; }
      public double Mid { get; }
      public double Spread { get; }
    }

    private readonly struct FeatureWindow
    {
      public static readonly FeatureWindow Invalid = new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

      public FeatureWindow(
        bool isValid,
        int tickCount,
        double tickRate,
        double imbalance,
        int runUpMax,
        int runDownMax,
        double drift,
        double range,
        double volatility,
        double spreadMean,
        double spreadStd)
      {
        IsValid = isValid;
        TickCount = tickCount;
        TickRate = tickRate;
        Imbalance = imbalance;
        RunUpMax = runUpMax;
        RunDownMax = runDownMax;
        Drift = drift;
        Range = range;
        Volatility = volatility;
        SpreadMean = spreadMean;
        SpreadStd = spreadStd;
      }

      public bool IsValid { get; }
      public int TickCount { get; }
      public double TickRate { get; }
      public double Imbalance { get; }
      public int RunUpMax { get; }
      public int RunDownMax { get; }
      public double Drift { get; }
      public double Range { get; }
      public double Volatility { get; }
      public double SpreadMean { get; }
      public double SpreadStd { get; }
    }
  }
}
