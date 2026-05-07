using source.data;
using System;
using System.ComponentModel.Design;
using System.Security.Cryptography.X509Certificates;

// ============================================================================
// Carbon Exchanges Module - VPRM Implementation for SWELL Model
// ============================================================================
// This module implements the Vegetation Photosynthesis Respiration Model (VPRM)
// adapted for two-layer canopy architecture with hourly carbon flux calculations.
//
// KEY ARCHITECTURE FEATURES:
//
// 1. TWO-LAYER CANOPY STRUCTURE:
//    - Overstory: Phenology-dependent (active phenoCode ≥3), deciduous behavior
//    - Understory: Always active, evergreen/herbaceous behavior
//    - Separate LAI, EVI, light interception, and temperature responses
//
// 2. LIGHT PARTITIONING (pre-computed outside hourly loop):
//    - Direct/diffuse radiation partitioning (Erbs et al. 1982)
//    - Beer-Lambert light extinction through overstory canopy
//    - Transmitted light to understory (separate direct/diffuse paths)
//    - Light interception coefficients computed once per day
//
// 3. GPP CALCULATION (hourly, minimum limiting factor approach):
//    GPP = QuantumYield × Tscale × min(Wscale, min(VPD, PARscale)) × PAR × Phenology × EVI
//    - Temperature scaler: Symmetric polynomial (zero outside Tmin-Tmax)
//    - Leaf temperature simplified to air temperature (no energy balance)
//    - Understory has temperature shift via pixelTemperatureShift parameter
//    - Overstory includes phenology scaler (logistic function of growth percentage)
//    - Understory has no phenology modulation (always active)
//
// 4. RECO CALCULATION (hourly, three additive components):
//    - recoOver: Overstory autotrophic respiration (GPP-dependent with aging)
//    - recoUnder: Understory autotrophic respiration (GPP-dependent)
//    - recoHetero: Heterotrophic soil respiration (temperature and water dependent)
//    - Each uses Lloyd-Taylor temperature response with separate activation energies
//    - Exponential moving average smoothing prevents discontinuities
//    - State variables lastRecoTree/lastRecoUnder persist across hours and days
//
// 5. CARBON FLUX OUTPUTS:
//    - Hourly arrays (24 elements): GPP (over/under/total), RECO (over/under/hetero/total), NEE
//    - Daily sums: gppDaily, recoDaily, neeDaily (µmol CO₂ m⁻² d⁻¹)
//    - Supporting hourly arrays: Temperature scales, PAR scales, water stress, VPD scale, phenology scale
//
// UNIT CONVERSIONS:
// - Input solar radiation: W/m² → MJ/m²/h (× 277.78)
// - PAR conversion: Shortwave → PAR (× 0.505 × 4.57 = × 2.31 µmol/J)
// - Output units: µmol CO₂ m⁻² s⁻¹ (hourly), µmol CO₂ m⁻² d⁻¹ (daily sums)
//
// CRITICAL DEPENDENCIES:
// - Phenology: phenoCode controls overstory activity (≥3 = active)
// - VI dynamics: EVI feeds LAI estimates and direct GPP calculation
// - Water stress: Rolling memory (precipitation - ET0) over waterStressDays
// - Respiration smoothing: Prevents unrealistic discontinuities from rapid GPP changes
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Carbon exchanges computation module implementing VPRM for two-layer canopy.
    /// Calculates hourly GPP, RECO, and NEE with phenology-dependent overstory
    /// and always-active understory layers.
    ///
    /// BIOLOGICAL REALISM:
    /// - Overstory represents deciduous trees with seasonal phenology
    /// - Understory represents evergreen/herbaceous layer with year-round activity
    /// - Light competition determines productivity partitioning
    /// - Temperature, water, VPD, and phenology modulate carbon uptake
    /// - Respiration components track autotrophic and heterotrophic processes
    ///
    /// IMPLEMENTATION:
    /// Main method VPRM() orchestrates hourly calculations with pre-computed
    /// daily light interception coefficients for computational efficiency.
    /// Helper methods compute GPP (2 layers) and RECO (3 components).
    /// </summary>
    public class exchanges
    {
        // ============================================================
        // AUTOTROPHIC RESPIRATION STATE (PERSISTENT)
        // ============================================================

        public float SfOver { get; set; } = 0f;
        public float SsOver { get; set; } = 0f;
        public float SfUnder { get; set; } = 0f;
        public float SsUnder { get; set; } = 0f;

        /// <summary>
        /// Computes hourly carbon fluxes using Vegetation Photosynthesis Respiration Model (VPRM).
        ///
        /// COMPUTATIONAL WORKFLOW:
        /// 1. Pre-computation (outside hourly loop):
        ///    - Estimate vegetation cover from VI thresholds
        ///    - Compute LAI and EVI for both canopy layers (uses outputT for temporal continuity)
        ///    - Calculate extraterrestrial radiation (top-of-atmosphere solar flux)
        ///    - Compute light extinction coefficients for overstory and understory
        ///    - Determine overstory gap probabilities (direct and diffuse)
        ///
        /// 2. Hourly loop (h = 0 to 23):
        ///    a. RADIATION PARTITIONING:
        ///       - Partition incoming shortwave into direct/diffuse components
        ///       - Convert shortwave to PAR (photosynthetically active radiation)
        ///       - Calculate absorbed PAR for overstory and understory
        ///       - Compute PAR scaling factors for GPP functions
        ///
        ///    b. TEMPERATURE EFFECTS:
        ///       - Overstory leaf temperature = air temperature (simplified)
        ///       - Understory leaf temperature = air temperature (simplified)
        ///       - Compute temperature scalers using symmetric polynomial function
        ///       - Understory uses shifted optimum temperature
        ///
        ///    c. ENVIRONMENTAL MODIFIERS:
        ///       - Water stress: Rolling memory of (precipitation - ET0)
        ///       - Phenology scale: Logistic function of growth percentage
        ///       - VPD scale: Sigmoid response function
        ///
        ///    d. GPP CALCULATION:
        ///       - Overstory GPP (if phenoCode ≥3): With phenology scaling
        ///       - Understory GPP: Always active, no phenology scaling
        ///       - Total GPP = gppOver + gppUnder
        ///
        ///    e. RECO CALCULATION:
        ///       - Compute Lloyd-Taylor temperature responses (3 components)
        ///       - Overstory autotrophic: GPP-dependent with aging function
        ///       - Understory autotrophic: GPP-dependent
        ///       - Heterotrophic soil: Temperature and water dependent
        ///       - Apply exponential smoothing to prevent discontinuities
        ///       - Total RECO = recoOver + recoUnder + recoHetero
        ///
        ///    f. NEE CALCULATION:
        ///       - NEE = RECO - GPP (positive = carbon source, negative = carbon sink)
        ///
        /// 3. Post-processing:
        ///    - Sum hourly fluxes to daily totals (gppDaily, recoDaily, neeDaily)
        ///
        /// CRITICAL IMPLEMENTATION DETAILS:
        /// - LAI estimation uses outputT (previous timestep) for temporal continuity in understory EVI
        /// - Light interception computed once per day (not in hourly loop) for efficiency
        /// - Overstory only active when phenoCode ≥3 (growth, greendown, decline phases)
        /// - Minimum limiting factor approach: min(Wscale, min(VPD, PARscale)) for co-limitation
        /// - Respiration smoothing state (lastRecoTree, lastRecoUnder) persists across hours and days
        ///
        /// PARAMETERS USED:
        /// - parPhotosynthesis: Quantum yields, cardinal temperatures, PAR half-saturation,
        ///   VPD response, water stress thresholds, light extinction, phenology scaling
        /// - parRespiration: Activation energies, reference rates, GPP response coefficients,
        ///   aging factor, smoothing factor
        /// - parVegetationIndex: Min/max VI for vegetation cover estimation
        ///
        /// OUTPUT POPULATION:
        /// - exchanges.LAIoverstory, LAIunderstory: Leaf area indices (m² m⁻²)
        /// - exchanges.EVIoverstory, EVIunderstory: Enhanced vegetation indices
        /// - exchanges.vegetationCover: Fraction of ground covered (0-1)
        /// - exchanges.gpp[h], gppOver[h], gppUnder[h]: Hourly GPP (µmol CO₂ m⁻² s⁻¹)
        /// - exchanges.reco[h], recoOver[h], recoUnder[h], recoHetero[h]: Hourly RECO
        /// - exchanges.nee[h]: Hourly NEE (µmol CO₂ m⁻² s⁻¹)
        /// - exchanges.gppDaily, recoDaily, neeDaily: Daily sums (µmol CO₂ m⁻² d⁻¹)
        /// - Supporting hourly arrays: PARdirect, PARdiffuse, TscaleOver, TscaleUnder,
        ///   WaterStress, vpdScale, phenologyScale, PARscaleOverstory, PARscaleUnderstory
        /// </summary>
        /// <param name="input">Daily weather inputs with hourly disaggregated arrays</param>
        /// <param name="parameters">Photosynthesis and respiration parameters</param>
        /// <param name="output">Previous timestep output (T-1) for LAI temporal continuity and respiration smoothing</param>
        /// <param name="outputT1">Current timestep output (T) to receive carbon flux calculations</param>
        public void VPRM(input input, parameters parameters, output output, output outputT1)
        {
            //estimate vegetation cover
            outputT1.exchanges.vegetationCover = utils.estimateVegetationCover(outputT1, output, parameters);

            #region Water availability effects
            //compute water stress
            var Wscale = utils.waterStressFunction(outputT1, input, parameters);
            #endregion
            
            //compute phenology effect
            float PhenologyScale = utils.phenologyFunction(outputT1, output, parameters);

            //hourly loop
            for (int h = 0; h < 24; h++)
            {
                #region Gross primary production

                #region temperature effects

                float temperature = input.airTemperatureH[h];

                //temperature scale factor (photosynthesis)
                float TR = utils.temperatureFunction(temperature, parameters.parPhotosynthesis.minimumTemperature,
                       parameters.parPhotosynthesis.optimumTemperature, parameters.parPhotosynthesis.maximumTemperature);

                outputT1.exchanges.temperatureScale.Add(TR);
                #endregion

                #region Water availability effect
                outputT1.exchanges.Wscale.Add(Wscale);
                #endregion

                #region Phenology effect
                if (input.simulationSettings.configuration.Contains("pheno"))
                {
                    outputT1.exchanges.phenologyScale = PhenologyScale;
                }
                else
                {
                    outputT1.exchanges.phenologyScale = 1f;
                }
                #endregion

                #region VPD effect
                //compute VPD effect
                float VPDscale = utils.VPDfunction(input.vaporPressureDeficitH[h] / 10, parameters); 
                outputT1.exchanges.vpdScale.Add(VPDscale);
                #endregion

                #region PAR effect
                // PAR modifier for GPP
                float SW_IN =0;
                if (input.solarRadiationH[h] > 0)
                {
                    SW_IN = input.solarRadiationH[h] * 544f;
                }
                float halfSaturation = parameters.parPhotosynthesis.halfSaturationTree;

                if (input.simulationSettings.configuration.Contains("pheno"))
                {
                    halfSaturation = parameters.parPhotosynthesis.halfSaturationTree * outputT1.exchanges.vegetationCover +
                   parameters.parPhotosynthesis.halfSaturationUnder * (1 - outputT1.exchanges.vegetationCover);
                }

                outputT1.exchanges.halfSaturation.Add(halfSaturation);

                float PARscale = utils.PARGppfunction(outputT1, SW_IN, halfSaturation);
                if (PARscale > 1) PARscale = 1;
                // add variables to the list
                outputT1.exchanges.PARscale.Add(PARscale);
                #endregion

                //metabolic activation
                float metActPhoto = 0;  
                #region GPP estimation

                float QY = parameters.parPhotosynthesis.maximumQuantumYieldOver;

                if(input.simulationSettings.configuration.Contains("pheno"))
                {
                    QY =(parameters.parPhotosynthesis.maximumQuantumYieldOver *
                        outputT1.exchanges.phenologyScale * outputT1.exchanges.vegetationCover) +
                    parameters.parPhotosynthesis.maximumQuantumYieldUnder * (1 - outputT1.exchanges.vegetationCover);
                }

                //effect of circadian rhythm
                float circadianActivation = 0f;
                if (input.simulationSettings.configuration == "pheno_circ")
                {
                    metActPhoto = metabolicActivation(input, h, parameters.parPhotosynthesis.circadianDayShiftHours);
                    circadianActivation = parameters.parPhotosynthesis.circadianActivationPhoto;
                }

                outputT1.exchanges.metActivationPhoto.Add(metActPhoto);
                //modified quantum yield
                float QYvar = QY + QY * circadianActivation * metActPhoto;
                //store values
                outputT1.exchanges.QY.Add(QY);

                //GPP estimation
                float GPP = estimateGPP(circadianActivation, QYvar, PARscale, VPDscale, TR, SW_IN,  Wscale,  
                    metActPhoto, outputT1.vi/100);

              
                outputT1.exchanges.GPP.Add(GPP);

                #endregion

                #endregion

                #region Respiration 

                #region temperature response
                // Lloyd–Taylor temperature scaling 
                float recoTResponse = utils.ComputeTscaleReco(temperature, parameters.parRespiration.activationEnergyParameter, 
                    parameters.parRespiration.Tref);
                outputT1.exchanges.TscaleReco.Add(recoTResponse);

                // Heterotrophic respiration with basal fraction
                float recoTandWS =   parameters.parRespiration.referenceRespiration * recoTResponse * Wscale;
                outputT1.exchanges.recoTandWS.Add(recoTandWS);
                #endregion

                #region GPP response
                float ageScale = 1;

                if (input.simulationSettings.configuration.Contains("pheno"))
                {
                  ageScale =  utils.RespirationAgeScaling(outputT1, parameters);
                }
                outputT1.exchanges.PhenologyscaleReco.Add(ageScale);

                // Young tissues → lower Yg → more respiration
                float vegCover = outputT1.exchanges.vegetationCover;
                float ageScaleEff = 1f;

                // applica ageScale SOLO se esiste overstory
                if (vegCover > 0f)
                {
                    ageScaleEff = ageScale;
                }

                float cue = parameters.parRespiration.carbonUseEfficiencyOver;

                if (input.simulationSettings.configuration.Contains("pheno"))
                {
                    cue = vegCover * (parameters.parRespiration.carbonUseEfficiencyOver / ageScaleEff) +
                    (1f - vegCover) * parameters.parRespiration.carbonUseEfficiencyUnder;
                }

                // sicurezza fisica
                cue = Math.Clamp(cue, 0.05f, 0.95f);

                float metActReco = 0;
                if(input.simulationSettings.configuration == "pheno_circ")
                {
                    metActReco = metabolicActivation(input, h,  parameters.parPhotosynthesis.circadianDayShiftHours);
                    cue = cue + cue * parameters.parRespiration.circadianActivationReco * (1 - metActReco);
                }
                outputT1.exchanges.metActivationReco.Add(metActReco);
                outputT1.exchanges.CUE.Add(cue);

                var recoGPP = StepTwoPool(outputT1.exchanges.fastPool, outputT1.exchanges.slowPool, GPP,
                    parameters.parRespiration.fractionGppToFastPool,
                    parameters.parRespiration.fastPoolTurnover,
                    parameters.parRespiration.slowPoolTurnover,
                    cue);
                outputT1.exchanges.recoGPP.Add(recoGPP.Ra);
                // aggiorna stato persistente
                outputT1.exchanges.fastPool = recoGPP.Sf;
                outputT1.exchanges.slowPool = recoGPP.Ss;
                outputT1.exchanges.fastPoolSeries.Add(recoGPP.Sf);
                outputT1.exchanges.slowPoolSeries.Add(recoGPP.Ss);

              
                outputT1.exchanges.recoGPP.Add(recoGPP.Ra);
                // aggiorna stato persistente
                outputT1.exchanges.fastPool = recoGPP.Sf;
                outputT1.exchanges.slowPool = recoGPP.Ss;
                outputT1.exchanges.fastPoolSeries.Add(recoGPP.Sf);
                outputT1.exchanges.slowPoolSeries.Add(recoGPP.Ss);
                #endregion

                float RECO = (recoTandWS + recoGPP.Ra);

                outputT1.exchanges.RECO.Add(RECO);
                #endregion

                //Net Ecosystem Exchange
                outputT1.exchanges.NEE.Add(RECO-GPP);

            }

            // --- Compute daily sums ---
            //outputT1.exchanges.gppDaily = outputT1.exchanges.gpp.Sum();
            //outputT1.exchanges.recoDaily = outputT1.exchanges.reco.Sum();
            //outputT1.exchanges.neeDaily = outputT1.exchanges.recoDaily - outputT1.exchanges.gppDaily;
        }

        #region GPP functions

        private float estimateGPP(float circadianActivation, float quantumYield, float PARscale, float VPDmodifier, float Tscale, float par, 
           float Wscale,  float metActivation, float evi)
        {
            float limitingFactor = Math.Min(Wscale, VPDmodifier);

            float gpp = quantumYield * evi * Tscale * PARscale * limitingFactor * par;

            return gpp;
        }

        


        #endregion

        #region Respiration functions
        private float metabolicActivationOld(input input, float hour,
                                   float daySkew = 0f,
                                   float dayShiftHours = 0f)
        {
            float sunRise = input.radData.hourSunrise; // es. 6.5
            float sunSet = input.radData.hourSunset;  // es. 19.0

            // 🔥 SHIFT GLOBALE (garantisce continuità)
            float hourFun = ((hour + dayShiftHours) % 24f + 24f) % 24f;

            // durata giorno e notte
            float dayLength = sunSet - sunRise;
            float nightLength = 24f - dayLength;

            float phase;

            if (hourFun >= sunSet || hourFun < sunRise)
            {
                // ===== NOTTE: sunset → sunrise (0 → π)
                float h = hourFun < sunRise ? hourFun + 24f : hourFun;
                phase = (h - sunSet) / nightLength * MathF.PI;

                // notte: coseno diretto
                float activationNight = -MathF.Cos(phase);
                return Math.Clamp(activationNight, -1f, 1f);
            }
            else
            {
                // ===== GIORNO: sunrise → sunset (π → 2π)
                phase = MathF.PI + (hourFun - sunRise) / dayLength * MathF.PI;

                // posizione normalizzata nel giorno [0,1]
                float u = (phase - MathF.PI) / MathF.PI;

                // 🔹 skew smooth (asimmetria AM vs PM)
                if (MathF.Abs(daySkew) > 1e-6f)
                {
                    float a = MathF.Exp(daySkew);
                    float b = MathF.Exp(-daySkew);

                    float ua = MathF.Pow(u, a);
                    float ub = MathF.Pow(1f - u, b);
                    u = ua / (ua + ub);
                }

                float dayPhase = MathF.PI + u * MathF.PI;
                float activationDay = -MathF.Cos(dayPhase);

                return Math.Clamp(activationDay, -1f, 1f);
            }
        }


        private float metabolicActivation(input input,
                                        float hour,
                                        float shiftHours = 0f)
        {
            float sunRise = input.radData.hourSunrise; // es. 6.5
            float sunSet = input.radData.hourSunset;  // es. 19.0

            // 🔥 SHIFT GLOBALE (nuovo)
            float hourFun = ((hour + shiftHours) % 24f + 24f) % 24f;

            // durata giorno e notte
            float dayLength = sunSet - sunRise;
            float nightLength = 24f - dayLength;

            float phase;

            if (hourFun >= sunSet || hourFun < sunRise)
            {
                // NOTTE: sunset → sunrise (0 → π)
                float h = hourFun < sunRise ? hourFun + 24f : hourFun;
                phase = (h - sunSet) / nightLength * MathF.PI;
            }
            else
            {
                // GIORNO: sunrise → sunset (π → 2π)
                phase = MathF.PI + (hourFun - sunRise) / dayLength * MathF.PI;
            }

            // stessa formula
            float activation = -MathF.Cos(phase);

            return Math.Clamp(activation, -1f, 1f);
        }



        private (float Sf, float Ss, float Ra) StepTwoPool(
     float Sf,
     float Ss,
     float gppEff,
     float fracFast,
     float kfRef,
     float ksRef,
     float Yg
 )
        {
 
            float kf = kfRef;
            float ks = ksRef;

            // --------------------------------------------------
            // 2. Pool dynamics (Euler forward, dt = 1 h)
            // --------------------------------------------------
            float dSs = (1f - fracFast) * gppEff - ks * Ss;
            float dSf = fracFast * gppEff - kf * Sf + ks * Ss;

            Ss = Math.Max(0f, Ss + dSs);
            Sf = Math.Max(0f, Sf + dSf);

            // --------------------------------------------------
            // 3. Autotrophic respiration
            // --------------------------------------------------
            // U = substrate utilization from fast pool
            float U = kf * Sf;

            // Ra = fraction (1 - Yg) of utilized substrate
            float Ra = (1f - Yg) * U;

            return (Sf, Ss, Ra);
        }

        #endregion
    }

}
