using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using MathNet.Numerics.IntegralTransforms;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBePrivate.Global

namespace cAlgo.Robots
{
  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class Furiere : Robot
  {
    private MovingAverage _movingAverage;


    [Parameter("Prefix", Group = "General", DefaultValue = "Furiere")]
    public string PositionPrefix { get; set; }

    [Parameter("Full Name", Group = "General", DefaultValue = "Furiere")]
    public string FullName { get; set; }

    #region Trading Moment

    [Parameter("From the hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 7, MaxValue = 24,
      Step = 0.01)]
    public double FromHour { get; set; }

    [Parameter("To trade hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 17, MaxValue = 24,
      Step = 0.01)]
    public double ToTradeHour { get; set; }

    [Parameter("End of day hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 17, MaxValue = 24,
      Step = 0.01)]
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

    [Parameter(nameof(HarmonicsCount), MinValue = 0, Step = 1, DefaultValue = 40,
      Group = "FFT")]
    public int HarmonicsCount { get; set; } = 40;

    #endregion

    #region Trend Prediction

    [Parameter(nameof(PredictTrend), DefaultValue = true, Group = "Trend Prediction")]
    public bool PredictTrend { get; set; }

    [Parameter(nameof(MinTrendingCandles), MinValue = 0, DefaultValue = 3, Group = "Trend Prediction")]
    public int MinTrendingCandles { get; set; }

    [Parameter(nameof(PredictBounce), DefaultValue = true, Group = "Bounce Prediction")]
    public bool PredictBounce { get; set; }

    [Parameter(nameof(BounceTrendingCandles), DefaultValue = 3, Group = "Bounce Prediction")]
    public int BounceTrendingCandles { get; set; }

    [Parameter(nameof(PredictionPosition), MinValue = 1, DefaultValue = 5, Group = "Predictions")]
    public int PredictionPosition { get; set; } = 5;

    [Parameter(nameof(LookBackPosition), MinValue = 1, DefaultValue = 3, Group = "Predictions")]
    public int LookBackPosition { get; set; } = 3;

    [Parameter("Prediction Strength (pip)", MinValue = 0, DefaultValue = 3, Group = "Predictions")]
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
    
    #region Stoploss

    [Parameter("Trailing Stop", Group = "Stop Loss", DefaultValue = false)]
    public bool UseTrailingStop { get; set; }

    [Parameter("Trailing Stop (pips)", Group = "Stop Loss", MinValue = 0, DefaultValue = 100, Step = 10)]
    public double TrailingStopPips { get; set; }

    [Parameter("Add trailing stop only after a gain in pips", Group = "Stop Loss", DefaultValue = false)]
    public bool UseTrailingStopOnPips { get; set; }

    [Parameter("Minutes to wait before trailing stop on pips (min)", Group = "Stop Loss", MinValue = 0,
      DefaultValue = 100, Step = 10)]
    public int TrailingStopOnBarAge { get; set; }

    [Parameter("Trailing stop on pips size (pips)", Group = "Stop Loss", MinValue = 0, DefaultValue = 100,
      Step = 10)]
    public double TrailingStopOnPips { get; set; }

    #endregion
    
    #region TakeProfit

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
    public bool CloseAllEOD { get; set; }

    [Parameter("Take profit at end of the day", Group = "Close Strategy", DefaultValue = true)]
    public bool TakeProfitEOD { get; set; }

    [Parameter("Close on Bar", Group = "Close Strategy", DefaultValue = true)]
    public bool CloseOnBar { get; set; }

    [Parameter("Close on Bar position age (min)", Group = "Close Strategy", DefaultValue = 1)]
    public int CloseOnBarPostionAge { get; set; }

    [Parameter("Close old if profit", Group = "Close Strategy", DefaultValue = true)]
    public bool CloseOldIfProfit { get; set; }

    [Parameter("Old min profit", Group = "Close Strategy", DefaultValue = 100)]
    public double MinProfit { get; set; }

    #endregion


    private bool IsMarketTime
    {
      get
      {
        var now = Bars.Last().OpenTime;
        var @from = now.Date.AddMinutes(Math.Floor(FromHour) * 60 + Math.Min(59, (FromHour % 1) * 100));
        var to = now.Date.AddMinutes(Math.Floor(ToHour) * 60 + Math.Min(59, (ToHour % 1) * 100));

        return now >= from && now <= to;
      }
    }

    private bool IsTradeTime
    {
      get
      {
        var now = Bars.Last().OpenTime;
        var @from = now.Date.AddMinutes(Math.Floor(FromHour) * 60 + Math.Min(59, (FromHour % 1) * 100));
        var to = now.Date.AddMinutes(Math.Floor(ToTradeHour) * 60 + +Math.Min(59, (ToTradeHour % 1) * 100));

        return now >= from && now <= to;
      }
    }


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

    private double tradeQuantity;


    protected override void OnStart()
    {
      tradeQuantity = this.MinQuantity;
      this._movingAverage = Indicators.MovingAverage(this.MovingAverageDataSeries, this.MovingAveratePeriods, this.MovingAverageType);

      this.Positions.Closed += args =>
      {
        if (args.Reason == PositionCloseReason.StopLoss)
        {
          tradeQuantity = MinQuantity;
        }

        if (takeprofitadjusted.Contains(args.Position.Id))
        {
          takeprofitadjusted.Remove(args.Position.Id);
        }

        if (args.Reason == PositionCloseReason.TakeProfit)
        {
          var marginquantity = ((Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin) /
                                MarginLotDivider);
          tradeQuantity = (UseMarginToLotSize ? marginquantity : (tradeQuantity + StepQuantity));
          Print(
            $"Quantity from Margin {(Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin)}  / {MarginLotDivider} =  {marginquantity}");
        }

        tradeQuantity = Math.Max(tradeQuantity, MinQuantity);
        tradeQuantity = Math.Min(tradeQuantity, MaxQuantity);
      };
    }


    protected override void OnBar()
    {
      base.OnBar();

      AdjustPositionOnBar();
      CheckCloseAll();

      if (this.IsTradeTime)
        EvaluateMarketAndPlaceOrders();
    }

    protected override void OnTick()
    {
      if (Server.Time.Second < 2)
        AdjustPositionOnTick();
    }

    protected override void OnTimer()
    {
    }

    protected override void OnStop()
    {
    }

    private readonly HashSet<int> takeprofitadjusted = new();

    void AdjustPositionOnBar()
    {
      var now = Bars.Last().OpenTime;
      var barage = now.AddMinutes(-TakeProfitOnBarAge);

      if (TakeProfitOnBar)
      {
        if (this.Positions.Count > 0)
          foreach (var pos in Positions.Where(i =>
                     i.EntryTime < barage && !takeprofitadjusted.Contains(i.Id)))
          {
            pos.ModifyTakeProfitPips(OnBarTakeProfitPips);
            takeprofitadjusted.Add(pos.Id);
          }
      }
    }

    void AdjustPositionOnTick()
    {
      if (UseTrailingStopOnPips)
      {
        var now = Bars.Last().OpenTime;
        var barage = now.AddMinutes(-TrailingStopOnBarAge);

        if (this.Positions.Count > 0)
          foreach (var pos in Positions.Where(i =>
                     !i.HasTrailingStop && i.Pips > TrailingStopOnPips && i.EntryTime < barage))
          {
            pos.ModifyStopLossPips(TrailingStopPips);
            pos.ModifyTrailingStop(true);
          }
      }
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
      if (!this.IsMarketTime && this.CloseAllEOD)
        if (tradepositions.Any())
          foreach (var pos in tradepositions)
            pos.Close();


      if (this.CloseOldIfProfit)
        if (tradepositions.Any())
          if (tradepositions.Where(i => (now - i.EntryTime).TotalDays > 0).Sum(i => i.NetProfit / Symbol.PipValue) > MinProfit)
            foreach (var pos in tradepositions.Where(i => (now - i.EntryTime).TotalDays > 0))
              pos.Close();


      // Chiudo solo i profittevoli
      if (!this.IsMarketTime && this.TakeProfitEOD)
        if (tradepositions.Any())
          foreach (var pos in tradepositions.Where(i => i.Label.StartsWith(FullName) && i.NetProfit > 0))
            pos.Close();
    }

    private void EvaluateMarketAndPlaceOrders()
    {
      var qta = tradeQuantity;
      if (qta == 0) return;

      var lastbar = Bars.Last(1);
      if (lastbar.TickVolume < MinTickVolume) return;
      
      var (topPricep, bottomPricep, trp) = CalcMarketThreshold(MarketThresholdPeriods, MarketThresholdPerc);
      if (Symbol.Ask > trp) return;
      
      var direction = CalcDirection();
      if (direction.trade == null) return;

      var vol = qta.QuantityToVolume(Symbol);
      var margin = (Account.Margin == 0 ? Account.Equity : (Account.Equity / Account.Margin) * 100);
      Print("Margin Level: " + margin);
      var nearPosition = FindNearestPosition(TradeType.Buy);

      switch (direction.trade)
      {
        case TradeType.Buy when this.LongPositions.Length >= this.MaxLongPositions ||
                                (this.LongPositions.Length > 0 && CalcMaxLongPositionsOnMargin &&
                                 margin < MinMarginLevel):
          Print("Exit for Full long positions or margin");
          return;

        case TradeType.Buy when nearPosition != null && Math.Abs(nearPosition.NetProfit / Symbol.PipValue) < this.DistanceBetweenPositionsInPips:
          Print($"Exit for near position : {nearPosition.NetProfit / Symbol.PipValue} pips");
          return;

        case TradeType.Sell when this.ShortPositions.Length >= this.MaxShortPositions || this.ShortPositions.Sum(p => p.Quantity) >= this.LongPositions.Sum(p => p.Quantity):
          Print($"Exit for invalid hedging conditions : {nearPosition?.NetProfit / Symbol.PipValue} pips");
          return;
      }


      CalcTPandSl(out var tp);

      var result = ExecuteMarketOrder(
        direction.trade.Value,
        SymbolName,
        vol,
        FullName,
        UseTrailingStop && !UseTrailingStopOnPips ? TrailingStopPips : null,
        tp,
        "",
        UseTrailingStop);

      Print(
        $"Opened position ID:{result.Position.Id} {(result.IsSuccessful ? "succeded" : "failed")} {result.Error} - qta: {vol} at {result.Position.EntryPrice}  with tickvolume: {lastbar.TickVolume} - contract value : {result.Position.EntryPrice * result.Position.Quantity}");
    }


    private Position FindNearestPosition(TradeType? operation)
    {
      Position nearPosition = operation switch
      {
        TradeType.Buy => this.LongPositions.MinBy(p => Math.Abs(p.NetProfit / Symbol.PipValue)),
        TradeType.Sell => this.ShortPositions.MinBy(p => Math.Abs(p.NetProfit / Symbol.PipValue)),
        _ => null
      };

      return nearPosition;
    }

    private void CalcTPandSl(out double? tp)
    {
      var maxvolume = Bars.TickVolumes.TakeLast(SignalLength).Max();
      tp = Math.Max(MaxTakeProfitPips * Bars.Last(1).TickVolume / maxvolume, MinTakeProfitPips);
    }


    private (int sell, int buy, TradeType? trade, int strength) CalcDirection()
    {
      var sell = 0;
      var buy = 0;


      var (tt, comment) = CalcFftPredictions();

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

      return (sell, buy, sell > buy ? TradeType.Sell : sell < buy ? TradeType.Buy : null,
        strength: Math.Abs(sell - buy));
    }


    private (double topPrice, double bottomPrice, double thresholdPrice) CalcMarketThreshold(int lookbackperiods, double thresholdlevel)
    {
      var tp = Bars.ClosePrices.TakeLast(lookbackperiods).Max();
      var bp = Bars.ClosePrices.TakeLast(lookbackperiods).Min();
      
      var delta = tp - bp;
      var result = delta *  thresholdlevel /100;

      return (tp, bp, result+ bp);
    }
    
    private (TradeType?, string) CalcFftPredictions()
    {
      int fftSize = TradeFFT.ClosestUpperPowerOfTwo(2 * SignalLength);

      Complex[] priceSamples = new Complex[fftSize];
      for (int i = SignalLength; i < fftSize; i++) priceSamples[i] = 0;

      for (int i = 0; i < SignalLength; i++)
        priceSamples[i] = new Complex(
          Source.Last(SignalLength - i) - _movingAverage.Result.Last(SignalLength - i), 0.0);

      var imixedResult = priceSamples.CalcFFT().StrongHarmonicFilter(HarmonicsCount).CalcInvertFFT();

      var dir2 = imixedResult.ExtractValue(SignalLength, PredictionPosition) -
                 imixedResult.ExtractValue(SignalLength, 0);
      var dir1 = imixedResult.ExtractValue(SignalLength, 0) -
                 imixedResult.ExtractValue(SignalLength, -LookBackPosition);

      var actualdata = $"dir1: {dir1} -- dir2: {dir2}";
      if (Math.Abs(dir2) < PredictionStrength) return (null, $"Niente da fare {actualdata} - Strenght: {PredictionStrength}");


      if (PredictTrend)
      {
        switch (dir1)
        {
          case > 0 when dir2 > 0 &&
                        Bars.For(MinTrendingCandles, (bars, idx) => (int)bars.Last(idx).BarDirection()).All(i => i > 0):
            return (TradeType.Buy, $"{actualdata} --> Trending LONG");
          case < 0 when dir2 < 0 &&
                        Bars.For(MinTrendingCandles, (bars, idx) => (int)bars.Last(idx).BarDirection()).All(i => i < 0):
            return (TradeType.Sell, $"{actualdata} --> Trending SHORT");
        }
      }

      if (PredictBounce)
      {
        switch (dir1)
        {
          // Se le ultime x candele sono Long
          case > 0 when dir2 < 0 && Bars.For(BounceTrendingCandles, (bars, idx) => (int)bars.Last(idx).BarDirection()).All(i => i > 0):
            return (TradeType.Sell, $"{actualdata} --> Bouncing SHORT");
          // Se le ultime x candele solo short
          case < 0 when dir2 > 0 && Bars.For(BounceTrendingCandles, (bars, idx) => (int)bars.Last(idx).BarDirection()).All(i => i < 0):
            return (TradeType.Buy, $"{actualdata} --> Bouncing LONG");
        }
      }

      return (null, "");
    }
  }


  public static class Helpers
  {
    public static double QuantityToVolume(this double value, Symbol symbol) =>
      Math.Floor(symbol.QuantityToVolumeInUnits(value) / symbol.VolumeInUnitsStep) * symbol.VolumeInUnitsStep;

    public enum Direction
    {
      Up = 1,
      Down = -1
    }

    public static IEnumerable<TResult> For<TItems, TResult>(this TItems items, int count, Func<TItems, int, TResult> func) where TItems : IEnumerable
    {
      for (var i = 0; i < count; i++) yield return func(items, i);
    }

    public static Direction BarDirection(this Bar bar)
    {
      return bar.Close > bar.Open ? Direction.Up : Direction.Down;
    }

    public static IEnumerable<Bar> GetLasts(this Bars value, int count)
    {
      for (var i = 0; i < count; i++)
        yield return value.Last(i);
    }

    public static IEnumerable<double> GetLasts(this IndicatorDataSeries value, int count)
    {
      for (var i = 0; i < count; i++)
        yield return value.Last(i);
    }

    public static IndicatorDataSeries Clear(this IndicatorDataSeries indicator, int bars, int count)
    {
      for (int i = 0; i < bars + count; i++)
      {
        indicator[i] = double.NaN;
      }

      return indicator;
    }

    public static void ToIndicatorDataSeries(this Complex[] value, IndicatorDataSeries ds, int currentBarIndex)
    {
      ds.Clear(value.Length, 500);
      for (int i = 0; i < value.Length; i++)
      {
        ds[currentBarIndex - value.Length + i] = value[i].Real;
      }
    }


    public static Complex[] Extend(this Complex[] values, int extensionsize)
    {
      Complex[] samples = new Complex[values.Length + extensionsize];

      for (int i = values.Length; i < samples.Length; i++)
      {
        samples[i] = values.Last();
      }

      values.CopyTo(samples, 0);
      return samples;
    }

    public static double ExtractValue(this Complex[] values, int SignalLength, int x)
    {
      if (values.Length > SignalLength + x)
        return values[SignalLength + x].Real;
      return 0;
    }
  }

  public static class TradeFFT
  {
    public static int ClosestUpperPowerOfTwo(int x)
    {
      double power = Math.Ceiling(Math.Log(x, 2));
      return (int)Math.Pow(2, power);
    }


    public static Complex[] NormalizeAroundZero(this Complex[] values, out double average)
    {
      Complex[] samples = new Complex[values.Length];
      values.CopyTo(samples, 0);
      average = samples.Average(x => x.Real);
      for (int i = 0; i < samples.Length; i++)
      {
        samples[i] -= average;
      }

      return samples;
    }

    public static Complex[] CalcFFT(this Complex[] values, FourierOptions options = FourierOptions.Default)
    {
      Complex[] samples = new Complex[values.Length];
      values.CopyTo(samples, 0);
      // Calcolo la FFT
      Fourier.Forward(samples, options);
      return samples;
    }

    public static Complex[] CalcInvertFFT(this Complex[] value, FourierOptions options = FourierOptions.Default)
    {
      Complex[] samples = new Complex[value.Length];
      value.CopyTo(samples, 0);
      Fourier.Inverse(samples, options);
      return samples;
    }

    public static Complex[] ApplyAmplitudeFilter(this Complex[] value, double amplitude)
    {
      for (int i = 0; i < value.Length; i++)
      {
        if (value[i].Magnitude < amplitude)
          value[i] *= 0.0;
      }

      return value;
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

    public static Complex[] ApplyLowPassFilter(this Complex[] values, double cutoffValue,
      double lowpassattenuation = 1)
    {
      for (int i = 0; i < values.Length; i++)
      {
        // La frequenza di un campione è proporzionale alla sua posizione nell'array
        double frequency = (double)i / values.Length;

        if (Math.Abs(frequency) > cutoffValue)
        {
          // Attenua il campione se la frequenza è troppo alta
          values[i] *= (1 - lowpassattenuation);
        }
      }

      return values;
    }

    public static Complex[] ApplyHighPassFilter(this Complex[] values, double cutoffValue,
      double highpassattenuation = 1)
    {
      for (int i = 0; i < values.Length; i++)
      {
        // La frequenza di un campione è proporzionale alla sua posizione nell'array
        double frequency = (double)i / values.Length;

        if (Math.Abs(frequency) < cutoffValue)
        {
          // Attenua il campione se la frequenza è troppo bassa
          values[i] *= (1 - highpassattenuation);
        }
      }

      return values;
    }
  }
}