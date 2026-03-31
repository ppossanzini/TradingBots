using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace cAlgo.Robots
{
  public enum OrderDirectionMode
  {
    Both,
    LongOnly,
    ShortOnly
  }

  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class SpikeCatcher : Robot
  {
    [Parameter("Label", DefaultValue = "Spike Catcher")]
    public string Label { get; set; }

    [Parameter("Position Prefix", DefaultValue = "Spike Catcher")]
    public string PositionPrefix { get; set; }

    [Parameter("Trading Start Time", DefaultValue = "08:00")]
    public string TradingStartTime { get; set; }

    [Parameter("Trading End Time", DefaultValue = "22:00")]
    public string TradingEndTime { get; set; }

    [Parameter("Friday Close Time", DefaultValue = "20:00")]
    public string FridayCloseTime { get; set; }

    #region Market Sizing

    [Parameter("Order Direction", DefaultValue = OrderDirectionMode.Both, Group = "Orders")]
    public OrderDirectionMode OrderDirection { get; set; }

    [Parameter("Min Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01, Group = "Orders")]
    public double MinVolumeLots { get; set; }

    [Parameter("Max Volume (Lots)", DefaultValue = 0.10, MinValue = 0.01, Group = "Orders")]
    public double MaxVolumeLots { get; set; }

    [Parameter("Margin Percent Divider", DefaultValue = 10.0, MinValue = 0.1, Group = "Orders")]
    public double MarginPercentDivider { get; set; }

    [Parameter("Max active long positions", Group = "Orders", DefaultValue = 3, MinValue = 0,
      Step = 1)]
    public int MaxLongPositions { get; set; }

    [Parameter("Max active short positions", Group = "Orders", DefaultValue = 0, MinValue = 0,
      Step = 1)]
    public int MaxShortPositions { get; set; }

    [Parameter("Timeframe to evaluate price directions", DefaultValue = 500, MinValue = 1, Group = "Orders")]
    public TimeFrame EvaluatingTimeFrame { get; set; } = TimeFrame.Hour;

    [Parameter("Evaluationg Candles", DefaultValue = 500, MinValue = 1, Group = "Orders")]
    public int EvaluationgCandles { get; set; } = 10;

    [Parameter("Evaluating Range", DefaultValue = 20, MinValue = 1, Group = "Orders")]
    public int EvaluatingRange { get; set; } = 20;

    #endregion

    #region Triggering Offsets

    [Parameter("Max Spread Pips", DefaultValue = 2, MinValue = 0, Group = "Triggering Offsets")]
    public double MaxSpreadPips { get; set; }

    [Parameter("Fixed Offset Pips", DefaultValue = 5, MinValue = 1, Group = "Triggering Offsets")]
    public double FixedOffsetPips { get; set; }

    [Parameter("Use Dynamic Offset", DefaultValue = false, Group = "Triggering Offsets")]
    public bool UseDynamicOffset { get; set; }

    [Parameter("ATR Period", DefaultValue = 14, MinValue = 1, Group = "Triggering Offsets")]
    public int AtrPeriod { get; set; }

    [Parameter("ATR Multiplier", DefaultValue = 1.0, MinValue = 0.1, Group = "Triggering Offsets")]
    public double AtrMultiplier { get; set; }

    [Parameter("Min Dynamic Offset Pips", DefaultValue = 3, MinValue = 1, Group = "Triggering Offsets")]
    public double MinDynamicOffsetPips { get; set; }

    [Parameter("Max Dynamic Offset Pips", DefaultValue = 20, MinValue = 1, Group = "Triggering Offsets")]
    public double MaxDynamicOffsetPips { get; set; }

    [Parameter("Distance between positions (pips)", Group = "Triggering Offsets", MinValue = 0, DefaultValue = 50)]
    public double DistanceBetweenPositionsInPips { get; set; } = 50;

    [Parameter("Min ATR Filter Pips", DefaultValue = 4, MinValue = 0.1, Group = "Triggering Offsets")]
    public double MinAtrFilterPips { get; set; }

    [Parameter("Breakout Confirmation Pips", DefaultValue = 1.5, MinValue = 0.1, Group = "Triggering Offsets")]
    public double BreakoutConfirmationPips { get; set; }

    [Parameter("Min Candle Body %", DefaultValue = 55, MinValue = 1, MaxValue = 100, Group = "Triggering Offsets")]
    public double MinCandleBodyPercent { get; set; }

    [Parameter("Cooldown Bars", DefaultValue = 3, MinValue = 0, Group = "Triggering Offsets")]
    public int CooldownBars { get; set; }

    #endregion

    #region Take Profit

    [Parameter("Take Profit Pips", DefaultValue = 500, MinValue = 0, Group = "Take Profit")]
    public double TakeProfitPips { get; set; }

    [Parameter("Trailing Trigger Pips", DefaultValue = 20, MinValue = 1, Group = "Take Profit")]
    public double TrailingTriggerPips { get; set; }

    [Parameter("Trailing Distance Pips", DefaultValue = 8, MinValue = 1, Group = "Take Profit")]
    public double TrailingDistancePips { get; set; }

    [Parameter("StopLoss Distance Pips", DefaultValue = 8, MinValue = 0, Group = "Take Profit")]
    public double StopLossPips { get; set; }

    [Parameter("Break Even Trigger Pips", DefaultValue = 0.5, MinValue = 0, Group = "Take Profit")]
    public double BrEvenTriggerPips { get; set; }

    [Parameter("Break Event Distance Pips", DefaultValue = 0.1, MinValue = 0, Group = "Take Profit")]
    public double BrEvenDistancePips { get; set; }

    #endregion


    private Position[] ShortPositions
    {
      get
      {
        return Positions.Where(p =>
            p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName &&
            p.TradeType == TradeType.Sell)
          .ToArray();
      }
    }

    private Position[] LongPositions
    {
      get
      {
        return Positions.Where(p =>
            p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName &&
            p.TradeType == TradeType.Buy)
          .ToArray();
      }
    }


    private DateTime _lastBarTime;
    private DateTime _lastEntryBarTime;
    private AverageTrueRange _atr;

    protected override void OnStart()
    {
      _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);
      _lastBarTime = Bars.OpenTimes.LastValue;
      _lastEntryBarTime = DateTime.MinValue;

      UpdatePendingOrders();
    }

    protected override void OnBar()
    {
      if (IsFridayCloseTimeReached())
        CloseFridayPositions();

      if (Bars.OpenTimes.LastValue != _lastBarTime)
      {
        _lastBarTime = Bars.OpenTimes.LastValue;
        UpdatePendingOrders();
      }

      ManageTrailing();
      //ManageOpenPositions();
    }
    

    private bool HasValidSpikeConfirmation(TradeType direction, double thresholdPrice)
    {
      var lastClosed = Bars.Last(1);
      var body = Math.Abs(lastClosed.Close - lastClosed.Open);
      var range = lastClosed.High - lastClosed.Low;

      if (range <= 0)
        return false;

      Print("Rannge 1");
      var atrPips = _atr.Result.LastValue / Symbol.PipSize;
      if (atrPips < MinAtrFilterPips)
        return false;

      var bodyPercent = (body / range) * 100.0;
      if (bodyPercent < MinCandleBodyPercent)
        return false;

      Print("Rannge 2");
      var confirmation = BreakoutConfirmationPips * Symbol.PipSize;

      var result = direction switch
      {
        TradeType.Buy => lastClosed.Close > (thresholdPrice + confirmation),
        TradeType.Sell => lastClosed.Close < (thresholdPrice - confirmation),
        _ => false
      };

      return result;
    }

    private void UpdatePendingOrders()
    {
      if (!IsWithinTradingWindow())
      {
        Print("Fuori fascia oraria di trading, nessun nuovo ordine piazzato.");
        return;
      }

      if (_lastEntryBarTime != DateTime.MinValue && (Server.Time - _lastEntryBarTime).TotalMinutes < CooldownBars)
        return;

      var spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
      if (spreadPips > MaxSpreadPips)
      {
        Print("Spread troppo alto: {0:F2} pips", spreadPips);
        return;
      }

      var logtimebasrs = MarketData.GetBars(EvaluatingTimeFrame, SymbolName);
      
      var maxPriceInEvaluationRange = logtimebasrs.TakeLast(EvaluationgCandles).Max(i => Math.Max(i.Close, i.Open));
      var minPriceInEvaluationRange = logtimebasrs.TakeLast(EvaluationgCandles).Min(i => Math.Min(i.Close, i.Open));
      
      var deltarange = maxPriceInEvaluationRange - minPriceInEvaluationRange;
      var minTreshold = minPriceInEvaluationRange + (deltarange * EvaluatingRange / 100);
      var maxTreshold = maxPriceInEvaluationRange - (deltarange * EvaluatingRange / 100);

      var nearLongPosition = FindNearestPosition(TradeType.Buy);
      var nearShortPosition = FindNearestPosition(TradeType.Sell);

      var isLongPositionFarEnough = (LongPositions.Length < MaxLongPositions) &&
                                        (nearLongPosition == null || Math.Abs(nearLongPosition.Pips) >
                                          DistanceBetweenPositionsInPips) ;//&& Symbol.Ask < minTreshold;

      var isShortPositionFarEnough = (ShortPositions.Length < MaxShortPositions) &&
                                         (nearShortPosition == null ||
                                          Math.Abs(nearShortPosition.Pips) >
                                          DistanceBetweenPositionsInPips);// && Symbol.Bid > maxTreshold;

      var allowLong = HasValidSpikeConfirmation(TradeType.Buy, maxTreshold);
      var allowShort = HasValidSpikeConfirmation(TradeType.Sell, minTreshold);

      Print("Long: {0}, Short: {1}, AllowLong: {2}, AllowShort: {3}", isLongPositionFarEnough, isShortPositionFarEnough, allowLong, allowShort);
      
      foreach (var o in PendingOrders.Where(p => p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName))
        o.Cancel();

      var volumeInUnits = GetDynamicVolumeInUnits();
      var offsetPips = GetCurrentOffsetPips();

      var buyPrice = Symbol.Ask + offsetPips * Symbol.PipSize;
      var sellPrice = Symbol.Bid - offsetPips * Symbol.PipSize;

      switch (OrderDirection)
      {
        case OrderDirectionMode.Both when isLongPositionFarEnough && allowLong:
        case OrderDirectionMode.LongOnly when isLongPositionFarEnough && allowLong:
          PlaceStopOrder(TradeType.Buy, SymbolName, volumeInUnits, buyPrice, $"{PositionPrefix}-{Label}",
            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips);
          Print("Pending aggiornati. Volume={0} units, Offset={1:F1} pips, Direzione={2}, BuyStop={3}, SellStop={4}",
            volumeInUnits, offsetPips, OrderDirection, buyPrice, sellPrice);
          break;

        case OrderDirectionMode.Both when isShortPositionFarEnough && allowShort:
        case OrderDirectionMode.ShortOnly when isShortPositionFarEnough && allowShort:
          PlaceStopOrder(TradeType.Sell, SymbolName, volumeInUnits, sellPrice, $"{PositionPrefix}-{Label}",
            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips);
          Print("Pending aggiornati. Volume={0} units, Offset={1:F1} pips, Direzione={2}, BuyStop={3}, SellStop={4}",
            volumeInUnits, offsetPips, OrderDirection, buyPrice, sellPrice);
          break;
      }
      
    }

    protected override void OnPositionOpened(Position position)
    {
      if (position.SymbolName != SymbolName || !position.Label.StartsWith(PositionPrefix))
        return;

      _lastEntryBarTime = Server.Time;
      Print("Posizione aperta: {0} {1}. Pending lasciate attive.", position.TradeType, position.EntryPrice);
    }

    private Position FindNearestPosition(TradeType? operation)
    {
      var nearPosition = operation switch
      {
        TradeType.Buy => this.LongPositions.MinBy(p => Math.Abs(p.NetProfit / Symbol.PipValue)),
        TradeType.Sell => this.ShortPositions.MinBy(p => Math.Abs(p.NetProfit / Symbol.PipValue)),
        _ => null
      };
      return nearPosition;
    }

    private double GetDynamicVolumeInUnits()
    {
      var freeMarginPercent = Account.Equity > 0 ? (Account.FreeMargin / Account.Equity) * 100.0 : 0.0;

      var normalized = freeMarginPercent / MarginPercentDivider;
      if (normalized < 0)
        normalized = 0;

      if (normalized > 1)
        normalized = 1;

      var lots = MinVolumeLots + (MaxVolumeLots - MinVolumeLots) * normalized;
      var volumeInUnits = Symbol.QuantityToVolumeInUnits(lots);

      return Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.Down);
    }

    private bool IsWithinTradingWindow()
    {
      var now = Server.Time.TimeOfDay;
      var start = TimeSpan.Parse(TradingStartTime);
      var end = TimeSpan.Parse(TradingEndTime);

      if (start <= end)
        return now >= start && now < end;

      return now >= start || now < end;
    }

    private bool IsFridayCloseTimeReached()
    {
      if (Server.Time.DayOfWeek != DayOfWeek.Friday)
        return false;

      var now = Server.Time.TimeOfDay;
      var fridayClose = TimeSpan.Parse(FridayCloseTime);

      return now >= fridayClose;
    }

    private void CloseFridayPositions()
    {
      // foreach (var position in Positions.Where(p => p.SymbolName == SymbolName && p.Label == Label).ToArray())
      //     ClosePosition(position);

      Print("Venerdì: posizioni chiuse e pending cancellate.");
    }


    private double GetCurrentOffsetPips()
    {
      if (!UseDynamicOffset)
        return FixedOffsetPips;

      var atrPrice = _atr.Result.LastValue;
      var atrPips = atrPrice / Symbol.PipSize;

      var dynamicOffset = atrPips * AtrMultiplier;

      if (dynamicOffset < MinDynamicOffsetPips)
        dynamicOffset = MinDynamicOffsetPips;

      if (dynamicOffset > MaxDynamicOffsetPips)
        dynamicOffset = MaxDynamicOffsetPips;

      return dynamicOffset;
    }

    protected void ManageTrailing()
    {
      var hftPositions = Positions.Where(i => i.Label.StartsWith(PositionPrefix) && i.SymbolName == SymbolName);

      foreach (var position in hftPositions)
      {
        double netPips = position.Pips - (position.Commissions / (Symbol.PipValue * position.VolumeInUnits));

        if (netPips >= BrEvenTriggerPips && 
           ((position.TradeType == TradeType.Buy && (position.StopLoss == null || position.StopLoss < position.EntryPrice)) ||
            (position.TradeType == TradeType.Sell && (position.StopLoss == null || position.StopLoss > position.EntryPrice))))
        {
          var newStop = position.TradeType == TradeType.Buy
            ? position.EntryPrice + (Symbol.PipSize * Math.Max(BrEvenDistancePips, 0))
            : position.EntryPrice - (Symbol.PipSize * Math.Max(BrEvenDistancePips, 0));

          ModifyPosition(position, newStop, position.TakeProfit, ProtectionType.Absolute);
          Print("Break-even attivato per {0}", position.Id);
        }

        if (netPips >= TrailingTriggerPips)
        {
          double trailingDistance = TrailingDistancePips * Symbol.PipSize;
          double targetStop = position.TradeType == TradeType.Buy
            ? Symbol.Bid - trailingDistance
            : Symbol.Ask + trailingDistance;

          switch (position.TradeType)
          {
            case TradeType.Buy when (position.StopLoss == null || targetStop > position.StopLoss):
              ModifyPosition(position, targetStop, position.TakeProfit, ProtectionType.Absolute);
              break;

            case TradeType.Sell when (position.StopLoss == null || targetStop < position.StopLoss):
              ModifyPosition(position, targetStop, position.TakeProfit, ProtectionType.Absolute);
              break;
          }
        }
      }
    }
  }
}