using source.data;
using source.functions;

namespace source.functions
{
    //this static class contains the utility functions used by the SWELL model
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
            float solarConstant = 4.921F;
            float DtoR = (float)Math.PI / 180;
            float dd;
            float ss;
            float cc;
            float ws;
            float dayHours = 0;

            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            float SolarDeclinationMinimum = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + 356));//winter solstice
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));
            float wsMinimum = (float)Math.Acos(-Math.Tan(SolarDeclinationMinimum) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            if (input.latitude < 65 && input.latitude > -65)
            {
                input.radData.dayLength = 0.13333F / DtoR * ws;
                input.radData.etr = solarConstant * dd * 24 / (float)Math.PI
                    * (ws * ss + cc * (float)Math.Sin(ws));
            }
            else
            {
                input.radData.dayLength = dayHours;
            }
            input.radData.hourSunrise = 12 - input.radData.dayLength / 2;
            input.radData.hourSunset = 12 + input.radData.dayLength / 2;

            if (hourlyTimeStep)
            {
                for (int h = 0; h < 24; h++)
                {
                    //hour angle (degrees)
                    float HourAngleHourly = 15 * (h - 12);
                    //hourly sun ElevationMatrix (radians)
                    float cosQHadj = ss + cc * (float)Math.Cos(DtoR * HourAngleHourly);

                    float SolarElevationHourly = 0;
                    if (cosQHadj > 0) { SolarElevationHourly = (float)Math.Asin(cosQHadj); }
                    else { SolarElevationHourly = 0; }
                    if (input.radData.etrHourly[h] > 0) { dayHours += dayHours++; }
                    input.radData.etrHourly[h] = solarConstant * dd * (float)Math.Sin(SolarElevationHourly);
                }
            }
            return input.radData;
        }

        public static (float LAIoverstory, float LAIunderstory, float EVIoverstory, float EVIunderstory) estimateLAI(output outputT1)
        {
            //estimate EVI of the overstory
            float EVIoverstory;
            float EVIunderstory;
            float LAIoverstory = 0;
            float LAIunderstory = 0;
            if (outputT1.phenoCode < 3)
            {
                EVIoverstory = 0;
                LAIoverstory = 0;
                EVIunderstory = outputT1.vi / 100;
            }
            else
            {
                EVIoverstory = outputT1.vi / 100 - outputT1.viAtGrowth;
                LAIoverstory = 9.41F * outputT1.vi / 100 - 1.67F;
                EVIunderstory = outputT1.viAtGrowth;
            }

            //estimate LAI overstory (https://doi.org/10.1016/j.agrformet.2012.09.003)


            //estimate LAI understory (ENVImethod
            LAIunderstory = 3.618F * EVIunderstory - 0.118F;

            if (LAIoverstory < 0) LAIoverstory = 0;
            if (LAIunderstory < 0) LAIunderstory = 0;
            if (EVIoverstory < 0) EVIoverstory = 0;
            if (EVIunderstory < 0) EVIunderstory = 0;

            return (LAIoverstory, LAIunderstory, EVIoverstory, EVIunderstory);
        }

        public static (float SW_DIR, float SW_DIF) PartitionRadiation(float SW_IN, float SW_TOA_H)
        {
            //Erbs et al. (1982)
            if (SW_IN <= 0f || SW_TOA_H <= 0f) return (0f, 0f);

            float kt = Math.Clamp(SW_IN / SW_TOA_H, 0f, 1.2f);

            float kd; // diffuse fraction
            if (kt <= 0.22f)
                kd = 1.0f - 0.09f * kt;
            else if (kt <= 0.80f)
                kd = 0.9511f - 0.1604f * kt + 4.388f * kt * kt - 16.638f * kt * kt * kt + 12.336f * kt * kt * kt * kt;
            else
                kd = 0.165f;

            float SW_DIF = Math.Clamp(kd * SW_IN, 0f, SW_IN);
            float SW_DIR = SW_IN - SW_DIF;
            return (SW_DIR, SW_DIF);
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

        public static float estimateVegetationCover(output outputT1, parameters parameters)
        {
            //estimate vegetation cover
            float vegetationCover = 0;
            if (outputT1.phenoCode == 3)
            {
                vegetationCover = (outputT1.vi / 100 - outputT1.viAtGrowth) /
                    ((parameters.parVegetationIndex.maximumVI - outputT1.viAtGrowth));
            }
            else if (outputT1.phenoCode == 4)
            {
                vegetationCover = 1;
            }
            else if (outputT1.phenoCode == 5)
            {
                vegetationCover = 1 - (((outputT1.viAtGreendown - outputT1.vi / 100) /
                   ((outputT1.viAtGreendown - outputT1.viAtGrowth))));
            }
            //check to avoid inconsistencies
            if (vegetationCover < 0)
            {
                vegetationCover = 0;
            }

            return vegetationCover;
        }

        //gpp
        public static float  waterStressFunction(output outputT1, input input, parameters parameters, int hour)
        {
            float waterAvailability = 0;
            float waterStress = 0;

            outputT1.exchanges.PrecipitationMemory.Add(input.hourlyData.precipitation[hour]);
            outputT1.exchanges.ET0memory.Add(input.hourlyData.referenceET0[hour]);

            if (outputT1.exchanges.PrecipitationMemory.Count <
                (int)parameters.parExchanges.waterStressDays * 24)
            {
                waterAvailability = 1;
                waterStress = 1;
            }
            else
            {
                //compute water stress: https://doi.org/10.1016/j.geoderma.2021.115003
                float ndviNorm = (outputT1.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                 (parameters.parVegetationIndex.maximumVI - parameters.parVegetationIndex.minimumVI);

                float et0Sum = outputT1.exchanges.ET0memory.Sum();
                float prec = outputT1.exchanges.PrecipitationMemory.Sum();

                if (prec > et0Sum) et0Sum = prec;

                waterAvailability = ndviNorm * (.5F + .5F *
                    (prec / et0Sum)) +
                    (1 - ndviNorm) * (prec / et0Sum);

                if (waterAvailability < 0)
                {
                    waterAvailability = 0;
                }

                //compute water stress GPP
                if (waterAvailability >= parameters.parExchanges.waterStressThreshold)
                {
                    waterStress = 1;
                }
                else
                {
                    waterStress = parameters.parExchanges.waterStressSensitivity *
                        (waterAvailability - parameters.parExchanges.waterStressThreshold) + 1;
                }
               


                //remove when the memory effect ends
                if (outputT1.exchanges.ET0memory.Count == (int)parameters.parExchanges.waterStressDays * 24)
                {
                    outputT1.exchanges.ET0memory.RemoveAt(0);
                    outputT1.exchanges.PrecipitationMemory.RemoveAt(0);
                }

            }

            //set maximum water stress to 0
            if (waterStress < 0)
            {
                waterStress = 0;
            }

            return (waterStress);
        }

        public static float temperatureFunction(float temperature, float tmin, float topt, float tmax)
        {
            float tScale = 0;

            //if (temperature < tmin)
            //{
            //    tScale = 0;
            //}
            //else
            //{
            //    float numerator = (temperature - tmin) *
            //        (temperature - tmax);

            //    float denominator = numerator -
            //        (float)Math.Pow((temperature - topt), 2);

            //    tScale = numerator / denominator;
            //}



            //if average temperature is below minimum or above maximum
            if (temperature < tmin || temperature > tmax)
            {
                tScale = 0;
            }
            else
            {
                //intermediate computations
                float firstTerm = (tmax - temperature) / (tmax - topt);
                float secondTerm = (temperature - tmin) / (topt - tmin);
                float Exponential = (topt - tmin) / (tmax - topt);

                //compute forcing rate
                tScale = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            }

            return tScale;
        }

        public static float PARGppfunction(output outputsT1, float par, float halfSaturationValue)
        {
            float parScaleTree = 1 / (1 + (par / halfSaturationValue));

            return parScaleTree;
        }

        public static float phenologyFunction(output outputsT1, parameters parameters)
        {
            float phenologyScale = 0;

            if (outputsT1.phenoCode == 3)
            {
                float leafActivity = 1 / (1 + (float)Math.Exp(3 *
                    (parameters.parExchanges.growthPhenologyScalingFactor - outputsT1.growthPercentage / 100)));
                phenologyScale = leafActivity;
            }
            else if (outputsT1.phenoCode == 5)
            {
                //float leafActivity = 1 / (1 + (float)Math.Exp(-10 *
                //    (parameters.parExchanges.declinePhenologyScalingFactor - outputsT1.declinePercentage / 100)));
                //phenologyScale = leafActivity;
                phenologyScale = 1;
            }
            else if (outputsT1.phenoCode == 4) //greendown
            {
                phenologyScale = 1;
            }
            else
            {
                phenologyScale = 0;
            }

            return phenologyScale;
        }

        public static float VPDfunction(float vpd, parameters parameters)
        {
            float vpdMin = parameters.parExchanges.vpdMin;
            float vpdMax = parameters.parExchanges.vpdMax;
            float kVPD = parameters.parExchanges.vpdSensitivity;


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

        public static float temperatureRecoFunction(float temperature, parameters parameters)
        {
            const float Tref = 288.15F;
            const float T0 = 227.13F;
            return (float)Math.Exp(parameters.parExchanges.activationEnergyReco *
                ((1 / (Tref - T0)) - (1 / ((273.15 + temperature) - T0))));
        }

        public static float gppRecoFunction(input input, float gppOver, float gppUnder, 
            float PARscaleOverstory, float PARscaleUnderstory,
            output outputs1, parameters parameters)
        {
            float gppRecoFunction = 0;
            //compute function
            gppRecoFunction = parameters.parExchanges.referenceRespiration +
                (parameters.parExchanges.respirationGPPresponseOver * gppOver  * RecoRespirationFunction(input,outputs1,parameters)) +
                (parameters.parExchanges.respirationGPPresponseUnder * gppUnder);

            return gppRecoFunction;
        }

        public static float RecoRespirationFunction(input input, output outputsT1, parameters parameters)
        {
            float recoReferenceFunction = 0;

            float growingSeasonPercentage = 0;
            float growingSeasonTotal = parameters.parGrowth.thermalThreshold +
                parameters.parGreendown.thermalThreshold + parameters.parSenescence.photoThermalThreshold;

            if (outputsT1.phenoCode == 3)
            {
                growingSeasonPercentage = outputsT1.growth.growthState / growingSeasonTotal;
            }
            else if (outputsT1.phenoCode == 4)
            {
                growingSeasonPercentage = (parameters.parGrowth.thermalThreshold +
                    outputsT1.greenDown.greenDownState) / growingSeasonTotal;
            }
            else if (outputsT1.phenoCode == 5)
            {
                growingSeasonPercentage = (parameters.parGrowth.thermalThreshold +
                     parameters.parGreendown.thermalThreshold +
                     outputsT1.decline.declineState) / growingSeasonTotal;
            }

            if (outputsT1.phenoCode >= 3)
            {
                recoReferenceFunction = 1 / (1 + (float)Math.Exp(10 * (growingSeasonPercentage -
                     parameters.parExchanges.respirationAgingFactor)));
            }

            if (outputsT1.phenoCode < 3)
            {
                recoReferenceFunction = 0;
            }

            return recoReferenceFunction;
        }


        #endregion

        #region leaf temperature model

        private static (float leafWaterVD, float airWaterVD) VaporDensity(float Tair, float TairMax)
        {
            float leafWaterVD = 5.018F + (0.32321F * Tair) + (0.0081847F * Tair * Tair) + (0.00031243F * Tair * Tair * Tair);
            float airWaterVD = 5.018F + (0.32321F * TairMax) + (0.0081847F * TairMax * TairMax) + (0.00031243F * TairMax * TairMax * TairMax);

            return (leafWaterVD, airWaterVD);
        }

        private static (float BoundaryLayerWidth, float BoundaryLayerResistance) BoundaryLayerResistanceWaterVapor(float WindSpeed, float LeafLength,
            float LeafShapeParam, float DiffusionCoefficient)
        {
            float BoundaryLayerWidth = LeafShapeParam * (float)(Math.Pow((LeafLength / WindSpeed), 0.5));
            float BoundaryLayerResistance = (BoundaryLayerWidth / 1000) / DiffusionCoefficient;
            return (BoundaryLayerWidth, BoundaryLayerResistance);
        }

        private static float LeafLayerResistanceWaterVapor(float StomatalResistance,
            float CuticolarResistance, float IntercellularSpaceResistance)
        {
            float LeafLayerResistance = ((IntercellularSpaceResistance + StomatalResistance) * CuticolarResistance) /
                (StomatalResistance + CuticolarResistance + IntercellularSpaceResistance);
            return LeafLayerResistance;
        }

        //private static float Transpiration(float Tair, float Tmax, //for vapor density
        //    float WindSpeed = 1, float LeafLength = 0.06F, float LeafShapeParam = 4, float DiffusionCoefficient = 2.42E-05F, //for boundary resistance
        //    float StomatalResistance = 600, float CuticolarResistance = 500, float IntercellularSpaceResistance = 25) //for leaf resistance
        //{
        //    var (leafWaterVD, airWaterVD) = VaporDensity(Tair, Tmax);
        //    var BoundaryResistance = BoundaryLayerResistanceWaterVapor(WindSpeed, LeafLength, LeafShapeParam, DiffusionCoefficient);
        //    var LeafResistance = LeafLayerResistanceWaterVapor(StomatalResistance, CuticolarResistance, IntercellularSpaceResistance);

        //    return (leafWaterVD - airWaterVD) / (LeafResistance + BoundaryResistance) * 1000 * 0.055F;

        //}

        private static (float DirectLightLeaves, float DiffuseLightLeaves) LightLeavesInterception(
     float absorptionCoefficient = 0.6f,
     float leafOrientationDeg = 40f,   // degrees
     float directLight = 0f,
     float diffuseLight = 0f)
        {
            // degrees → radians
            float iRad = leafOrientationDeg * (float)(Math.PI / 180.0);

            // optional safety clamps
            float a = Math.Clamp(absorptionCoefficient, 0f, 1f);

            float direct = a * (float)Math.Cos(iRad) * directLight;
            float diffuse = a * diffuseLight;

            return (direct, diffuse);
        }


        public static float LeavesTemperature(float Tair, float Tsoil, float VPD, float Tmax,
            float absorptionCoefficient = 0.6F, float LeafOrientation = 40F, float DirectLight = 0F, float DiffuseLight = 0F,
            float LeavesEmissivity = 0.97F, float StomatalResistance = 600F, float CuticolarResistance = 500,
            float IntercellularSpaceResistance = 25F, float WindSpeed = 1F, float LeafLength = 0.06F, float LeafShapeParam = 4F)
        {

            const float SIGMA = 5.670374419e-8F; // W m-2 K-4
            const float LATENT_HEAT_VAPOR = 2450.00F;   // J Kg-1
            const float DIFFUSION_COEFF = 2.42E-05F;   //m2 s-1
            const float THERMAL_CONDUCTIVITY = 0.0259F;



            var (leafWaterVD, airWaterVD) = VaporDensity(Tair, Tmax);
            var (DirectLightLeaves, DiffuseLightLeaves) = LightLeavesInterception(absorptionCoefficient, LeafOrientation, DirectLight, DiffuseLight);
            var (Ldown, Lground, R_in, R_abs) = TerrestrialIR.ComputeR(Tair, Tsoil, VPD, null, .5F, LeavesEmissivity, .97F, .22F, .5F, 1F);
            var LeafLayerResistance = LeafLayerResistanceWaterVapor(StomatalResistance, CuticolarResistance, IntercellularSpaceResistance);
            var (BoundaryLayerWidth, BoundaryLayerResistance) = BoundaryLayerResistanceWaterVapor(WindSpeed, LeafLength, LeafShapeParam, DIFFUSION_COEFF);
            var ConvectionCoefficient = THERMAL_CONDUCTIVITY / (BoundaryLayerWidth / 1000F); //W m-2 °C-1

            float SlopeVapPress = (33.8639F * (0.05904F * (float)Math.Pow((0.00739F * Tmax + 0.8072F),7) - 0.0000342F)) * 0.1F;

            float totalRadiation = DirectLightLeaves + DiffuseLightLeaves + R_abs;

         
            float Tleaf = Tair +
                ((totalRadiation / 2 - 
                (LeavesEmissivity * SIGMA * ((float)Math.Pow((Tair + 273.15), 4)) - ((LATENT_HEAT_VAPOR * (23 - airWaterVD)) /
                (LeafLayerResistance + BoundaryLayerResistance)))) / (4 * LeavesEmissivity * SIGMA * ((float)Math.Pow((Tair + 273.15F), 3)) +
                ConvectionCoefficient + ((LATENT_HEAT_VAPOR * SlopeVapPress * 0.804F)/ (LeafLayerResistance + BoundaryLayerResistance))));

            return Tleaf;

        }




        #endregion
    }



    public static class TerrestrialIR
    {
        
        private const float SIGMA = 5.670374419e-8F; // W m-2 K-4
        // ---- public one-call method ------------------------------------------------
        // Pass either vpdKPa 
        // ===== API principale =====
        // Passa VPD (kPa) OPPURE RH (%). cloudFraction opzionale [0..1].
        public static (float Ldown, float Lground, float R_in, float R_abs) ComputeR(
            float tAirC,
            float soilTC,
            float vpdKPa = 0,
            float? cloudFraction = null,   // 0..1
            float skyViewFactor = 0.5F,     // SVF: foglia verticale ~0.5; esposta verso l’alto ~1.0
            float leafEmissivity = 0.98F,   // ≈ assorbanza LW
            float groundEmissivity = 0.97F, // suolo/chioma
            float cloudAlpha = 0.22F,       // f(C)=1+αC^2
            float epsSkyClipMin = 0.50F,
            float epsSkyClipMax = 1.00F
        )
        {
            // 1) vapore (kPa)
            float ea = EaFromVPD(tAirC, vpdKPa);
            

            // 2) L↓ (cielo → superficie)
            float tAirK = tAirC + 273.15F;
            float epsClr = SkyEmissivityClear(tAirK, ea, epsSkyClipMin, epsSkyClipMax);
            float epsSky = SkyEmissivityAllSky(epsClr, cloudFraction, cloudAlpha);
            float Ldown = epsSky * SIGMA * (float)Math.Pow(tAirK, 4);

            // 3) Lground (suolo → cielo)
            float tSoilK = soilTC + 273.15F;
            float Lground = (float)Math.Clamp(groundEmissivity, 0.0, 1.0) * SIGMA * (float)Math.Pow(tSoilK, 4);

            // 4) R incidente sulla foglia (geometria via SVF)
            float SVF = (float)Math.Clamp(skyViewFactor, 0.0, 1.0);
            float R_in = SVF * Ldown + (1.0F - SVF) * Lground;          // W m-2
            float R_abs = (float)Math.Clamp(leafEmissivity, 0.0, 1.0) * R_in;   // ε·R

            return (Ldown, Lground, R_in, R_abs);
        }

        // ---- helpers ---------------------------------------------------------------
        // Saturation vapor pressure (kPa) via Tetens; T in °C
        private static float Es_kPa(double tC) =>
            0.6108F * (float)Math.Exp((17.27 * tC) / (tC + 237.3));

        private static float EaFromVPD(float tAirC, float vpdKPa)
        {
            double es = Es_kPa(tAirC);
            return (float)Math.Max(0.0, es - Math.Max(0.0, vpdKPa));
        }

    
        // Brutsaert (1975) clear-sky emissivity; ea in kPa, T in K
        private static float SkyEmissivityClear(float tAirK, float eaKPa, float clipMin, float clipMax)
        {
            double eps = 1.24 * Math.Pow(eaKPa / tAirK, 1.0 / 7.0);
            if (double.IsNaN(eps) || double.IsInfinity(eps)) eps = clipMin;
            return (float)(float)Math.Clamp(eps, clipMin, clipMax);
        }

        // All-sky emissivity with optional cloud fraction C (0..1)
        private static float SkyEmissivityAllSky(float epsClear, float? cloudFraction, float alpha)
        {
            if (cloudFraction is null) return epsClear;
            double C = Math.Clamp(cloudFraction.Value, 0.0, 1.0);
            return (float)Math.Min(1.0, epsClear * (1.0 + alpha * C * C));
        }
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

            input.hourlyData = estimateHourly(input);

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
        public hourlyData estimateHourly(input inputDaily)
        {
            hourlyData hourlyData = new hourlyData();

            float avgT = (inputDaily.airTemperatureMaximum + inputDaily.airTemperatureMinimum) / 2;
            float dailyRange = inputDaily.airTemperatureMaximum - inputDaily.airTemperatureMinimum;
            float dewPoint = Math.Clamp(inputDaily.dewPointTemperature, inputDaily.airTemperatureMinimum - 5,
                inputDaily.airTemperatureMaximum);
            float rain = inputDaily.precipitation;

            for (int i = 0; i < 24; i++)
            {
                // Temperature Estimate
                float hourlyT = (float)(avgT + dailyRange / 2 * Math.Cos(0.2618f * (i - 15)));
                hourlyData.airTemperature.Add(hourlyT);

                // Relative Humidity Estimate            
                float es = 0.6108f * (float)Math.Exp((17.27f * hourlyT) / (237.3F + hourlyT));
                float ea = 0.6108f * (float)Math.Exp((17.27F * dewPoint) / (237.3F + dewPoint));
                float rh_hour = ea / es * 100;
                hourlyData.relativeHumidity.Add(Math.Clamp(rh_hour, 0f, 100f));

                // Precipitation
                hourlyData.precipitation.Add(rain / 24);

                // evenly distribute or use sinusoidal pattern
                inputDaily.radData = dayLength(inputDaily, inputDaily.airTemperatureMaximum, inputDaily.airTemperatureMinimum);

                hourlyData.photoActiveRadiation.Add(inputDaily.radData.gsrHourly[i] * 1269.44F);

                // Wind Speed ?

                // VPD
                hourlyData.vaporPressureDeficit.Add(vpd(hourlyData, i));

                // ET₀
                hourlyData.referenceET0.Add(referenceEvapotranspiration(inputDaily, hourlyData, i));
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

        public float vpd(hourlyData Input, int hour)
        {
            //VPD calculation method by Monteith and Unsworth (1990)

            float SVP = 0.6108f * (float)Math.Exp((17.27f * Input.airTemperature[hour]) / (Input.airTemperature[hour] + 237.3f)); // in kPa
            float AVP = SVP * Input.relativeHumidity[hour] / 100f;
            float VPD = SVP - AVP;
            return VPD;
        }

        public float referenceEvapotranspiration(input dailyInput, hourlyData Input, int hour)
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
                         + c1 * Input.relativeHumidity[hour]
                         + c2 * Input.airTemperature[hour]
                         + c3 * Math.Pow(Input.relativeHumidity[hour], 2)
                         + c4 * Math.Pow(Input.airTemperature[hour], 2)
                         + c5 * Rs_MJ
                         + 0.5 * Rs_MJ * (c6 * Input.relativeHumidity[hour]
                         + c7 * Input.airTemperature[hour])
                         + c8 * Math.Pow(Rs_MJ, 2);

            if (ET0 < 0) { ET0 = 0; }
            ;
            return (float)ET0;

        }

    }
    #endregion

}