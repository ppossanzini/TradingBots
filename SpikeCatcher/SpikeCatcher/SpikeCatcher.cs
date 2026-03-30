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

    #endregion

    #region Take Profit

    [Parameter("Take Profit Pips", DefaultValue = 500, MinValue = 1, Group = "Take Profit")]
    public double TakeProfitPips { get; set; }

    [Parameter("Trailing Trigger Pips", DefaultValue = 20, MinValue = 1, Group = "Take Profit")]
    public double TrailingTriggerPips { get; set; }

    [Parameter("Trailing Distance Pips", DefaultValue = 8, MinValue = 1, Group = "Take Profit")]
    public double TrailingDistancePips { get; set; }

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
    private AverageTrueRange _atr;

    protected override void OnStart()
    {
      _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);
      _lastBarTime = Bars.OpenTimes.LastValue;

      UpdatePendingOrders();
    }

    protected void ManageTrailing()
    {
      // 1. Recupera tutte le posizioni aperte dal bot
      var hftPositions = Positions.Where(i => i.Label.StartsWith(PositionPrefix) && i.SymbolName == SymbolName);

      foreach (var position in hftPositions)
      {
        // Calcoliamo il profitto in Pips netti (sottraendo le commissioni)
        double netPips = position.Pips - (position.Commissions / (Symbol.PipValue * position.VolumeInUnits));

        // 2. LOGICA BREAK-EVEN RAPIDO
        // Se siamo in profitto di 0.5 pip netti, spostiamo lo stop a +0.1 (protezione capitale)
        if (netPips >= BrEvenTriggerPips && (position.StopLoss == null || position.StopLoss < position.EntryPrice))
        {
          var newStop = position.TradeType == TradeType.Buy
            ? position.EntryPrice + (Symbol.PipSize * BrEvenDistancePips)
            : position.EntryPrice - (Symbol.PipSize * BrEvenTriggerPips);

          ModifyPosition(position, newStop, position.TakeProfit, ProtectionType.Absolute);
          Print("Break-even attivato per {0}", position.Id);
        }

        // 3. TRAILING "SMART" (Asfissiante)
        // Se il profitto sale oltre 1.2 pip, inseguiamo il prezzo a soli 0.3 pip di distanza
        if (netPips > TrailingTriggerPips)
        {
          double trailingDistance = Symbol.PipSize * TrailingDistancePips;
          double targetStop = position.TradeType == TradeType.Buy
            ? Symbol.Bid - trailingDistance
            : Symbol.Ask + trailingDistance;

          switch (position.TradeType)
          {
            case TradeType.Buy when ( position.StopLoss == null || targetStop > position.StopLoss ):
            case TradeType.Sell when (position.StopLoss == null || targetStop < position.StopLoss  ):
              ModifyPosition(position, targetStop, null, ProtectionType.Absolute); break;
          }
        }
      }
    }

    protected override void OnTick()
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

    protected override void OnPositionOpened(Position position)
    {
      if (position.SymbolName != SymbolName || position.Label != Label)
        return;

      Print("Posizione aperta: {0} {1}. Pending lasciate attive.", position.TradeType, position.EntryPrice);
    }

    private void UpdatePendingOrders()
    {
      if (!IsWithinTradingWindow())
      {
        Print("Fuori fascia oraria di trading, nessun nuovo ordine piazzato.");
        return;
      }

      var spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
      if (spreadPips > MaxSpreadPips)
      {
        Print("Spread troppo alto: {0:F2} pips", spreadPips);
        return;
      }

      var maxPriceInEvaluationRange = Bars.TakeLast(EvaluationgCandles).Max(i => Math.Max(i.Close, i.Open));
      var minPriceInEvaluationRange = Bars.TakeLast(EvaluationgCandles).Max(i => Math.Min(i.Close, i.Open));

      var deltarange = maxPriceInEvaluationRange - minPriceInEvaluationRange;
      var minTreshold = minPriceInEvaluationRange + (deltarange * EvaluatingRange / 100);
      var maxTreshold = maxPriceInEvaluationRange - (deltarange * EvaluatingRange / 100);


      var nearLongPosition = FindNearestPosition(TradeType.Buy);
      var nearShortPosition = FindNearestPosition(TradeType.Sell);

      var isLongPositionFarEnough = (LongPositions.Length < MaxLongPositions) &&
                                    (nearLongPosition == null || Math.Abs(nearLongPosition.NetProfit / Symbol.PipSize) >
                                      DistanceBetweenPositionsInPips) && Symbol.Ask < minTreshold;

      var isShortPositionFarEnough = (ShortPositions.Length < MaxShortPositions) &&
                                     (nearShortPosition == null ||
                                      Math.Abs(nearShortPosition.NetProfit / Symbol.PipSize) >
                                      DistanceBetweenPositionsInPips && Symbol.Bid > maxTreshold);

      foreach (var o in PendingOrders.Where(p => p.Label.StartsWith(PositionPrefix) && p.SymbolName == SymbolName ) ) o.Cancel();

      var volumeInUnits = GetDynamicVolumeInUnits();
      var offsetPips = GetCurrentOffsetPips();

      var buyPrice = Symbol.Ask + offsetPips * Symbol.PipSize;
      var sellPrice = Symbol.Bid - offsetPips * Symbol.PipSize;

      switch (OrderDirection)
      {
        case OrderDirectionMode.Both when isLongPositionFarEnough:
        case OrderDirectionMode.LongOnly when isLongPositionFarEnough:

          PlaceStopOrder(TradeType.Buy, SymbolName, volumeInUnits, buyPrice, $"{PositionPrefix}-{Label}", null,
            TakeProfitPips); break;

        case OrderDirectionMode.Both when isShortPositionFarEnough:
        case OrderDirectionMode.ShortOnly when isShortPositionFarEnough:

          PlaceStopOrder(TradeType.Sell, SymbolName, volumeInUnits, sellPrice, $"{PositionPrefix}-{Label}", null,
            TakeProfitPips); break;
      }


      Print("Pending aggiornati. Volume={0} units, Offset={1:F1} pips, Direzione={2}, BuyStop={3}, SellStop={4}",
        volumeInUnits, offsetPips, OrderDirection, buyPrice, sellPrice);
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

    private void ManageOpenPositions()
    {
      var positions = Positions.Where(p => p.SymbolName == SymbolName && p.Label == Label).ToArray();

      if (positions.Length == 0)
        return;


      foreach (var position in positions)
      {
        var currentProfitPips = position.Pips;
        double? newStopLossPrice = position.StopLoss;

        if (currentProfitPips >= TrailingTriggerPips)
        {
          if (position.TradeType == TradeType.Buy)
          {
            var trailingPrice = Symbol.Bid - TrailingDistancePips * Symbol.PipSize;
            if (!position.StopLoss.HasValue || trailingPrice > position.StopLoss.Value)
              newStopLossPrice = trailingPrice;
          }
          else if (position.TradeType == TradeType.Sell)
          {
            var trailingPrice = Symbol.Ask + TrailingDistancePips * Symbol.PipSize;
            if (!position.StopLoss.HasValue || trailingPrice < position.StopLoss.Value)
              newStopLossPrice = trailingPrice;
          }
        }

        if (newStopLossPrice.HasValue &&
            (!position.StopLoss.HasValue ||
             Math.Abs(position.StopLoss.Value - newStopLossPrice.Value) > Symbol.PipSize / 10))
        {
          ModifyPosition(position, newStopLossPrice, position.TakeProfit);
          Print("Posizione aggiornata. SL={0}, Pips={1:F1}", newStopLossPrice.Value, currentProfitPips);
        }
      }
    }
  }
}