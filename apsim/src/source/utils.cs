using source.data;
using source.functions;

// ============================================================================
// Utility Functions Module - SWELL Model Core Computational Library
// ============================================================================
// This static class provides all core computational functions used across the
// SWELL model for phenology, carbon exchanges, and data processing.
//
// FUNCTIONAL ORGANIZATION:
//
// 1. **WEATHER DATA PROCESSING**:
//    - hourlyTemperature(): Sinusoidal temperature disaggregation (Campbell 1985)
//    - astronomy(): Solar geometry, day length, extraterrestrial radiation
//    - dayLength(): Standalone day length calculation
//    - PartitionRadiation(): Direct/diffuse radiation partitioning (Erbs et al. 1982)
//
// 2. **PHENOLOGY FORCING FUNCTIONS**:
//    - forcingUnitFunction(): Yan & Hunt (1999) thermal forcing with 3 cardinal temps
//    - photoperiodFunctionInduction(): Sigmoid photoperiod response for dormancy
//    - temperatureFunctionInduction(): Sigmoid temperature response for dormancy
//    - endodormancyRate(): Hourly chilling accumulation (Utah model variant)
//    - ecodormancyRate(): Photothermal forcing with asymptote scaling
//
// 3. **VEGETATION INDEX & CANOPY STRUCTURE**:
//    - estimateVegetationCover(): VI-based fractional cover estimation
//    - estimateLAI(): Two-layer LAI from EVI with temporal continuity
//
// 4. **CARBON EXCHANGE ENVIRONMENTAL MODIFIERS**:
//    - waterStressFunction(): Rolling memory precipitation-ET0 balance
//    - VPDfunction(): Sigmoid vapor pressure deficit response
//    - phenologyFunction(): Logistic aging function during growth
//    - PARGppfunction(): Michaelis-Menten PAR saturation response
//    - temperatureFunction(): Symmetric polynomial temperature response for GPP
//
// 5. **RESPIRATION CALCULATIONS**:
//    - ComputeTscaleReco(): Lloyd-Taylor temperature response with safeguards
//    - gppRecoTreeFunction(): Overstory GPP-dependent respiration with aging
//    - gppRecoUnderFunction(): Understory GPP-dependent respiration
//    - RecoRespirationFunction(): Phenological aging modifier for respiration
//
// 6. **VVVV INTERFACE**:
//    - vvvvInterface class: Main execution wrapper for vvvv visual programming
//    - estimateHourly(): Hourly disaggregation from daily weather inputs
//    - vvvvExecution(): Complete model timestep with phenology and carbon fluxes
//
// CRITICAL CONSTANTS:
// - Solar constant: 4.921 MJ m⁻² h⁻¹ (extraterrestrial radiation)
// - PAR fraction: 50.5% of shortwave radiation
// - Reference temperature (Lloyd-Taylor): 288.15 K (15°C)
// - Temperature offset (Lloyd-Taylor): 227.13 K
// - Latitude validity: -65° to 65° for day length calculations
//
// MATHEMATICAL FOUNDATIONS:
// - Yan & Hunt (1999): Non-linear thermal forcing with asymmetric curves
// - Campbell (1985): Sinusoidal temperature disaggregation
// - Erbs et al. (1982): Direct/diffuse radiation partitioning
// - Lloyd-Taylor: Exponential temperature response for respiration
// - Michaelis-Menten: Hyperbolic saturation kinetics for PAR response
//
// STATE MANAGEMENT:
// Functions are stateless except for vvvvInterface which maintains
// outputT0 and outputT1 for temporal continuity between timesteps.
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Static utility class providing all core computational functions for SWELL model.
    ///
    /// DESIGN PHILOSOPHY:
    /// Pure functions with no side effects (except vvvvInterface state management).
    /// All functions accept explicit parameters and return computed values.
    ///
    /// USAGE PATTERN:
    /// Called by phenology functions (dormancySeason, growingSeason), vegetation
    /// index dynamics (VIdynamics), carbon exchanges (exchanges), and vvvv interface.
    ///
    /// NUMERICAL STABILITY:
    /// Temperature response functions include safeguards against:
    /// - Division by zero
    /// - Exponential overflow
    /// - Invalid logarithms
    /// - Out-of-range inputs
    /// </summary>
    public static class utils
    {
        #region additional weather inputs
        //hourly temperatures for chilling (24 values in a list) (Campbell, 1985)
        public static List<float> hourlyTemperature(input input)
        {
            //empty list
            List<float> hourlyTemperatures = new List<float>();

            //average temperature
            double Tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            //daily range
            double DT = input.airTemperatureMaximum - input.airTemperatureMinimum;
            for (int h = 0; h < 24; h++)
            {
                //14 is set as the hour with maximum temperature
                hourlyTemperatures.Add((float)(Tavg + DT / 2 * Math.Cos(0.2618F * (h - 14))));
            }
            //return the list of hourly temperatures
            return hourlyTemperatures;
        }

        #region astronomy
        public static radData astronomy(input input, bool hourlyTimeStep)
        {
            // --- Costanti ---
            const float solarConstant = 4.921f;     // MJ m-2 h-1 (manteniamo la tua)
            const float DtoR = MathF.PI / 180f;

            int doy = input.date.DayOfYear;
            int doyTomorrow = input.date.AddDays(1).DayOfYear;

            float latRad = input.latitude * DtoR;

            // --- Distanza relativa Terra–Sole ---
            float drToday = 1f + 0.033f * MathF.Cos(2f * MathF.PI * doy / 365f);
            float drTomorrow = 1f + 0.033f * MathF.Cos(2f * MathF.PI * doyTomorrow / 365f);

            // --- Declinazione solare ---
            float declToday = 0.409f * MathF.Sin(2f * MathF.PI * doy / 365f - 1.39f);
            float declTomorrow = 0.409f * MathF.Sin(2f * MathF.PI * doyTomorrow / 365f - 1.39f);

            // --- Angolo orario al tramonto ---
            float wsToday = ComputeSunsetHourAngle(latRad, declToday);
            float wsTomorrow = ComputeSunsetHourAngle(latRad, declTomorrow);

            // --- Lunghezza del giorno (ore) ---
            float dayLengthToday = 24f / MathF.PI * wsToday;
            float dayLengthTomorrow = 24f / MathF.PI * wsTomorrow;

            input.radData.dayLength = dayLengthToday;
            input.radData.dayLengthTomorrow = dayLengthTomorrow;

            // --- Alba e tramonto (oggi) ---
            input.radData.hourSunrise = 12f - dayLengthToday / 2f;
            input.radData.hourSunset = 12f + dayLengthToday / 2f;
            input.radData.hourSunriseTomorrow = 12f - dayLengthTomorrow / 2f;

            // --- Radiazione extraterrestre giornaliera ---
            float ss = MathF.Sin(latRad) * MathF.Sin(declToday);
            float cc = MathF.Cos(latRad) * MathF.Cos(declToday);

            input.radData.etr =
                solarConstant * drToday * 24f / MathF.PI *
                (wsToday * ss + cc * MathF.Sin(wsToday));

            // --- Calcolo orario (opzionale) ---
            if (hourlyTimeStep)
            {
                float dayHours = 0f;

                for (int h = 0; h < 24; h++)
                {
                    // angolo orario (rad)
                    float hourAngle = (h + 0.5f - 12f) * 15f * DtoR;

                    float cosZenith =
                        MathF.Sin(latRad) * MathF.Sin(declToday) +
                        MathF.Cos(latRad) * MathF.Cos(declToday) * MathF.Cos(hourAngle);

                    if (cosZenith > 0f)
                    {
                        input.radData.etrHourly[h] =
                            solarConstant * drToday * cosZenith;

                        dayHours += 1f;
                    }
                    else
                    {
                        input.radData.etrHourly[h] = 0f;
                    }
                }

                // alle alte latitudini il dayLength viene dal conteggio orario
                if (MathF.Abs(input.latitude) >= 65f)
                {
                    input.radData.dayLength = dayHours;
                }
            }

            return input.radData;
        }
        private static float ComputeSunsetHourAngle(float latRad, float decl)
        {
            float x = -MathF.Tan(latRad) * MathF.Tan(decl);

            if (x <= -1f) return MathF.PI;   // sole sempre sopra (24h)
            if (x >= 1f) return 0f;         // sole sempre sotto (0h)

            return MathF.Acos(x);
        }


        public static float dayLength(input input)
        {
            float DtoR = (float)Math.PI / 180;
            float cc;
            float ws;
            float dayHours = 0;

            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                dayHours = 0.13333F / DtoR * ws;
            }
            else
            {
                dayHours = 0;
            }

            return dayHours;
        }

        #endregion

        #endregion

        #region SWELL phenophase specific functions

        #region growth, greendown, decline thermal units
        //this method computes the forcing thermal unit (Yan & Hunt, 1999)
        public static float forcingUnitFunction(input input, float tmin, float topt, float tmax)
        {
            //local output variable
            float forcingRate = 0;

            //average air temperature
            float averageAirTemperature = (input.airTemperatureMaximum +
                input.airTemperatureMinimum) / 2;

            //if average temperature is below minimum or above maximum
            if (averageAirTemperature < tmin || averageAirTemperature > tmax)
            {
                forcingRate = 0;
            }
            else
            {
                //intermediate computations
                float firstTerm = (tmax - averageAirTemperature) / (tmax - topt);
                float secondTerm = (averageAirTemperature - tmin) / (topt - tmin);
                float Exponential = (topt - tmin) / (tmax - topt);

                //compute forcing rate
                forcingRate = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            }
            //assign to output variable
            return forcingRate;
        }
        #endregion

        #region dormancy induction 
        //photoperiod function
        public static float photoperiodFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //local variable to store the output
            float photoperiodFunction = 0;

            //day length is non limiting PT
            if (input.radData.dayLength < parameters.parDormancyInduction.notLimitingPhotoperiod)
            {
                photoperiodFunction = 1;
            }
            else if (input.radData.dayLength > parameters.parDormancyInduction.limitingPhotoperiod)
            {
                photoperiodFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingPhotoperiod + parameters.parDormancyInduction.notLimitingPhotoperiod) * 0.5F;
                float width = parameters.parDormancyInduction.limitingPhotoperiod - parameters.parDormancyInduction.notLimitingPhotoperiod;

                //compute function
                photoperiodFunction = 1 / (1 + (float)Math.Exp(10 / width *
                    ((input.radData.dayLength - midpoint))));

            }
            //return the photoperiod function
            return photoperiodFunction;
        }

        //temperature function
        public static float temperatureFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //average temperature
            float tAverage = (float)(input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;

            //local variable to store the output
            float temperatureFunction = 0;

            if (tAverage <= parameters.parDormancyInduction.notLimitingTemperature)
            {
                temperatureFunction = 1;
            }
            else if (tAverage >= parameters.parDormancyInduction.limitingTemperature)
            {
                temperatureFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingTemperature + parameters.parDormancyInduction.notLimitingTemperature) * .5F;
                float width = (parameters.parDormancyInduction.limitingTemperature - parameters.parDormancyInduction.notLimitingTemperature);
                //compute function
                temperatureFunction = 1 / (1 + (float)Math.Exp(10 / width * (tAverage - midpoint)));

            }
            //return the output
            return temperatureFunction;
        }
        #endregion

        #region endodormancy
        public static float endodormancyRate(input input, parameters parameters, //internal list to store hourly temperatures
            List<float> hourlyTemperatures, out List<float> chillingUnitsList)
        {

            chillingUnitsList = new List<float>();
            //internal variable to store chilling units
            float chillingUnits = 0;

            #region chilling units accumulation
            foreach (var temperature in hourlyTemperatures)
            {
                //when hourly temperature is below the limiting lower temperature or above the limiting upper temperature
                if (temperature < parameters.parEndodormancy.limitingLowerTemperature ||
                    temperature > parameters.parEndodormancy.limitingUpperTemperature)
                {
                    //no chilling units are accumulated 
                    chillingUnits = 0; //not needed, just to be clear
                }
                //when hourly temperature is between the limiting lower temperature
                //and the non limiting lower temperature
                else if (temperature >= parameters.parEndodormancy.limitingLowerTemperature &&
                    temperature < parameters.parEndodormancy.notLimitingLowerTemperature)
                {
                    //compute lag and slope
                    double midpoint = (parameters.parEndodormancy.limitingLowerTemperature +
                        parameters.parEndodormancy.notLimitingLowerTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingLowerTemperature -
                        parameters.parEndodormancy.notLimitingLowerTemperature);

                    //update chilling units
                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / -width * ((temperature - midpoint))));
                }
                //when hourly temperature is between the non limiting lower temperature and the 
                //non limiting upper temperature
                else if (temperature >= parameters.parEndodormancy.notLimitingLowerTemperature &&
                    temperature <= parameters.parEndodormancy.notLimitingUpperTemperature)
                {
                    chillingUnits = 1;
                }
                //when hourly temperature is between the non limiting upper temperature and the
                //limiting upper temperature
                else
                {
                    double midpoint = (parameters.parEndodormancy.limitingUpperTemperature +
                       parameters.parEndodormancy.notLimitingUpperTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingUpperTemperature -
                        parameters.parEndodormancy.notLimitingUpperTemperature);

                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / width * ((temperature - midpoint))));
                }

                chillingUnitsList.Add(chillingUnits);
            }
            #endregion

            //return the output
            return chillingUnitsList.Sum() / 24;
        }
        #endregion

        #region ecodormancy
        public static float ecodormancyRate(input input, float asymptote, parameters parameters)
        {
            //local variable to store the output
            float ecodormancyRate = 0;


            //the slope of the photothermal function depends on day length 
            float ratioPhotoperiod = input.radData.dayLength / parameters.parEcodormancy.notLimitingPhotoperiod;
            if (ratioPhotoperiod > 1)
            {
                ratioPhotoperiod = 1;
            }

            //modify asymptote depending on day length and endodormancy completion
            float asymptoteModifier = ratioPhotoperiod * asymptote;
            float newAsymptote = asymptote + (1 - asymptote) * asymptoteModifier;

            //lag depends on maximum temperature and day length
            float midpoint = parameters.parEcodormancy.notLimitingTemperature * 0.5F +
                (1 - ratioPhotoperiod) * parameters.parEcodormancy.notLimitingTemperature;
            float tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
            float width = parameters.parEcodormancy.notLimitingTemperature * ratioPhotoperiod;

            //compute ecodormancy rate
            ecodormancyRate = newAsymptote /
              (1 + (float)Math.Exp(-10 / width * ((tavg - midpoint)))); ;

            //return the output
            return ecodormancyRate;
        }
        #endregion

        #endregion

        #region exchanges

        public static float estimateVegetationCover(output outputT1, output outputT, parameters parameters)
        {
            float vegetationCover = 0f;
            float vi = outputT1.vi / 100f;

            if (outputT1.phenoCode < 3)
            {
                vegetationCover = 0f;
            }
            else if (outputT1.phenoCode == 3)
            {
                // fase di chiusura canopy
                vegetationCover = outputT1.growthPercentage / 100;
            }
            else if (outputT1.phenoCode == 4 || (outputT1.phenoCode == 5 && outputT1.declinePercentage<50))
            //else if (outputT1.phenoCode >= 4)
            {
                // canopy chiusa → struttura stabile
                vegetationCover = 1;
            }
            else if (outputT1.phenoCode == 5 && outputT1.declinePercentage > parameters.parPhotosynthesis.declineStartSteep*100)
            {
                // senescenza: solo se c'è vera perdita strutturale
                vegetationCover = 1f - ((outputT1.declinePercentage - 50.0f) / 50.0f);

                if (vegetationCover < 0.0)
                    vegetationCover = 0.0f;
            }

            // controlli di sicurezza
            if (vegetationCover < 0f || vi <= parameters.parVegetationIndex.minimumVI)
                vegetationCover = 0f;

            if (vegetationCover > 1f)
                vegetationCover = 1f;

            return vegetationCover;
        }


        //gpp
        public static float waterStressFunction(output outputT1, input input, parameters parameters)
        {
            float waterAvailability = 0;
            float waterStressGPP = 0;

            outputT1.exchanges.PrecipitationMemory.Add(input.precipitation);
            outputT1.exchanges.ET0memory.Add(input.referenceEvapotranspiration);

            // ============================================================
            // SPIN-UP
            // ============================================================
            if (outputT1.exchanges.PrecipitationMemory.Count < (int)parameters.parPhotosynthesis.waterStressDays)
            {
                return (1f);
            }
            else
            {
                ////compute water stress: https://doi.org/10.1016/j.geoderma.2021.115003
               
                // ============================================================
                // Compute water availability using normalized evaporative demand
                // (ET0 - P) / (ET0 + P), rescaled to [0–1]
                // ============================================================

                float et0Sum = outputT1.exchanges.ET0memory.Sum();
                float prec = outputT1.exchanges.PrecipitationMemory.Sum();

                // Numerical safety
                float denom = et0Sum + prec + 1e-6f;

                // Normalized evaporative demand index [-1, +1]
                float I = (et0Sum - prec) / denom;
                I = Math.Clamp(I, -1f, 1f);

                // Rescale to water availability [0, 1]
                // I = -1 → waterAvailability = 1 (no stress)
                // I =  0 → waterAvailability = 0.5
                // I = +1 → waterAvailability = 0 (max stress)
                waterAvailability = 1f - (I + 1f) * 0.5f;

                // Final safety clamp
                waterAvailability = Math.Clamp(waterAvailability, 0f, 1f);


                //compute water stress GPP
                if (waterAvailability >= parameters.parPhotosynthesis.waterStressThreshold)
                {
                    waterStressGPP = 1;
                }
                else
                {
                    waterStressGPP = parameters.parPhotosynthesis.waterStressSensitivity *
                        (waterAvailability - parameters.parPhotosynthesis.waterStressThreshold) + 1;
                }
 
                //remove when the memory effect ends
                if (outputT1.exchanges.ET0memory.Count > (int)parameters.parPhotosynthesis.waterStressDays)
                {
                    outputT1.exchanges.ET0memory.RemoveAt(0);
                    outputT1.exchanges.PrecipitationMemory.RemoveAt(0);
                }

            }

            //set maximum water stress to 0
            waterStressGPP = Math.Clamp(waterStressGPP, 0f, 1f);
           

            return (waterStressGPP);
        }

        public static float temperatureFunction(float temperature, float tmin, float topt, float tmax)
        {
            float tScale = 0;

            if (temperature < tmin || temperature > tmax)
            {
                tScale = 0;
            }
            else
            {
                float numerator = (temperature - tmin) *
                    (temperature - tmax);

                float denominator = numerator -
                    (float)Math.Pow((temperature - topt), 2);

                tScale = numerator / denominator;
            }



            //if average temperature is below minimum or above maximum
            //if (temperature < tmin || temperature > tmax)
            //{
            //    tScale = 0;
            //}
            //else
            //{
            //    //intermediate computations
            //    float firstTerm = (tmax - temperature) / (tmax - topt);
            //    float secondTerm = (temperature - tmin) / (topt - tmin);
            //    float Exponential = (topt - tmin) / (tmax - topt);

            //    //compute forcing rate
            //    tScale = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            //}

            return tScale;
        }

        public static float PARGppfunction(output outputsT1, float par, float halfSaturationValue)
        {
            float parScaleTree = 1 / (1 + (par / halfSaturationValue));

            return parScaleTree;
        }

        public static float phenologyFunction(output o, output o_yest, parameters p)
        {
            // -----------------------------
            // 0. DORMIENZA
            // -----------------------------
            if (o.phenoCode < 3)
                return 0f;

            // --------------------
            // GROWTH, GREENDOWN, DECLINE precoce
            // --------------------
            if (o.phenoCode == 3 ||
                o.phenoCode == 4 ||
                (o.phenoCode == 5 && o.declinePercentage < p.parPhotosynthesis.declineStartSteep))
            {
                return 1f;
            }

            // --------------------
            // DECLINE tardivo: spegnimento a potenza
            // --------------------
            if (o.phenoCode == 5)
            {
                float start = p.parPhotosynthesis.declineStartSteep*100; // es. 40–60 %

                // prima della soglia: nessun effetto
                if (o.declinePercentage <= start)
                    return 1f;

                // rimappa [start–100] → [0–1]
                float d = (o.declinePercentage - start) / (100f - start);
                d = Math.Clamp(d, 0f, 1f);

                // spegnimento a potenza
                float dp = (float)Math.Pow(d,p.parPhotosynthesis.declineSteepness);

                float f = 1f - dp;
                return Math.Clamp(f, 0f, 1f);
            }


            return 1f;
        }


        public static float VPDfunction(float vpd, parameters parameters)
        {
            float vpdMin = parameters.parPhotosynthesis.vpdMin;
            float vpdMax = parameters.parPhotosynthesis.vpdMax;
            float kVPD = parameters.parPhotosynthesis.vpdSensitivity;


            if (vpd < vpdMin)
            {
                return 1f;
            }
            else
            {
                float midpoint = (vpdMax + vpdMin) / 2f;
                float exponent = kVPD * (vpd - midpoint);
                float result = 1f / (1f + (float)Math.Exp(exponent));
                return result;
            }
        }

        public static float ComputeTscaleReco(float temperature, float activatonEnergyParameter, float referenceTemperature)
        {
            float Tref = referenceTemperature + 273.15f;// reference temperature [K]  (15 °C)
            const float T0 = 227.13f;  // temperature offset [K]  (-46.02 °C)

            // --- convert to Kelvin 
            float TK = 273.15f + temperature;

            // --- numerical and physical safeguards ---
            // below -45 °C the Lloyd-Taylor formula becomes unstable
            if (TK <= (T0 + 0.5f))
                return 0f;  // respiration effectively zero (or clamp to minimal value)

            // avoid division by zero or extremely small denominators
            float denom1 = 1/(Tref - T0);
            float denom2 = 1/(TK - T0);

            if (denom1 < 1e-6f || denom2 < 1e-6f)
                return 0f;

            // --- compute exponential term safely ---
            float exponent = activatonEnergyParameter * (denom1 - denom2);

            // clamp exponent to prevent overflow in Math.Exp
            if (exponent > 50f) exponent = 50f;   // e^50 ≈ 3.0e21
            if (exponent < -50f) exponent = -50f; // e^-50 ≈ 1.9e-22

            float Tscale = (float)Math.Exp(exponent);

            // optional: limit scaling to reasonable physiological range
            if (Tscale > 10f) Tscale = 10f;
            if (Tscale < 0f) Tscale = 0f;


            return Tscale;
        }

        public static float RespirationAgeScaling(output o, parameters p)
        {
            float result = 0f;

            if (o.phenoCode == 3)
            {

                float x = Math.Clamp(o.growthPercentage / 100f, 0f, 1f);

                float boost = p.parRespiration.respirationYoungBoost; // es. 1.5–2
                float pshape = p.parRespiration.respirationYoungPow;  // es. 2–4

                return 1f + (boost - 1f) * (float)Math.Pow(1f - x, pshape);
            }
            else if (o.phenoCode == 4)
            {
                return 1;
            }
            else if (o.phenoCode == 5) // foglie senescenti
            {
                float x = Math.Clamp(o.declinePercentage / 100f, 0f, 1f);

                float boost = p.parRespiration.respirationSenescenceBoost; // meglio separarlo
                float pshape = p.parRespiration.respirationSenescencePow;

                // basso all'inizio, aumenta con la senescenza
                return 1f + (boost - 1f) * (float)Math.Pow(x, pshape);
            }
            return result;
        }



        #endregion
    }

    #region vvvv execution interface
    public class vvvvInterface
    {
        //initialize the SWELL phenology classes with functions
        dormancySeason dormancy = new dormancySeason();
        growingSeason growing = new growingSeason();
        VIdynamics VIdynamics = new VIdynamics();
        source.functions.exchanges exchanges = new source.functions.exchanges();
        //initialize the outputT1
        output outputT0 = new output();
        output outputT1 = new output();

        //this method contains the logic for the execution in vvvv
        public output vvvvExecution(input input, parameters parameters)
        {

            input = estimateHourly(input);

            //pass values from the previous day
            outputT0 = outputT1;
            outputT1 = new output();

            //call the functions
            //dormancy season
            dormancy.induction(input, parameters, outputT0, outputT1);
            dormancy.endodormancy(input, parameters, outputT0, outputT1);
            dormancy.ecodormancy(input, parameters, outputT0, outputT1);
            //growing season
            growing.growthRate(input, parameters, outputT0, outputT1);
            growing.greendownRate(input, parameters, outputT0, outputT1);
            growing.declineRate(input, parameters, outputT0, outputT1);
            //NDVI dynamics
            VIdynamics.ndviNormalized(input, parameters, outputT0, outputT1);
            exchanges.VPRM(input, parameters, outputT0, outputT1);

            outputT1.weather.date = input.date;


            return outputT1;
        }

        #region private methods
        public input estimateHourly(input inputDaily)
        {
            input hourlyData = inputDaily;

            float avgT = (inputDaily.airTemperatureMaximum + inputDaily.airTemperatureMinimum) / 2;
            float dailyRange = inputDaily.airTemperatureMaximum - inputDaily.airTemperatureMinimum;
            float dewPoint = Math.Clamp(inputDaily.dewPointTemperature, inputDaily.airTemperatureMinimum - 5,
                inputDaily.airTemperatureMaximum);
            float rain = inputDaily.precipitation;

            for (int h = 0; h < 24; h++)
            {
                // Temperature Estimate
                float hourlyT = (float)(avgT + dailyRange / 2 * Math.Cos(0.2618f * (h - 15)));
                hourlyData.airTemperatureH[h] = hourlyT;

                // Relative Humidity Estimate            
                float es = 0.6108f * (float)Math.Exp((17.27f * hourlyT) / (237.3F + hourlyT));
                float ea = 0.6108f * (float)Math.Exp((17.27F * dewPoint) / (237.3F + dewPoint));
                float rh_hour = ea / es * 100;
                hourlyData.relativeHumidityH[h] = Math.Clamp(rh_hour, 0f, 100f);

                // Precipitation
                hourlyData.precipitationH[h] = rain / 24;

                // evenly distribute or use sinusoidal pattern
                inputDaily.radData = dayLength(inputDaily, inputDaily.airTemperatureMaximum, inputDaily.airTemperatureMinimum);

                //TODO: CONVERT FROM MJ
                hourlyData.solarRadiationH[h] = inputDaily.radData.gsrHourly[h] * 1269.44F;

                // Wind Speed ?

                // VPD
                hourlyData.vaporPressureDeficitH[h] = vpd(hourlyData, h);

                // ET₀
                hourlyData.referenceET0H[h] = referenceEvapotranspiration(inputDaily, hourlyData, h);
            }

            return hourlyData;
        }
        #endregion


        public radData dayLength(input input, float Tmax, float Tmin)
        {
            radData _radData = input.radData;
            _radData.gsr = input.PAR;
            // Constants
            float solarConstant = 4.921f; // MJ m⁻² h⁻¹ (derived from 1367 W/m²)
            float DtoR = (float)Math.PI / 180f;

            int doy = input.date.DayOfYear;
            float latitudeRad = input.latitude * DtoR;

            // Solar geometry
            float inverseEarthSun = 1f + 0.0334f * (float)Math.Cos(0.01721f * doy - 0.0552f); // Earth-Sun distance correction
            float solarDeclination = 0.4093f * (float)Math.Sin((6.284f / 365f) * (284 + doy)); // radians
            float sinDec = (float)Math.Sin(solarDeclination);
            float cosDec = (float)Math.Cos(solarDeclination);
            float sinLat = (float)Math.Sin(latitudeRad);
            float cosLat = (float)Math.Cos(latitudeRad);
            float ss = sinDec * sinLat;
            float cc = cosDec * cosLat;
            float ws = (float)Math.Acos(-Math.Tan(solarDeclination) * Math.Tan(latitudeRad)); // sunset hour angle (radians)

            // Initialize arrays
            float[] HourAngleHourly = new float[24];
            float[] SolarElevationHourly = new float[24];
            float[] ExtraterrestrialRadiationHourly = new float[24];
            float[] Distribution = new float[24];
            _radData.etrHourly = new float[24];
            _radData.gsrHourly = new float[24];

            float dayHours = 0f;
            _radData.etr = 0f;

            // Loop through 24 hours to calculate hourly ETR
            for (int h = 0; h < 24; h++)
            {
                HourAngleHourly[h] = 15f * (h - 12f); // degrees
                float cosQHadj = ss + cc * (float)Math.Cos(DtoR * HourAngleHourly[h]);
                cosQHadj = Math.Clamp(cosQHadj, -1f, 1f);

                SolarElevationHourly[h] = (cosQHadj > 0f) ? (float)Math.Asin(cosQHadj) : 0f;

                float hourlyETR = solarConstant * inverseEarthSun * (float)Math.Sin(SolarElevationHourly[h]);
                hourlyETR = Math.Max(0f, hourlyETR); // no negative values

                _radData.etrHourly[h] = hourlyETR;
                _radData.etr += hourlyETR;

                if (hourlyETR > 0f) dayHours++;
            }

            // Compute analytical ETR and day length if within valid latitudes
            if (input.latitude < 65 && input.latitude > -65)
            {
                float dayLength = 0.13333f / DtoR * ws; // day length (hours)
                float Ra_analytical = (24f / (float)Math.PI) * solarConstant * inverseEarthSun *
                    (ws * ss + cc * (float)Math.Sin(ws));

                _radData.dayLength = dayLength;
                _radData.etr = Ra_analytical; // use analytical instead of loop? choose one
            }
            else
            {
                _radData.dayLength = dayHours;
            }

            // Redistribute GSR over 24 hours using ETR fractions
            for (int h = 0; h < 24; h++)
            {
                Distribution[h] = (_radData.etr > 0f) ? _radData.etrHourly[h] / _radData.etr : 0f;
                _radData.gsrHourly[h] = Distribution[h] * _radData.gsr;
            }

            // Estimate sunrise and sunset
            _radData.hourSunrise = 12f - _radData.dayLength / 2f;
            _radData.hourSunset = 12f + _radData.dayLength / 2f;

            return _radData;
        }

        public float vpd(input Input, int hour)
        {
            //VPD calculation method by Monteith and Unsworth (1990)

            float SVP = 0.6108f * (float)Math.Exp((17.27f * Input.airTemperatureH[hour]) / (Input.airTemperatureH[hour] + 237.3f)); // in kPa
            float AVP = SVP * Input.relativeHumidityH[hour] / 100f;
            float VPD = SVP - AVP;
            return VPD;
        }

        public float referenceEvapotranspiration(input dailyInput, input Input, int hour)
        {
            // Convert Rs from W/m² to MJ/m²/h
            double Rs_MJ = dailyInput.PAR;

            // Given coefficients
            double c0 = 0.1396;
            double c1 = -3.019e-3;
            double c2 = -1.2109e-3;
            double c3 = 1.626e-5;
            double c4 = 8.224e-5;
            double c5 = 0.1842;
            double c6 = -1.095e-3;
            double c7 = 3.655e-3;
            double c8 = -4.442e-3;

            // Compute ET0 using the equation
            double ET0 = c0
                         + c1 * Input.relativeHumidityH[hour]
                         + c2 * Input.airTemperatureH[hour]
                         + c3 * Math.Pow(Input.relativeHumidityH[hour], 2)
                         + c4 * Math.Pow(Input.airTemperatureH[hour], 2)
                         + c5 * Rs_MJ
                         + 0.5 * Rs_MJ * (c6 * Input.relativeHumidityH[hour]
                         + c7 * Input.airTemperatureH[hour])
                         + c8 * Math.Pow(Rs_MJ, 2);

            if (ET0 < 0) { ET0 = 0; }
            ;
            return (float)ET0;

        }

    }
    #endregion

}