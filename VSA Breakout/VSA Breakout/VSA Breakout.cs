using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
  [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
  public class VsaBreakoutPro : Robot
  {

    [Parameter("Full Name", Group = "General", DefaultValue = "Furiere_VsaBreakoutPro")]
    public string FullName { get; set; }


    [Parameter("From the hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 7, MaxValue = 24, Step = 0.01)]
    public double FromHour { get; set; }

    [Parameter("End of day hour", Group = "Trading Moment", MinValue = 0, DefaultValue = 17, MaxValue = 24, Step = 0.01)]
    public double ToHour { get; set; }

    [Parameter("Start Hour (UTC)", DefaultValue = 14)] // Apertura NY (14:30-15:00 UTC)
    public int StartHour { get; set; }

    [Parameter("End Hour (UTC)", DefaultValue = 15)]
    public int EndHour { get; set; }

    [Parameter("Volume Multiplier", DefaultValue = 1.5, MinValue = 1.0)]
    public double VolMultiplier { get; set; }

    [Parameter("Max Take Profit (pips)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double MaxTakeProfitPips { get; set; }

    [Parameter("Min Take Profit (pips)", Group = "Take Profit", MinValue = 0, DefaultValue = 30, Step = 10)]
    public double MinTakeProfitPips { get; set; }


    #region Money Management

    [Parameter("Min Quantity (Lots)", Group = "Money Management", DefaultValue = 1, MinValue = 0.001, Step = 0.01)]
    public double MinQuantity { get; set; }

    [Parameter("Max Quantity (Lots)", Group = "Money Management", DefaultValue = 1, MinValue = 0.001, Step = 0.01)]
    public double MaxQuantity { get; set; }

    [Parameter("Min margin level", Group = "Money Management", DefaultValue = 150, MinValue = 100, Step = 10)]
    public double MinMarginLevel { get; set; }

    [Parameter("Use margin to lot size ", Group = "Money Management")]
    public bool UseMarginToLotSize { get; set; }

    [Parameter("Margin Lot divider", Group = "Money Management", DefaultValue = 1500)]
    public double MarginLotDivider { get; set; }

    #endregion

    #region Close Strategy

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

    private bool IsMarketTime => Server.Time.Hour >= FromHour && Server.Time.Hour <= ToHour;


    private double _sessionHigh;
    private bool _isMonitoring;

    protected override void OnStart()
    {
      _isMonitoring = false;
      _sessionHigh = 0;
    }

    protected override void OnBar()
    {
      CheckCloseAll();
      var currentTime = Server.Time.TimeOfDay;
      var start = new TimeSpan(StartHour, 30, 0);
      var end = new TimeSpan(EndHour, 0, 0);

      // 1. Fase di accumulo dati (Opening Range)
      if (currentTime >= start && currentTime <= end)
      {
        _sessionHigh = Math.Max(_sessionHigh, Bars.HighPrices.Last(1));
        _isMonitoring = true;
        Chart.DrawHorizontalLine("HighLevel", _sessionHigh, Color.Yellow, 2, LineStyle.Lines);
      }

      // 2. Fase di monitoraggio Breakout (dopo le 15:00 UTC)
      if (currentTime > end && _isMonitoring)
      {
        CheckForVsaBreakout();
      }
    }

    private void CheckForVsaBreakout()
    {
      var lastBar = Bars.Last(1);
      var prevVolumes = Bars.TickVolumes.TakeLast(19).Average();

      // CONDIZIONI VSA PER IL BREAKOUT:
      // A. Il prezzo chiude SOPRA il massimo della sessione
      bool priceBreak = lastBar.Close > _sessionHigh;

      // B. Il volume della candela di rottura è superiore alla media (Sforzo)
      bool volumeConfirm = lastBar.TickVolume > (prevVolumes * VolMultiplier);

      var margin = (Account.Margin == 0 ? Account.Equity : (Account.Equity / Account.Margin) * 100);
      Print("Margin Level: " + margin);

      if (priceBreak && volumeConfirm && margin > MinMarginLevel)
      {
        ExecuteOrder();
        _isMonitoring = false; // Stop monitoraggio per oggi dopo l'ingresso
      }
    }

    private void CalcTPandSl(out double tp)
    {
      var maxvolume = Bars.TickVolumes.TakeLast(100).Max();
      tp = Math.Max(MaxTakeProfitPips * Bars.Last(1).TickVolume / maxvolume, MinTakeProfitPips);
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

    private void ExecuteOrder()
    {
      var tradeQuantity = 0d;

      var marginquantity = ((Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin) /
                            MarginLotDivider);
      tradeQuantity = (UseMarginToLotSize ? marginquantity : (tradeQuantity));
      Print(
        $"Quantity from Margin {(Account.Margin == 0 ? Account.Equity : Account.Equity / Account.Margin)}  / {MarginLotDivider} =  {marginquantity}");

      tradeQuantity = Math.Max(tradeQuantity, MinQuantity);
      tradeQuantity = Math.Min(tradeQuantity, MaxQuantity);

      var volume = Symbol.QuantityToVolumeInUnits(tradeQuantity);

      CalcTPandSl(out double tp);

      ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, FullName, null, tp);
      Print("VSA Breakout Confermato! Volume: {0}, Livello: {1}", Bars.TickVolumes.Last(1), _sessionHigh);
    }
  }
}