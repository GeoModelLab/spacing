using runner.data;
using source.data;
using System.Globalization;

namespace runner
{
    public class weatherReader
    {     
        public Dictionary<DateTime, input> readWeather(string fileName)
        {
            Dictionary<DateTime, input> date_input = new Dictionary<DateTime, input>();
            StreamReader streamReader = new StreamReader(fileName);

            float latitude = 0;           
            streamReader.ReadLine();

           
            while (!streamReader.EndOfStream)
            {
                string[] line = streamReader.ReadLine().Split(',');
                input input = new input();

                if (line[4] != "NA")
                {
                    #region read weather data
                    string rawDate = line[2].Trim('"');
                    DateTime date = Convert.ToDateTime(rawDate);
                    input.date = date;
                    
                    input.airTemperatureMaximum = (float)Convert.ToDouble(line[4]);
                    if (line[3] != "NA")
                    {
                        input.airTemperatureMinimum = (float)Convert.ToDouble(line[3]);
                    }
                    else
                    {
                        input.airTemperatureMinimum = input.airTemperatureMaximum - 10;
                    }
                    //TODO check
                    input.latitude = (float)Convert.ToDouble(line[0]);
                    date_input.Add(date, input);
                    #endregion
                }
            }
            streamReader.Close();

           
            return date_input;

        }


        public Dictionary<DateTime, input> readWeatherFluxNet(string fileName, string calibrationVariable, pixel thisPixel, int startYear)
        {
            Dictionary<DateTime, input> date_input = new Dictionary<DateTime, input>();
            StreamReader streamReader = new StreamReader(fileName);

            float latitude = 0;
           
            streamReader.ReadLine();

            List<float> temperature = new List<float>();
        
            // --- State for feature building (per site/pixel stream) ---
            double? lastAirTemp = null;                 // φ1 = previous-hour air temperature
            System.Collections.Generic.Queue<double> last12Air = new(); // rolling 12-h window

            input input = null;
            while (!streamReader.EndOfStream)
            {
                string[] line = streamReader.ReadLine().Split(',');
                

                DateTime date = new DateTime(int.Parse(line[2]) - 1, 12, 31).AddDays(int.Parse(line[3])).
                    AddHours(int.Parse(line[4]));

                int hour = int.Parse(line[4]);
                if(hour==0)
                {
                    input = new input();
                }
                if (date >= new DateTime(startYear,09,01))
                {
                    //if temperature is present
                    if (line[3] != "-9999")
                    {
                        #region read weather data
                        //if (calibrationVariable != "Phenology")
                        //{
                        if (float.Parse(line[7])>-50)
                            {
                                temperature.Add(float.Parse(line[7]));
                            }
                            else
                            {
                                if (temperature.Count > 0)
                                {
                                temperature.Add(input.airTemperatureH[hour-1]);
                                }
                                else { temperature.Add(0); };
                            }

                        input.airTemperatureH[hour] = float.Parse(line[7]);
                        input.precipitationH[hour] = float.Parse(line[8]);
                        input.solarRadiationH[hour] = float.Parse(line[10])*0.0036f;
                        input.vaporPressureDeficitH[hour] = float.Parse(line[11]);

                            // --- Parse hour & DOY for harmonics (use your existing parsed date/hour if available) ---
                            int hourInt = int.Parse(line[4]);     // 0..23
                            int jd = int.Parse(line[3]);     // DOY (1..365/366)

                            // --- Read current air temperature (use your own parsing/cleaning as above) ---
                            double airT = double.Parse(line[7], System.Globalization.CultureInfo.InvariantCulture);

                            // --- update state for next hour ---
                            lastAirTemp = airT;

                        input.relativeHumidityH[hour] = float.Parse(line[9]);
                        
                    }

                    //the last hour of the day, compute tmax and tmin
                    if (line[4] == "23")
                    {
                        input.airTemperatureMaximum = temperature.Max();
                        input.airTemperatureMinimum = temperature.Min();
                        input.precipitation = input.precipitationH.Sum();
                        //TODO check
                        input.latitude = (float)Convert.ToDouble(line[6]);
                        date = new DateTime(int.Parse(line[2]) - 1, 12, 31).AddDays(int.Parse(line[3]));
                        temperature = new List<float>();
                        input.date = date;
                        radData radData = dayLength(input,input.airTemperatureMaximum,
                            input.airTemperatureMinimum);
                        
                        if (calibrationVariable != "Phenology")
                        {

                            //if RH is -9999 need to estimate it
                            for (int h = 0; h < 24; h++)
                            {
                                if (input.relativeHumidityH[h] <= 0)
                                {
                                    //estimate RH from VPD
                                    input.relativeHumidityH[h] = VPDtoRH(input.vaporPressureDeficitH[h],
                                        input.airTemperatureH[h]);
                                }
                                //compute hourly ET0
                                //input.referenceET0H[h] = CalculateET0(input.relativeHumidityH[h],
                                //    input.airTemperatureH[h], input.solarRadiationH[h]);
                            }

                        }

                        date_input.Add(date, input);
                        input = null;
                    }
                    #endregion
                }
               
            }

            //close the stream
            streamReader.Close();


            return date_input;

        }

        public Dictionary<DateTime, input> readWeatherNASA_ERA5(
 string fileName,
 string calibrationVariable,
 pixel thisPixel,
 int startYear,
 string weatherSource)
        {
            Dictionary<DateTime, input> date_input = new();
            StreamReader streamReader = new StreamReader(fileName);

            // salta header
            streamReader.ReadLine();

            List<float> temperature = new();

            input input = null;
            float prevPrec = 0f;
            float prevRad = 0f;

            DateTime start = thisPixel.dateGPP.Keys.First();
            DateTime end = thisPixel.dateGPP.Keys.Last();


            while (!streamReader.EndOfStream)
            {
                string[] line = streamReader.ReadLine().Replace("\"", "").Split(',');

                int year = int.Parse(line[3]);
                int month = int.Parse(line[4]);
                int day = int.Parse(line[5]);

                if (year >= start.AddYears(-1).Year &&
                     year <= end.Year)
                {

                    if (weatherSource == "nasaPowerDaily")
                    {
                        input = new input();
                        input.date = new DateTime(year, month, day);



                        input.latitude = float.Parse(line[2]);
                        input.airTemperatureMaximum = float.Parse(line[6], CultureInfo.InvariantCulture);
                        input.airTemperatureMinimum = float.Parse(line[7], CultureInfo.InvariantCulture);
                        input.dewPointTemperature = float.Parse(line[8], CultureInfo.InvariantCulture);
                        input.solarRadiation = float.Parse(line[10], CultureInfo.InvariantCulture);
                        input.precipitation = float.Parse(line[11], CultureInfo.InvariantCulture);

                        estimateHourly(input);
                        radData radData = dayLength(input, input.airTemperatureMaximum,
                              input.airTemperatureMinimum);
                        date_input.Add(input.date, input);

                    }

                    else
                    {



                        int hour = int.Parse(line[6]);

                        if (year < startYear) continue;
                        if (line[7] == "NA") continue;

                        // start new day
                        if (hour == 0)
                        {
                            input = new input();
                            prevPrec = 0f;
                            prevRad = 0f;
                        }

                        float airT = float.Parse(line[7], CultureInfo.InvariantCulture);
                        float dewT = float.Parse(line[8], CultureInfo.InvariantCulture);

                        float wind = float.Parse(line[11], CultureInfo.InvariantCulture);

                        input.airTemperatureH[hour] = airT;
                        float prec = 0;
                        float rad = 0;
                        if (weatherSource == "nasaPower")
                        {
                            prec = float.Parse(line[9], CultureInfo.InvariantCulture) / 24f;
                            rad = float.Parse(line[10], CultureInfo.InvariantCulture);
                        }
                        else if (weatherSource == "ERA5_LAND")
                        {
                            float precCum = float.Parse(line[9], CultureInfo.InvariantCulture);
                            float radCum = float.Parse(line[10], CultureInfo.InvariantCulture);
                            prec = precCum - prevPrec; // mm or m
                            rad = (radCum - prevRad) * 0.0036f; // J m-2
                            prevPrec = precCum;
                            prevRad = radCum;


                        }
                        input.precipitationH[hour] = prec;
                        input.solarRadiationH[hour] = rad;
                      

                        // RH
                        float es_air = (float)Math.Exp((17.625f * airT) / (243.04f + airT));
                        float es_dew = (float)Math.Exp((17.625f * dewT) / (243.04f + dewT));
                        float rh = 100f * (es_dew / es_air);
                        rh = Math.Clamp(rh, 0f, 100f);
                        input.relativeHumidityH[hour] = rh;

                        // VPD (kPa)
                        float es = 0.6108f * (float)Math.Exp((17.27f * airT) / (airT + 237.3f));
                        float ea = es * (rh / 100f);
                        input.vaporPressureDeficitH[hour] = Math.Max(0f, es - ea);

                        // ET0
                        //input.referenceET0H[hour] = CalculateET0(rh, airT, input.solarRadiationH[hour]);

                        // end of day
                        if (hour == 23)
                        {
                            input.airTemperatureMaximum = input.airTemperatureH.Max();
                            input.airTemperatureMinimum = input.airTemperatureH.Min();
                            input.precipitation = input.precipitationH.Sum();
                            input.latitude = float.Parse(line[2], CultureInfo.InvariantCulture);
                            input.date = new DateTime(year, month, day);
                            radData radData = dayLength(input, input.airTemperatureMaximum,
                              input.airTemperatureMinimum);
                            date_input.Add(input.date, input);
                            input = null;
                        }
                    }
                }
            }

            streamReader.Close();
            return date_input;
        }

           
      

        #region private methods
        private static float Esat(float TdegC, double Pa = 101)
        {
            double a = 611.21;
            double b = 17.502;
            double c = 240.97;
            double f = 1.0007 + 3.46 * Math.Pow(10, -8) * Pa * 1000;
            double esatVal = f * a * Math.Exp(b * TdegC / (c + TdegC));
            return (float)esatVal;
        }

        private static float VPDtoRH(float VPD, float TdegC, double Pa = 101)
        {
            double esatVal = Esat(TdegC, Pa);
            double e = Math.Max(0, esatVal - VPD / 10 * 1000);
            double RH = 100 * e / esatVal;

            //minimum RH forced to 10 for feasibility
            if (RH < 10) { RH = 10; }
            return (float)RH;
        }

        private static float CalculateET0(float RH, float T, float Rs)
        {
            // Convert Rs from micromoles/m² to MJ/m²/h
            float Rs_MJ = Rs;

            //// Given coefficients
            //double c0 = 0.1396;
            //double c1 = -3.019e-3;
            //double c2 = -1.2109e-3;
            //double c3 = 1.626e-5;
            //double c4 = 8.224e-5;
            //double c5 = 0.1842;
            //double c6 = -1.095e-3;
            //double c7 = 3.655e-3;
            //double c8 = -4.442e-3;

            //// Compute ET0 using the equation
            //float ET0 = c0
            //             + c1 * RH
            //             + c2 * T
            //             + c3 * Math.Pow(RH, 2)
            //             + c4 * Math.Pow(T, 2)
            //             + c5 * Rs_MJ
            //             + 12 * Rs_MJ * (c6 * RH + c7 * T)
            //             + c8 * Math.Pow(Rs_MJ, 2);



            // T in °C
            // Rs_MJ = net radiation or global radiation in MJ m-2 h-1

            float ET0 = 0f;

            // slope of saturation vapor pressure curve (kPa °C-1)
            float Delta =
                4098f *
                (0.6108f * (float)Math.Exp(17.27f * T / (T + 237.3f))) /
                (float)Math.Pow(T + 237.3f, 2f);

            // psychrometric constant
            float gamma = 0.066f; // kPa °C-1

            // Priestley–Taylor coefficient (reference crop)
            float alpha = 1.26f;

            // latent heat of vaporization
            float lambda = 2.45f; // MJ kg-1

            // Priestley–Taylor ET0 (mm h-1)
            ET0 =
                alpha *
                (Delta / (Delta + gamma)) *
                (Rs_MJ / lambda);

            // safety clamp
            ET0 = Math.Max(0f, ET0);




            return (float)ET0;
        }

        #endregion

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
                hourlyData.solarRadiationH[h] = inputDaily.radData.gsrHourly[h];

                // Wind Speed ?

                // VPD
                hourlyData.vaporPressureDeficitH[h] = vpd(hourlyData, h);

                // ET₀
                //hourlyData.referenceET0H[h] = CalculateET0(hourlyData.relativeHumidityH[h], hourlyT, hourlyData.solarRadiationH[h]);
            }

            return hourlyData;
        }
        #endregion


        public radData dayLength(input input, float Tmax, float Tmin)
        {
            radData _radData = input.radData;


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

          


            // Estimate global solar radiation (GSR) if not measured
            if (_radData.gsr == 0f)
            {
                float kRs = 0.19f; // empirical constant
                float Rs = kRs * (float)Math.Sqrt(Tmax - Tmin) * _radData.etr;
                _radData.gsr = (float)Math.Round(Rs, 2); // MJ m⁻² day⁻¹
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

            // =====================================================
            // DAILY REFERENCE ET0 (HARGREAVES-SAMANI)
            // =====================================================
            // Inputs:
            // Tmax, Tmin: °C
            // Ra (_radData.etr): MJ m-2 day-1
            // Output:
            // ET0: mm day-1

            float Tmean = 0.5f * (Tmax + Tmin);
            float deltaT = Math.Max(0.1f, Tmax - Tmin); // safety (no zero)

            float ET0_Hargreaves =
                0.0023f *
                (Tmean + 17.8f) *
                (float)Math.Sqrt(deltaT) *
                _radData.etr;

            // physical bounds
            ET0_Hargreaves = Math.Max(0f, ET0_Hargreaves);

            // store
            input.referenceEvapotranspiration = ET0_Hargreaves;

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

     
    }

}
