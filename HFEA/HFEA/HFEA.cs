using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace cAlgo.Robots
{
  public enum OrderDirectionMode
  {
    Both,
    LongOnly,
    ShortOnly
  }

  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class HFEA : Robot
  {
    [Parameter("Label", DefaultValue = "Spike Catcher")]
    public string PositionSuffix { get; set; }

    [Parameter("Position Prefix", DefaultValue = "HFA")]
    public string PositionPrefix { get; set; }

    [Parameter("Trading Start Time", DefaultValue = "08:00")]
    public string TradingStartTime { get; set; }

    [Parameter("Trading End Time", DefaultValue = "22:00")]
    public string TradingEndTime { get; set; }

    [Parameter("Friday Close Time", DefaultValue = "20:00")]
    public string FridayCloseTime { get; set; }

    [Parameter("Timer Interval in seconds", DefaultValue = "30")]
    public int TimerInterval { get; set; } = 30;

    [Parameter("Losing position Time to Live in minutes", DefaultValue = "3")]
    public int TimeToLive { get; set; } = 180;

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

    #endregion

    #region Pending Positions

    [Parameter("Max Spread Pips", DefaultValue = 2, MinValue = 0, Group = "Pending Positions")]
    public double MaxSpreadPips { get; set; }

    [Parameter("Fixed Offset Pips", DefaultValue = 5, MinValue = 1, Group = "Pending Positions")]
    public double FixedOffsetPips { get; set; }

    [Parameter("Limit Range Pips", DefaultValue = 1, MinValue = 0, MaxValue = 200, Group = "Pending Positions")]
    public double LimitRangePips { get; set; }

    [Parameter("StopLoss Distance Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double StopLossPips { get; set; }

    [Parameter("TakeProfit Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double TakeProfitPips { get; set; }

    [Parameter("Trailing Trigger Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double TrailingTriggerPips { get; set; }

    [Parameter("Trailing Distance Margin Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double TrailingDistancePips { get; set; }

    [Parameter("Distance Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double MinDistancePips { get; set; }

    [Parameter("Profit Distance Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
    public double ProfitDistancePips { get; set; }

    [Parameter("MinTickVolume", DefaultValue = 30, MinValue = 0, Group = "Pending Positions")]
    public int MinTickVolume { get; set; }
    #endregion

    string Label => $"{PositionPrefix}-{PositionSuffix}";

    private PendingOrder _shortOrder = null;
    private PendingOrder _longOrder = null;
    private Bar _currentBar;
    
    private Position[] ShortPositions
    {
      get
      {
        return Positions.Where(p =>
            p.Label == Label && p.SymbolName == SymbolName &&
            p.TradeType == TradeType.Sell)
          .ToArray();
      }
    }

    private Position[] LongPositions
    {
      get
      {
        return Positions.Where(p =>
            p.Label == Label && p.SymbolName == SymbolName &&
            p.TradeType == TradeType.Buy)
          .ToArray();
      }
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

    private double GetBuyPrice
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => Symbol.Ask + FixedOffsetPips * Symbol.PipSize;
    }


    private double GetSellPrice
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => Symbol.Bid - FixedOffsetPips * Symbol.PipSize;
    }

    protected override void OnStart()
    {
      base.OnStart();
      Positions.Opened += args => SearchForPendingOrders();

      Timer.Start(TimeSpan.FromSeconds(TimerInterval));
    }

    protected override void OnTimer()
    {
      base.OnTimer();
      CloseLoosingPositions();
      MoveOrders();
    }

    protected override void OnTick()
    {
      EvaluateTrailingStop();
    }

    protected override void OnBar() 
    {
      if (IsFridayCloseTimeReached())
        CloseFridayPositions();

      _currentBar = Bars.Last(1);

      CreatePendingOrders(LongPositions.Length < MaxLongPositions, ShortPositions.Length < MaxShortPositions);
    }

    private void CloseLoosingPositions()
    {
      foreach (var position in Positions)
      {
        if (position.SymbolName != SymbolName || position.Label != Label || position.NetProfit > 0) continue;

        if (position.EntryTime.AddMinutes(TimeToLive) < Server.Time)
          position.Close();
      }
    }

    private void EvaluateTrailingStop()
    {
      foreach (var position in Positions)
      {
        if (position.SymbolName != SymbolName || position.Label != Label) continue;
        if (position.HasTrailingStop) continue;
        if (position.NetProfit < 0 || position.NetProfit < TrailingTriggerPips * Symbol.PipValue) continue;

        var price = position.TradeType switch
        {
          TradeType.Buy => Symbol.Bid - TrailingDistancePips * Symbol.PipValue,
          TradeType.Sell => Symbol.Ask + TrailingDistancePips * Symbol.PipValue,
          _ => 0d
        };

        position.ModifyStopLossPrice(price);
        position.ModifyTrailingStop(true);
        position.ModifyTakeProfitPips(null);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveOrders()
    {
      if (_longOrder != null)
      {
        var profitShortPosition = ShortPositions.Any(p => p.NetProfit > 0);
        if (profitShortPosition)
        {
          _longOrder.Cancel();
          _longOrder = null;
        }
        else
        {
          _longOrder.ModifyTargetPrice(GetBuyPrice);
        }
      }

      if (_shortOrder != null)
      {
        var profitLongPosition = LongPositions.Any(p => p.NetProfit > 0);
        if (profitLongPosition)
        {
          _shortOrder.Cancel();
          _shortOrder = null;
        }
        else
        {
          _shortOrder.ModifyTargetPrice(GetSellPrice);
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SearchForPendingOrders()
    {
      _longOrder = null;
      _longOrder = PendingOrders.FirstOrDefault(i => i.SymbolName == SymbolName &&
                                                     i.TradeType == TradeType.Buy &&
                                                     i.Label == Label);

      _shortOrder = null;
      _shortOrder = PendingOrders.FirstOrDefault(i => i.SymbolName == SymbolName &&
                                                      i.TradeType == TradeType.Sell &&
                                                      i.Label == Label);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreatePendingOrders(bool canGoLong, bool canGoShort)
    {
      SearchForPendingOrders();
      if (!IsWithinTradingWindow()) return;
      if (_currentBar.TickVolume < MinTickVolume) return;
      
      var spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
      if (spreadPips > MaxSpreadPips)
      {
        Print("Spread troppo alto: {0:F2} pips", spreadPips);
        return;
      }


      
      var volumeInUnits = GetDynamicVolumeInUnits();

      if (canGoLong && _longOrder is null)
      {
        // Vado long solo se non ho posizioni short in profit  
        //var canopen = !ShortPositions.Any(p => p.NetProfit > 0);// && LongPositions.Any(p => p.NetProfit > 0);

        var pos = FindNearestPosition(TradeType.Buy);
        if (pos is { NetProfit: > 0, HasTrailingStop: true } && Math.Abs(pos.Pips) > ProfitDistancePips)
        {
          ExecuteMarketRangeOrder(TradeType.Buy, SymbolName, volumeInUnits, LimitRangePips, 
            GetBuyPrice, Label, StopLossPips == 0 ? null : StopLossPips, TakeProfitPips);
        }

        if (pos == null || Math.Abs(pos.Pips) > MinDistancePips)
          PlaceStopLimitOrderAsync(TradeType.Buy, SymbolName, volumeInUnits, GetBuyPrice, LimitRangePips, Label,
            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips, null, null, null, false, r =>
            {
              if (r.IsSuccessful)
                _longOrder = r.PendingOrder;
              else
                Print($"Error placing LONG StopLimitOrder : {r.Error.ToString()}");
            });
      }

      if (canGoShort && _shortOrder is null)
      {
        // Vado short solo se non ho posizioni long in profit 
        // var canOpen = !LongPositions.Any(p => p.NetProfit > 0) && !ShortPositions.Any(p => p.NetProfit < 0);

        var pos = FindNearestPosition(TradeType.Sell);
        if (pos is { NetProfit: > 0, HasTrailingStop: true } && Math.Abs(pos.Pips) > ProfitDistancePips)
        {
          ExecuteMarketRangeOrder(TradeType.Sell, SymbolName, volumeInUnits, LimitRangePips, 
            GetSellPrice, Label, StopLossPips == 0 ? null : StopLossPips, TakeProfitPips);
        }

        if (pos == null || Math.Abs(pos.Pips) > MinDistancePips)
          PlaceStopLimitOrderAsync(TradeType.Sell, SymbolName, volumeInUnits, GetSellPrice, LimitRangePips, Label,
            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips, null, null, null, false, r =>
            {
              if (r.IsSuccessful)
                _shortOrder = r.PendingOrder;
              else
                Print($"Error placing SHORT StopLimitOrder : {r.Error.ToString()}");
            });
      }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
  }
}