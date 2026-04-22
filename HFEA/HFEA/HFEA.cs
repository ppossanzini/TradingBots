using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using System;
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
        [Parameter("Label", DefaultValue = "ETH-HFT")]
        public string PositionSuffix { get; set; }

        [Parameter("Position Prefix", DefaultValue = "HFEA")]
        public string PositionPrefix { get; set; }

        // Crypto trades 24/7 – default finestra aperta tutto il giorno
        [Parameter("Trading Start Time", DefaultValue = "00:00")]
        public string TradingStartTime { get; set; }

        [Parameter("Trading End Time", DefaultValue = "23:59")]
        public string TradingEndTime { get; set; }

        [Parameter("Friday Close Time", DefaultValue = "23:59")]
        public string FridayCloseTime { get; set; }

        [Parameter("Timer Interval (seconds)", DefaultValue = 10)]
        public int TimerInterval { get; set; } = 10;

        // Posizioni losing: TTL breve per HFT (minuti)
        [Parameter("Losing Position TTL (minutes)", DefaultValue = 8)]
        public int TimeToLive { get; set; } = 8;

        #region Market Sizing

        [Parameter("Order Direction", DefaultValue = OrderDirectionMode.LongOnly, Group = "Orders")]
        public OrderDirectionMode OrderDirection { get; set; }

        [Parameter("Min Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01, Group = "Orders")]
        public double MinVolumeLots { get; set; }

        [Parameter("Max Volume (Lots)", DefaultValue = 0.05, MinValue = 0.01, Group = "Orders")]
        public double MaxVolumeLots { get; set; }

        [Parameter("Margin Percent Divider", DefaultValue = 10.0, MinValue = 0.1, Group = "Orders")]
        public double MarginPercentDivider { get; set; }

        [Parameter("Max active long positions", Group = "Orders", DefaultValue = 3, MinValue = 0, Step = 1)]
        public int MaxLongPositions { get; set; }

        [Parameter("Max active short positions", Group = "Orders", DefaultValue = 0, MinValue = 0, Step = 1)]
        public int MaxShortPositions { get; set; }

        #endregion

        #region Pending Positions

        // Spread reale su ETHUSD (dati storici M1): ~10 pips fissi (0.10 USD, pip=0.01)
        [Parameter("Max Spread Pips", DefaultValue = 15, MinValue = 0, Group = "Pending Positions")]
        public double MaxSpreadPips { get; set; }

        [Parameter("Fixed Offset Pips", DefaultValue = 3, MinValue = 1, Group = "Pending Positions")]
        public double FixedOffsetPips { get; set; }

        [Parameter("Limit Range Pips", DefaultValue = 5, MinValue = 0, MaxValue = 200, Group = "Pending Positions")]
        public double LimitRangePips { get; set; }

        // Con spread 10 pip, il TP netto deve essere > spread per guadagnare.
        // TP=20 pip → movimento lordo necessario = 30 pip (spread 10 + TP 20).
        // SL=35 pip → rapporto rischio/rendimento 35:20 compensato dall'alta frequenza.
        [Parameter("StopLoss Pips", DefaultValue = 35, MinValue = 0, Group = "Pending Positions")]
        public double StopLossPips { get; set; }

        [Parameter("TakeProfit Pips", DefaultValue = 20, MinValue = 0, Group = "Pending Positions")]
        public double TakeProfitPips { get; set; }

        [Parameter("Trailing Trigger Pips", DefaultValue = 15, MinValue = 0, Group = "Pending Positions")]
        public double TrailingTriggerPips { get; set; }

        [Parameter("Trailing Distance Pips", DefaultValue = 8, MinValue = 0, Group = "Pending Positions")]
        public double TrailingDistancePips { get; set; }

        [Parameter("Min Distance Pips", DefaultValue = 25, MinValue = 0, Group = "Pending Positions")]
        public double MinDistancePips { get; set; }

        [Parameter("Profit Distance Pips", DefaultValue = 12, MinValue = 0, Group = "Pending Positions")]
        public double ProfitDistancePips { get; set; }

        [Parameter("Min Tick Volume", DefaultValue = 20, MinValue = 0, Group = "Pending Positions")]
        public int MinTickVolume { get; set; }

        #endregion

        #region Trend Filter (EMA + RSI)

        [Parameter("Enable Trend Filter", DefaultValue = true, Group = "Trend Filter")]
        public bool EnableTrendFilter { get; set; }

        [Parameter("EMA Fast Period", DefaultValue = 8, MinValue = 2, Group = "Trend Filter")]
        public int EmaFastPeriod { get; set; }

        [Parameter("EMA Slow Period", DefaultValue = 21, MinValue = 5, Group = "Trend Filter")]
        public int EmaSlowPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 14, MinValue = 2, Group = "Trend Filter")]
        public int RsiPeriod { get; set; }

        // Non comprare se RSI troppo alto (overbought) o troppo basso (momentum assente)
        [Parameter("RSI Min Level (Long entry)", DefaultValue = 40, MinValue = 1, MaxValue = 99, Group = "Trend Filter")]
        public double RsiMinLevel { get; set; }

        [Parameter("RSI Max Level (Long entry)", DefaultValue = 68, MinValue = 1, MaxValue = 99, Group = "Trend Filter")]
        public double RsiMaxLevel { get; set; }

        #endregion

        string Label => $"{PositionPrefix}-{PositionSuffix}";

        private PendingOrder _shortOrder = null;
        private PendingOrder _longOrder = null;
        private Bar _currentBar;

        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;

        private Position[] ShortPositions =>
            Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName && p.TradeType == TradeType.Sell).ToArray();

        private Position[] LongPositions =>
            Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName && p.TradeType == TradeType.Buy).ToArray();

        private bool HasLosingPosition(TradeType tradeType)
        {
            return Positions.Any(p => p.SymbolName == SymbolName && p.Label == Label && p.TradeType == tradeType && p.NetProfit < 0);
        }

        private bool IsFarEnoughFromExistingEntries(TradeType tradeType, double minDistancePips)
        {
            var sameSide = Positions
                .Where(p => p.SymbolName == SymbolName && p.Label == Label && p.TradeType == tradeType)
                .ToArray();

            if (sameSide.Length == 0)
                return true;

            var referencePrice = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            var nearestDistance = sameSide.Min(p => Math.Abs(referencePrice - p.EntryPrice) / Symbol.PipSize);
            return nearestDistance >= minDistancePips;
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

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaFastPeriod);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaSlowPeriod);
            _rsi     = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);

            Positions.Opened += args => SearchForPendingOrders();
            Timer.Start(TimeSpan.FromSeconds(TimerInterval));

            Print($"HFEA ETH-HFT avviato su {SymbolName} | TrendFilter={EnableTrendFilter}");
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
            {
                CloseFridayPositions();
                return;
            }

            _currentBar = Bars.Last(1);

            bool canLong  = (OrderDirection == OrderDirectionMode.Both || OrderDirection == OrderDirectionMode.LongOnly)
                            && LongPositions.Length < MaxLongPositions;
            bool canShort = (OrderDirection == OrderDirectionMode.Both || OrderDirection == OrderDirectionMode.ShortOnly)
                            && ShortPositions.Length < MaxShortPositions;

            CreatePendingOrders(canLong, canShort);
        }

        private void CloseLoosingPositions()
        {
            foreach (var position in Positions.ToArray())
            {
                if (position.SymbolName != SymbolName || position.Label != Label || position.NetProfit > 0) continue;
                if (position.EntryTime.AddMinutes(TimeToLive) < Server.Time)
                {
                    Print($"TTL scaduto – chiudo posizione {position.Id} ({position.TradeType}) P&L={position.NetProfit:F2}");
                    position.Close();
                }
            }
        }

        private void EvaluateTrailingStop()
        {
            foreach (var position in Positions)
            {
                if (position.SymbolName != SymbolName || position.Label != Label) continue;
                if (position.HasTrailingStop) continue;
                if (position.NetProfit < 0 || position.Pips < TrailingTriggerPips) continue;

                var price = position.TradeType switch
                {
                    TradeType.Buy  => Symbol.Bid - TrailingDistancePips * Symbol.PipSize,
                    TradeType.Sell => Symbol.Ask + TrailingDistancePips * Symbol.PipSize,
                    _              => 0d
                };

                position.ModifyStopLossPrice(price);
                position.ModifyTrailingStop(true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveOrders()
        {
            if (_longOrder != null)
            {
                if (ShortPositions.Any(p => p.NetProfit > 0))
                {
                    _longOrder.Cancel();
                    _longOrder = null;
                }
                else
                {
                    // Evita di inseguire il prezzo continuamente: su M1 tende a comprare i massimi locali.
                }
            }

            if (_shortOrder != null)
            {
                if (LongPositions.Any(p => p.NetProfit > 0))
                {
                    _shortOrder.Cancel();
                    _shortOrder = null;
                }
                else
                {
                    // Evita di inseguire il prezzo continuamente: su M1 tende a vendere i minimi locali.
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SearchForPendingOrders()
        {
            _longOrder  = PendingOrders.FirstOrDefault(i => i.SymbolName == SymbolName && i.TradeType == TradeType.Buy  && i.Label == Label);
            _shortOrder = PendingOrders.FirstOrDefault(i => i.SymbolName == SymbolName && i.TradeType == TradeType.Sell && i.Label == Label);
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
                Print($"Spread alto: {spreadPips:F1} pips – skip");
                return;
            }

            // Se TP non copre spread + buffer minimo, il sistema parte con edge negativo.
            if (TakeProfitPips <= spreadPips + 1.0)
            {
                Print($"TP troppo basso per spread attuale: TP={TakeProfitPips:F1} spread={spreadPips:F1} – skip");
                return;
            }

            var volumeInUnits = GetDynamicVolumeInUnits();

            if (canGoLong && _longOrder is null)
            {
                if (EnableTrendFilter && !IsBullishSetup())
                {
                    Print($"Trend filter: no long | EMAf={_emaFast.Result.Last(0):F2} EMAs={_emaSlow.Result.Last(0):F2} RSI={_rsi.Result.Last(0):F1}");
                }
                else
                {
                    if (HasLosingPosition(TradeType.Buy))
                    {
                        Print("Skip long: esiste gia una posizione long in perdita.");
                    }
                    else if (IsFarEnoughFromExistingEntries(TradeType.Buy, MinDistancePips))
                    {
                        PlaceStopLimitOrderAsync(TradeType.Buy, SymbolName, volumeInUnits, GetBuyPrice, LimitRangePips, Label,
                            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips, null, null, null, false, r =>
                            {
                                if (r.IsSuccessful)
                                    _longOrder = r.PendingOrder;
                                else
                                    Print($"Errore ordine LONG: {r.Error}");
                            });
                    }
                    else
                    {
                        Print("Skip long: distanza minima da entry esistente non raggiunta.");
                    }
                }
            }

            if (canGoShort && _shortOrder is null)
            {
                if (EnableTrendFilter && !IsBearishSetup())
                {
                    Print($"Trend filter: no short | EMAf={_emaFast.Result.Last(0):F2} EMAs={_emaSlow.Result.Last(0):F2} RSI={_rsi.Result.Last(0):F1}");
                }
                else
                {
                    if (HasLosingPosition(TradeType.Sell))
                    {
                        Print("Skip short: esiste gia una posizione short in perdita.");
                    }
                    else if (IsFarEnoughFromExistingEntries(TradeType.Sell, MinDistancePips))
                    {
                        PlaceStopLimitOrderAsync(TradeType.Sell, SymbolName, volumeInUnits, GetSellPrice, LimitRangePips, Label,
                            StopLossPips == 0 ? null : StopLossPips, TakeProfitPips, null, null, null, false, r =>
                            {
                                if (r.IsSuccessful)
                                    _shortOrder = r.PendingOrder;
                                else
                                    Print($"Errore ordine SHORT: {r.Error}");
                            });
                    }
                    else
                    {
                        Print("Skip short: distanza minima da entry esistente non raggiunta.");
                    }
                }
            }
        }

        /// <summary>
        /// Setup rialzista: EMA veloce sopra EMA lenta + RSI in zona momentum (non overbought).
        /// </summary>
        private bool IsBullishSetup()
        {
            var emaFastVal = _emaFast.Result.Last(0);
            var emaSlowVal = _emaSlow.Result.Last(0);
            var rsiVal     = _rsi.Result.Last(0);

            return emaFastVal > emaSlowVal
                   && rsiVal >= RsiMinLevel
                   && rsiVal <= RsiMaxLevel;
        }

        private bool IsBearishSetup()
        {
            var emaFastVal = _emaFast.Result.Last(0);
            var emaSlowVal = _emaSlow.Result.Last(0);
            var rsiVal     = _rsi.Result.Last(0);

            return emaFastVal < emaSlowVal
                   && rsiVal >= (100.0 - RsiMaxLevel)
                   && rsiVal <= (100.0 - RsiMinLevel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double GetDynamicVolumeInUnits()
        {
            var freeMarginPercent = Account.Equity > 0 ? (Account.FreeMargin / Account.Equity) * 100.0 : 0.0;
            var normalized = Math.Clamp(freeMarginPercent / MarginPercentDivider, 0.0, 1.0);
            var lots = MinVolumeLots + (MaxVolumeLots - MinVolumeLots) * normalized;
            return Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(lots), RoundingMode.Down);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsWithinTradingWindow()
        {
            var now   = Server.Time.TimeOfDay;
            var start = TimeSpan.Parse(TradingStartTime);
            var end   = TimeSpan.Parse(TradingEndTime);

            if (start <= end)
                return now >= start && now < end;

            return now >= start || now < end;
        }

        private bool IsFridayCloseTimeReached()
        {
            if (Server.Time.DayOfWeek != DayOfWeek.Friday) return false;
            return Server.Time.TimeOfDay >= TimeSpan.Parse(FridayCloseTime);
        }

        private void CloseFridayPositions()
        {
            foreach (var position in Positions.Where(p => p.SymbolName == SymbolName && p.Label == Label).ToArray())
                position.Close();

            foreach (var order in PendingOrders.Where(o => o.SymbolName == SymbolName && o.Label == Label).ToArray())
                order.Cancel();

            Print("Venerdi: posizioni chiuse e pending cancellate.");
        }
    }
}