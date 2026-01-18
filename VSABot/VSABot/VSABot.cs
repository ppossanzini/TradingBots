using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class VSALongOnlyBot : Robot
    {
        [Parameter("Quantity (Lots)", DefaultValue = 0.1, MinValue = 0.01, Step = 0.01)]
        public double Quantity { get; set; }

        [Parameter("Volume Lookback (MA)", DefaultValue = 20)]
        public int VolumeLookback { get; set; }

        [Parameter("Volume Multiplier (Climax)", DefaultValue = 2.0)]
        public double VolumeMultiplier { get; set; }

        [Parameter("Stop Loss (Pips)", DefaultValue = 20)]
        public double StopLoss { get; set; }

        [Parameter("Take Profit (Pips)", DefaultValue = 40)]
        public double TakeProfit { get; set; }

        private AverageTrueRange _atr;

        protected override void OnStart()
        {
            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            Print("VSA Accumulator Bot iniziato. Solo operazioni Long.");
        }

        protected override void OnBar()
        {
            // Analizziamo la candela appena chiusa (index 1)
            int index = Bars.Count - 2;

            double currentVolume = Bars.TickVolumes[index];
            double avgVolume = Bars.TickVolumes.TakeLast(VolumeLookback).Average();
            
            double spread = Bars.HighPrices[index] - Bars.LowPrices[index];
            double body = Math.Abs(Bars.ClosePrices[index] - Bars.OpenPrices[index]);
            double lowerWick = Math.Min(Bars.OpenPrices[index], Bars.ClosePrices[index]) - Bars.LowPrices[index];

            // --- LOGICA VSA PER IL LONG ---
            
            // 1. Condizione di "Selling Climax" o "Stopping Volume"
            // Il volume deve essere significativamente superiore alla media
            bool isHighVolume = currentVolume > (avgVolume * VolumeMultiplier);

            // 2. Analisi della Price Action (Assorbimento)
            // Cerchiamo una candela dove i venditori hanno spinto (Lower Wick lunga) 
            // ma il prezzo ha recuperato, o il range è piccolo rispetto al volume (Sforzo senza Risultato)
            bool hasBuyingPressure = lowerWick > (spread * 0.5) || (spread < (Symbol.PipSize * 10) && isHighVolume);

            // 3. Filtro di Direzione (Opzionale: operiamo solo se il trend precedente era ribassista)
            bool wasDownTrend = Bars.ClosePrices[index] < Bars.ClosePrices[index - 3];

            if (isHighVolume && hasBuyingPressure && wasDownTrend)
            {
                if (Positions.Count == 0)
                {
                    OpenLongPosition();
                }
            }
        }

        private void OpenLongPosition()
        {
            var targetRaw = Quantity;
            var volumeInUnits = Symbol.QuantityToVolumeInUnits(targetRaw);
            
            ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, "VSA_Long", null, TakeProfit);
            
            Print("Segnale VSA rilevato: Volume anomalo con assorbimento. Apertura Long.");
        }
    }
}