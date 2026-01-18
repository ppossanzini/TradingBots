using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using MathNet.Numerics.IntegralTransforms;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace
// ReSharper disable MemberCanBePrivate.Global

namespace cAlgo.Robots
{
  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class Furiere : Robot
  {
    private MovingAverage _movingAverage = null;

    [Parameter("Name", Group = "General", DefaultValue = "Furiere")]
    public string PositionPrefix { get; set; }

    [Parameter("Full Name", Group = "General", DefaultValue = "Furiere_Pending")]
    public string FullName { get; set; }

    #region Trading Moment

    [Parameter("From the hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 7, MaxValue = 24, Step = 0.01)]
    public double FromHour { get; set; }

    [Parameter("To trade hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 17, MaxValue = 24, Step = 0.01)]
    public double ToTradeHour { get; set; }

    [Parameter("End of day hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 17, MaxValue = 24, Step = 0.01)]
    public double ToHour { get; set; }

    [Parameter(nameof(MarketThresholdPeriods), Group = "Trading Moment", MinValue = 0, DefaultValue = 17, Step=1)]
    public int MarketThresholdPeriods { get; set; }
    
    [Parameter(nameof(MarketThresholdPerc), Group = "Trading Moment", MinValue = 0, DefaultValue = 85, MaxValue = 100, Step=1)]
    public double MarketThresholdPerc { get; set; }
    
    #endregion

    #region FFT

    [Parameter(nameof(SignalLength), DefaultValue = 1000, Group = "FFT")]
    public int SignalLength { get; set; }

    [Parameter(nameof(Source), Group = "FFT")]
    public DataSeries Source { get; set; }

    [Parameter(nameof(HarmonicsCount), MinValue = 0, Step = 1, DefaultValue = 40, Group = "FFT")]
    public int HarmonicsCount { get; set; } = 40;

    #endregion

    #region Trend & Bounce Prediction

    [Parameter(nameof(PredictTrend), DefaultValue = true, Group = "Prediction Logic")]
    public bool PredictTrend { get; set; }

    [Parameter(nameof(MinTrendingCandles), MinValue = 0, DefaultValue = 3, Group = "Prediction Logic")]
    public int MinTrendingCandles { get; set; }

    [Parameter(nameof(PredictBounce), DefaultValue = true, Group = "Prediction Logic")]
    public bool PredictBounce { get; set; }

    [Parameter(nameof(BounceTrendingCandles), DefaultValue = 3, Group = "Prediction Logic")]
    public int BounceTrendingCandles { get; set; }

    #endregion

    #region Pending & Expiration

    [Parameter("Prediction Position (Future)", MinValue = 1, DefaultValue = 5, Group = "Pending Strategy")]
    public int PredictionPosition { get; set; } = 5;


    [Parameter(nameof(LookBackPosition), MinValue = 1, DefaultValue = 3, Group = "Pending Strategy")]
    public int LookBackPosition { get; set; } = 3;

    [Parameter("Pending Expiration (Bars)", MinValue = 1, DefaultValue = 5, Group = "Pending Strategy")]
    public int PendingExpirationBars { get; set; } = 5;

    [Parameter("Prediction Strength (pip)", MinValue = 0, DefaultValue = 3, Group = "Pending Strategy")]
    public double PredictionStrength { get; set; }

    #endregion

    #region Moving Average

    [Parameter("Data Serie", Group = "Moving Average")]
    public DataSeries MovingAverageDataSeries { get; set; }

    [Parameter("Periods", Group = "Moving Average", DefaultValue = 100)]
    public int MovingAveratePeriods { get; set; }

    [Parameter("Moving Average Type", Group = "Moving Average")]
    public MovingAverageType MovingAverageType { get; set; }

    #endregion

    #region Risk Management

    [Parameter("Stop Loss", Group = "Stop Loss", DefaultValue = false)]
    public bool UseStopLoss { get; set; }

    [Parameter("Stop Loss (pips)", Group = "Stop Loss", MinValue = 0, DefaultValue = 100)]
    public double StopLoss { get; set; }

    [Parameter("Max Take Profit (pips)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double MaxTakeProfitPips { get; set; }

    [Parameter("Min Take Profit (pips)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double MinTakeProfitPips { get; set; }

    [Parameter("Adjust Take Profit on bar(pips)", Group = "Take Profit", DefaultValue = false)]
    public bool TakeProfitOnBar { get; set; }

    [Parameter("OnBar Take Profit (pips)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double OnBarTakeProfitPips { get; set; }

    [Parameter("OnBar Take Profit age (min)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double TakeProfitOnBarAge { get; set; }

    #endregion


    #region Money Management

    [Parameter("Min Quantity (Lots)", Group = "Money Management", DefaultValue = 1, MinValue = 0.001, Step = 0.01)]
    public double MinQuantity { get; set; }

    [Parameter("Max Quantity (Lots)", Group = "Money Management", DefaultValue = 1, MinValue = 0.001, Step = 0.01)]
    public double MaxQuantity { get; set; }

    [Parameter("Step Quantity (Lots)", Group = "Money Management", DefaultValue = 1, MinValue = 0.001,
      Step = 0.0001)]
    public double StepQuantity { get; set; }

    [Parameter("Evaluate max number of long open positions on margin", Group = "Money Management")]
    public bool CalcMaxLongPositionsOnMargin { get; set; }

    [Parameter("Min margin level", Group = "Money Management", DefaultValue = 150, MinValue = 100, Step = 10)]
    public double MinMarginLevel { get; set; }

    [Parameter("Use margin to lot size ", Group = "Money Management")]
    public bool UseMarginToLotSize { get; set; }

    [Parameter("Margin Lot divider", Group = "Money Management", DefaultValue = 1500)]
    public double MarginLotDivider { get; set; }

    [Parameter("Max active long positions", Group = "Money Management", DefaultValue = 3, MinValue = 0,
      Step = 1)]
    public int MaxLongPositions { get; set; }

    [Parameter("Max active short positions", Group = "Money Management", DefaultValue = 0, MinValue = 0,
      Step = 1)]
    public int MaxShortPositions { get; set; }

    [Parameter("Distance between positions (pips)", Group = "Money Management", MinValue = 0, DefaultValue = 50)]
    public double DistanceBetweenPositionsInPips { get; set; } = 50;


    [Parameter("Min TickVolume to open positions", Group = "Money Management", MinValue = 0, DefaultValue = 0)]
    public int MinTickVolume { get; set; } = 0;

    #endregion

    #region Close Strategy

    [Parameter("Close all at the end of the day", Group = "Close Strategy", DefaultValue = true)]
    public bool CloseAllEod { get; set; }

    [Parameter("Take profit at end of the day", Group = "Close Strategy", DefaultValue = true)]
    public bool TakeProfitEod { get; set; }

    [Parameter("Close on Bar", Group = "Close Strategy", DefaultValue = true)]
    public bool CloseOnBar { get; set; }

    [Parameter("Close on Bar position age (min)", Group = "Close Strategy", DefaultValue = 1)]
    public int CloseOnBarPostionAge { get; set; }

    [Parameter("Close old if profit", Group = "Close Strategy", DefaultValue = true)]
    public bool CloseOldIfProfit { get; set; }

    [Parameter("Old min profit", Group = "Close Strategy", DefaultValue = 100)]
    public double MinProfit { get; set; }

    #endregion

    private double _tradeQuantity = 0;
    private bool IsMarketTime => Server.Time.Hour >= FromHour && Server.Time.Hour <= ToHour;
    private bool IsTradeTime => Server.Time.Hour >= FromHour && Server.Time.Hour <= ToTradeHour;


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

    private readonly HashSet<int> _takeprofitadjusted = new();

    protected override void OnStart()
    {
      Bars.LoadMoreHistory();
      _tradeQuantity = MinQuantity;
      _movingAverage = Indicators.MovingAverage(MovingAverageDataSeries, MovingAveratePeriods, MovingAverageType);

      this.Positions.Closed += args =>
      {
        if (args.Reason == PositionCloseReason.StopLoss)
        {
          _tradeQuantity = MinQuantity;
        }

        if (_takeprofitadjusted.Contains(args.Position.Id))
        {
          _takeprofitadjusted.Remove(args.Position.Id);
        }

        if (args.Reason == PositionCloseReason.TakeProfit)
        {
          var marginquantity = ((Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin) /
                                MarginLotDivider);
          _tradeQuantity = (UseMarginToLotSize ? marginquantity : (_tradeQuantity + StepQuantity));
          Print(
            $"Quantity from Margin {(Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin)}  / {MarginLotDivider} =  {marginquantity}");
        }

        _tradeQuantity = Math.Max(_tradeQuantity, MinQuantity);
        _tradeQuantity = Math.Min(_tradeQuantity, MaxQuantity);
      };
    }

    protected override void OnBar()
    {
      CheckCloseAll();
      AdjustPositionOnBar();
      if (IsTradeTime)
        EvaluateMarketAndPlaceOrders();
    }

    void AdjustPositionOnBar()
    {
      var now = Bars.Last().OpenTime;
      var barage = now.AddMinutes(-TakeProfitOnBarAge);

      if (TakeProfitOnBar)
      {
        if (this.Positions.Count > 0)
          foreach (var pos in Positions.Where(i =>
                     i.EntryTime < barage && !_takeprofitadjusted.Contains(i.Id)))
          {
            pos.ModifyTakeProfitPips(OnBarTakeProfitPips);
            _takeprofitadjusted.Add(pos.Id);
          }
      }
    }

    private void EvaluateMarketAndPlaceOrders()
    {
      DateTime expiration = Server.Time.AddMinutes((Bars.LastBar.OpenTime - Bars.Last(PendingExpirationBars).OpenTime).TotalMinutes);

      // 1. Pulizia ordini pendenti obsoleti o contrari alla nuova previsione o con positione predetta lontada dalla nuova previsione
      double targetPrice = GetFftTargetPrice(PredictionPosition);
      var pendingOrders = PendingOrders.Where(o => o.Label == FullName && o.SymbolName == SymbolName).ToArray();
      foreach (var po in pendingOrders)
      {
        if (targetPrice > po.TargetPrice) continue;

        po.ModifyExpirationTime(expiration);
        po.ModifyTargetPrice(targetPrice);
      }
      
      var lastbar = Bars.Last(1);
      if (lastbar.TickVolume < MinTickVolume)
      {
        Print($"Exit no volume: {lastbar.TickVolume} < {MinTickVolume}");
        return;
      }
      
      var (topPricep, bottomPricep, trp) = CalcMarketThreshold(MarketThresholdPeriods, MarketThresholdPerc);
      if (Symbol.Ask > trp) return;

      var direction = CalcDirection();
      if (direction.trade == null) return;

      var qta = _tradeQuantity;
      if (qta == 0) return;

      // 2. Controllo Limiti (Aperte + Pendenti)
      int activeLongs = LongPositions.Length +
                        PendingOrders.Count(o => o.TradeType == TradeType.Buy && o.Label == FullName);
      int activeShorts = ShortPositions.Length +
                         PendingOrders.Count(o => o.TradeType == TradeType.Sell && o.Label == FullName);

      int longPending = PendingOrders.Count(o => o.TradeType == TradeType.Buy && o.Label == FullName);
      int shortPending = PendingOrders.Count(o => o.TradeType == TradeType.Sell && o.Label == FullName);

      var margin = (Account.Margin == 0 ? Account.Equity : (Account.Equity / Account.Margin) * 100);
      Print("Margin Level: " + margin);
      var nearPosition = FindNearestPosition(TradeType.Buy);
      var nearPending = PendingOrders.Where(o => o.TradeType == TradeType.Buy && o.Label == FullName).MinBy(o => Math.Abs(o.DistancePips));
      
      switch (direction.trade)
      {
        case TradeType.Buy when activeLongs >= this.MaxLongPositions ||
                                longPending > 0 ||
                                (activeLongs > 0 && CalcMaxLongPositionsOnMargin && margin < MinMarginLevel):
          Print("Exit for Full long positions or margin");
          return;

        case TradeType.Buy when nearPosition != null && Math.Abs(nearPosition.NetProfit / Symbol.PipValue) < this.DistanceBetweenPositionsInPips:
          Print($"Exit for near position : {nearPosition.NetProfit / Symbol.PipValue} pips");
          return;

        case TradeType.Buy when nearPending != null && Math.Abs(nearPending.DistancePips) < this.DistanceBetweenPositionsInPips:
          Print($"Exit for near pending : {nearPending.DistancePips} pips");
          return;
        case TradeType.Sell when activeShorts >= this.MaxShortPositions ||
                                 shortPending > 0 ||
                                 this.ShortPositions.Sum(p => p.Quantity) >= this.LongPositions.Sum(p => p.Quantity):
          Print($"Exit for invalid hedging conditions : {nearPosition?.NetProfit / Symbol.PipValue} pips");
          return;
      }


      var vol = qta.QuantityToVolume(Symbol);

      CalcTPandSl(direction.trade, out var tp);

      switch (direction.trade)
      {
        case TradeType.Buy when targetPrice > Symbol.Ask:
          PlaceStopOrder(direction.trade.Value,
            SymbolName, vol, targetPrice,
            FullName,
            UseStopLoss ? StopLoss : null,
            tp, ProtectionType.Relative, expiration);
          break;
        case  TradeType.Buy when direction.predictiontype == Helpers.PredictionTypeEnum.Bounce && targetPrice < Symbol.Bid:
          PlaceLimitOrder(TradeType.Buy,
            SymbolName, vol, targetPrice,
            FullName,
            UseStopLoss ? StopLoss : null,
            tp, ProtectionType.Relative, expiration);
          break;
      }
    }

    private void CalcTPandSl(TradeType? operation, out double? tp)
    {
      var maxvolume = Bars.TickVolumes.TakeLast(SignalLength).Max();
      tp = Math.Max(MaxTakeProfitPips * Bars.Last(1).TickVolume / maxvolume, MinTakeProfitPips);
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

    private double GetFftTargetPrice(int futureIndex)
    {
      int fftSize = TradeFFT.ClosestUpperPowerOfTwo(2 * SignalLength);
      Complex[] priceSamples = new Complex[fftSize];

      for (int i = 0; i < SignalLength; i++)
        priceSamples[i] = new Complex(Source.Last(SignalLength - i) - _movingAverage.Result.Last(SignalLength - i), 0.0);

      var imixedResult = priceSamples.CalcFft().StrongHarmonicFilter(HarmonicsCount).CalcInvertFft();
      double predictedOffset = imixedResult.ExtractValue(SignalLength, futureIndex);

      return _movingAverage.Result.LastValue + predictedOffset;
    }


    private (int sell, int buy, TradeType? trade, int strength, Helpers.PredictionTypeEnum? predictiontype) CalcDirection()
    {
      var sell = 0;
      var buy = 0;


      var (tt, previsionTypeEnum, comment) = CalcFftPredictions();

      if (!string.IsNullOrWhiteSpace(comment)) Print(comment);
      switch (tt)
      {
        case TradeType.Buy:
          buy += 1;
          break;
        case TradeType.Sell:
          sell += 1;
          break;
      }

      return (sell, buy, sell > buy ? TradeType.Sell : sell < buy ? TradeType.Buy : null, strength: Math.Abs(sell - buy), previsionTypeEnum);
    }

    private (double topPrice, double bottomPrice, double thresholdPrice) CalcMarketThreshold(int lookbackperiods, double thresholdlevel)
    {
      var tp = Bars.ClosePrices.TakeLast(lookbackperiods).Max();
      var bp = Bars.ClosePrices.TakeLast(lookbackperiods).Min();
      
      var delta = tp - bp;
      var result = delta *  thresholdlevel /100;

      return (tp, bp, result+ bp);
    }
    

    private (TradeType?, Helpers.PredictionTypeEnum?, string) CalcFftPredictions()
    {
      int fftSize = TradeFFT.ClosestUpperPowerOfTwo(2 * SignalLength);

      Complex[] priceSamples = new Complex[fftSize];
      for (int i = SignalLength; i < fftSize; i++) priceSamples[i] = 0;

      for (int i = 0; i < SignalLength; i++)
        priceSamples[i] = new Complex(
          Source.Last(SignalLength - i) - _movingAverage.Result.Last(SignalLength - i), 0.0);

      var imixedResult = priceSamples.CalcFft().StrongHarmonicFilter(HarmonicsCount).CalcInvertFft();

      var dir2 = imixedResult.ExtractValue(SignalLength, PredictionPosition + 1) -
                 imixedResult.ExtractValue(SignalLength, 1);
      var dir1 = imixedResult.ExtractValue(SignalLength, -1) -
                 imixedResult.ExtractValue(SignalLength, -LookBackPosition - 1);

      var actualdata = $"dir1: {dir1} -- dir2: {dir2}";
      if (Math.Abs(dir2) < PredictionStrength) return (null, null, $"Niente da fare {actualdata} - Strenght: {PredictionStrength}");


      if (PredictTrend)
      {
        if (dir1 > 0 && dir2 > 0 && Bars.TakeLast(MinTrendingCandles).Select(b => b.BarDirection()).All(i => i > 0))
          return (TradeType.Buy, Helpers.PredictionTypeEnum.Trend, $"{actualdata} --> Trending LONG");
        if (dir1 < 0 && dir2 < 0 && Bars.TakeLast(MinTrendingCandles).Select(b => b.BarDirection()).All(i => i < 0))
          return (TradeType.Sell, Helpers.PredictionTypeEnum.Trend, $"{actualdata} --> Trending SHORT");
      }

      if (PredictBounce)
      {
        // var nv = imixedResult.ExtractValue(SignalLength, 5);
        if (dir1 > 0 && dir2 < 0 && //nv < imixedResult.ExtractValue(SignalLength, 0) &&
            Bars.TakeLast(BounceTrendingCandles).Select(b => b.BarDirection()).All(i => i > 0)) // Se le ultime x candele sono Long
          return (TradeType.Sell, Helpers.PredictionTypeEnum.Bounce, $"{actualdata} --> Bouncing SHORT");

        if (dir1 < 0 && dir2 > 0 && //nv > imixedResult.ExtractValue(SignalLength, 0) &&
            Bars.TakeLast(BounceTrendingCandles).Select(b => b.BarDirection()).All(i => i < 0)) // Se le ultime x candele solo short
          return (TradeType.Buy, Helpers.PredictionTypeEnum.Bounce, $"{actualdata} --> Bouncing LONG");
      }

      return (null, null, "");
    }

    private void CheckCloseAll()
    {
      var now = Bars.Last().OpenTime;
      var closingbartime = now.AddMinutes(-CloseOnBarPostionAge);

      // Applico politiche di chiusura solo per operazioni che hanno trailing stop
      var tradepositions = Positions.Where(i =>
        i.SymbolName == SymbolName &&
        i.Label.StartsWith(FullName)).ToArray();

      if (CloseOnBar)
      {
        if (tradepositions.Any())
          foreach (var pos in tradepositions.Where(i => i.EntryTime < closingbartime && i.NetProfit > 0))
            pos.Close();
      }

      // Chiudo tutto a fine giornata. 
      if (!this.IsMarketTime && this.CloseAllEod)
        if (tradepositions.Any())
          foreach (var pos in tradepositions)
            pos.Close();


      if (this.CloseOldIfProfit)
        if (tradepositions.Any())
          if (tradepositions.Where(i => (now - i.EntryTime).TotalDays > 0).Sum(i => i.NetProfit / Symbol.PipValue) > MinProfit)
            foreach (var pos in tradepositions.Where(i => (now - i.EntryTime).TotalDays > 0))
              pos.Close();


      // Chiudo solo i profittevoli
      if (!this.IsMarketTime && this.TakeProfitEod)
        if (tradepositions.Any())
          foreach (var pos in tradepositions.Where(i => i.Label.StartsWith(FullName) && i.NetProfit > 0))
            pos.Close();
    }
  }

  #region Helpers & FFT Classes

  public static class Helpers
  {
    public enum Direction : int
    {
      Up = 1,
      Down = -1
    }


    public enum PredictionTypeEnum : int
    {
      Trend = 1,
      Bounce = 2
    }

    public static Direction BarDirection(this Bar bar)
    {
      return bar.Close > bar.Open ? Direction.Up : Direction.Down;
    }

    public static IEnumerable<TResult> For<TItems, TResult>(this TItems items, int count, Func<TItems, int, TResult> func) where TItems : IEnumerable
    {
      for (int i = 0; i < count; i++) yield return func(items, i);
    }

    public static double QuantityToVolume(this double value, Symbol symbol) =>
      Math.Floor(symbol.QuantityToVolumeInUnits(value) / symbol.VolumeInUnitsStep) * symbol.VolumeInUnitsStep;

    public static IEnumerable<Bar> GetLasts(this Bars value, int count)
    {
      for (int i = 1; i <= count; i++) yield return value.Last(i);
    }

    public static double ExtractValue(this Complex[] values, int signalLength, int x)
    {
      if (values.Length > signalLength + x && signalLength + x >= 0)
        return values[signalLength + x].Real;
      return 0;
    }
  }

  public static class TradeFFT
  {
    public static int ClosestUpperPowerOfTwo(int x) => (int)Math.Pow(2, Math.Ceiling(Math.Log(x, 2)));

    public static Complex[] CalcFft(this Complex[] values)
    {
      Complex[] samples = new Complex[values.Length];
      values.CopyTo(samples, 0);
      Fourier.Forward(samples, FourierOptions.Default);
      return samples;
    }

    public static Complex[] CalcInvertFft(this Complex[] value)
    {
      Complex[] samples = new Complex[value.Length];
      value.CopyTo(samples, 0);
      Fourier.Inverse(samples, FourierOptions.Default);
      return samples;
    }

    public static Complex[] StrongHarmonicFilter(this Complex[] values, int harmonicsCount)
    {
      var harmonics = values.OrderByDescending(x => x.Magnitude).Take(harmonicsCount).ToList();
      for (int i = 0; i < values.Length; i++)
      {
        if (!harmonics.Contains(values[i])) values[i] = Complex.Zero;
      }

      return values;
    }
  }

  #endregion
}