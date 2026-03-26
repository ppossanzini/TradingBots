using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System;
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
    public class SpikeCAtcher : Robot
    {
        [Parameter("Min Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01)]
        public double MinVolumeLots { get; set; }

        [Parameter("Max Volume (Lots)", DefaultValue = 0.10, MinValue = 0.01)]
        public double MaxVolumeLots { get; set; }

        [Parameter("Margin Percent Divider", DefaultValue = 10.0, MinValue = 0.1)]
        public double MarginPercentDivider { get; set; }

        [Parameter("Fixed Offset Pips", DefaultValue = 5, MinValue = 1)]
        public double FixedOffsetPips { get; set; }

        [Parameter("Use Dynamic Offset", DefaultValue = true)]
        public bool UseDynamicOffset { get; set; }

        [Parameter("ATR Period", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR Multiplier", DefaultValue = 1.0, MinValue = 0.1)]
        public double AtrMultiplier { get; set; }

        [Parameter("Min Dynamic Offset Pips", DefaultValue = 3, MinValue = 1)]
        public double MinDynamicOffsetPips { get; set; }

        [Parameter("Max Dynamic Offset Pips", DefaultValue = 20, MinValue = 1)]
        public double MaxDynamicOffsetPips { get; set; }

        [Parameter("Take Profit Pips", DefaultValue = 500, MinValue = 1)]
        public double TakeProfitPips { get; set; }

        [Parameter("Max Spread Pips", DefaultValue = 2, MinValue = 0)]
        public double MaxSpreadPips { get; set; }

        [Parameter("Trailing Trigger Pips", DefaultValue = 20, MinValue = 1)]
        public double TrailingTriggerPips { get; set; }

        [Parameter("Trailing Distance Pips", DefaultValue = 8, MinValue = 1)]
        public double TrailingDistancePips { get; set; }

        [Parameter("Trading Start Time", DefaultValue = "08:00")]
        public string TradingStartTime { get; set; }

        [Parameter("Trading End Time", DefaultValue = "22:00")]
        public string TradingEndTime { get; set; }

        [Parameter("Friday Close Time", DefaultValue = "20:00")]
        public string FridayCloseTime { get; set; }

        [Parameter("Order Direction", DefaultValue = OrderDirectionMode.Both)]
        public OrderDirectionMode OrderDirection { get; set; }

        [Parameter("Label", DefaultValue = "PendingRecenterBot")]
        public string Label { get; set; }

        private DateTime _lastBarTime;
        private AverageTrueRange _atr;

        protected override void OnStart()
        {
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Simple);
            _lastBarTime = Bars.OpenTimes.LastValue;

            UpdatePendingOrders();
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

            ManageOpenPositions();
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
            
            foreach (var o in PendingOrders) o.Cancel();

            var volumeInUnits = GetDynamicVolumeInUnits();
            var offsetPips = GetCurrentOffsetPips();

            var buyPrice = Symbol.Ask + offsetPips * Symbol.PipSize;
            var sellPrice = Symbol.Bid - offsetPips * Symbol.PipSize;

            if (OrderDirection == OrderDirectionMode.Both || OrderDirection == OrderDirectionMode.LongOnly)
                PlaceStopOrder(TradeType.Buy, SymbolName, volumeInUnits, buyPrice, Label,
                   null, TakeProfitPips);
                

            if (OrderDirection == OrderDirectionMode.Both || OrderDirection == OrderDirectionMode.ShortOnly)
                PlaceStopOrder(TradeType.Sell, SymbolName, volumeInUnits, sellPrice, Label, null, 
                    TakeProfitPips);

            Print("Pending aggiornati. Volume={0} units, Offset={1:F1} pips, Direzione={2}, BuyStop={3}, SellStop={4}",
                volumeInUnits, offsetPips, OrderDirection, buyPrice, sellPrice);
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
            //if (!UseDynamicOffset)
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
/*
                if (newStopLossPrice.HasValue &&
                    (!position.StopLoss.HasValue || Math.Abs(position.StopLoss.Value - newStopLossPrice.Value) > Symbol.PipSize / 10))
                {
                    ModifyPosition(position, newStopLossPrice, position.TakeProfit);
                    Print("Posizione aggiornata. SL={0}, Pips={1:F1}", newStopLossPrice.Value, currentProfitPips);
                }
                */
            }
        }
    }
}