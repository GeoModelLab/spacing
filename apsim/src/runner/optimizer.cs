using runner.data;
using source.data;
using source.functions;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using UNIMI.optimizer;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace runner
{
    //this class perform the multi-start simplex optimization of SWELL parameters
    internal class optimizer : IOBJfunc
    {
        #region optimizer methods
        int _neval = 0;
        int _ncompute = 0;

        public Dictionary<int, string> _Phenology = new Dictionary<int, string>();
        
        // the number of times that this function is called        
        public int neval
        {
            get
            {
                return _neval;
            }

            set
            {
                _neval = value;
            }
        }

        
        // the number of times where the function is evaluated 
        // (when an evaluation is requested outside the parameters domain this counter is not incremented        
        public int ncompute
        {
            get
            {
                return _ncompute;
            }

            set
            {
                _ncompute = value;
            }
        }
        #endregion

        #region instances of SWELL data types and functions
        //SWELL data types
        output output = new output();
        output outputT1 = new output();
        //instance of the SWELL functions
        VIdynamics VIdynamics = new VIdynamics();
        dormancySeason dormancy = new dormancySeason();
        growingSeason growing = new growingSeason();
        source.functions.exchanges exchanges = new source.functions.exchanges();
        #endregion

        #region instance of the weather reader class
        weatherReader weatherReader = new weatherReader();
        #endregion

        #region local variables to perform the optimization
        public Dictionary<string, pixel> idPixel = new Dictionary<string, pixel>();
        public Dictionary<string, parameter> nameParam = new Dictionary<string, parameter>();
        public Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
        public Dictionary<DateTime, output> date_outputs = new Dictionary<DateTime, output>();
        public string weatherDir;
        public string weatherSource;
        public List<string> allWeatherDataFiles;
        public string species;
        public string weatherDataFile;        
        public bool isCalibration;
        public string calibrationVariable;
        //for validation
        public int parset;
        public int startYear;
        public int endYear;
        public string outputsCalibrationDir;
        public string outputsValidationDir;
        public string outputParametersDir;
        public string referenceFluxesDir;
        public string vegetationIndex;
        internal string timeStep;
        internal string configuration;
        #endregion

        //this method perform the multi-start simplex calibration
        public double ObjfuncVal(double[] Coefficient, double[,] limits)
        {         
            #region Calibration methods
            for (int j = 0; j < Coefficient.Length; j++)
            {
                if (Coefficient[j] == 0)
                {
                    break;
                }
                if (Coefficient[j] <= limits[j, 0] | Coefficient[j] > limits[j, 1])
                {
                    return 1E+300;
                }
                
            }
            _neval++;
            _ncompute++;
            #endregion

            #region assign parameters
            // Get the type of the parameters object
            source.data.parameters parameters = new parameters();
            var _parametersType = parameters.GetType();

            int coef = 0;
            foreach (var param in nameParam.Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                if (nameParam[param].calibration != "")
                {
                    object convertedValue = Convert.ChangeType(Coefficient[coef],
                        propertyInfo.PropertyType);
                    propertyInfo.SetValue(classInstance, convertedValue);
                    coef++;
                }
                else
                {
                    propertyInfo.SetValue(classInstance, param_outCalibration[param]);
                }


            }

            #endregion

            //list of errors
            List<double> errors_gpp = new List<double>();
            List<double> errors_nee = new List<double>();
            List<double> errors_reco = new List<double>();
            List<double> errors_evi = new List<double>();

            double objFun = 0;
            List<float> simulated_evi = new List<float>();
            List<float> measured_evi = new List<float>();

            List<float> simulated_gpp = new List<float>();
            List<float> measured_gpp = new List<float>();

            List<float> simulated_nee = new List<float>();
            List<float> measured_nee = new List<float>();

            List<float> simulated_reco = new List<float>();
            List<float> measured_reco = new List<float>();

            float[] recoObsSum = new float[24];
            float[] recoSimSum = new float[24];
            int[] recoCount = new int[24];

            float[] gppObsSum = new float[24];
            float[] gppSimSum = new float[24];
            int[] gppCount = new int[24];

            float[] neeObsSum = new float[24];
            float[] neeSimSum = new float[24];
            int[] neeCount = new int[24];

            //assign simulation settings
            //loop over ids
            foreach (var id in idPixel.Keys)
            {
                //reinitialize variables for each site
                output = new output();
                outputT1 = new output();

                exchanges= new source.functions.exchanges();


                // Find the closest point
                string closestFile = FindClosestPoint(idPixel[id].latitude, idPixel[id].longitude, allWeatherDataFiles);

                //read weather data
                Dictionary<DateTime, input> weatherData = new Dictionary<DateTime, input>();
                if (weatherSource != "Tower")
                {
                    weatherData = weatherReader.readWeatherNASA_ERA5(weatherDir + "//" + closestFile, calibrationVariable, idPixel[id], startYear, weatherSource);
                }
                else
                {
                    weatherData = weatherReader.readWeatherFluxNet(weatherDir + "//" + closestFile, calibrationVariable, idPixel[id], startYear);
                }


                //load calibrated phenology parameters
                if (calibrationVariable != "Phenology")
                {

                    //StreamReader streamReader = new StreamReader(outputParametersDir + "_Phenology//" +
                    //    "calibParam" + "_" + id + "_WS" + weatherSource +  ".csv");

                    string dir = outputParametersDir + "_Phenology";

                    string mustContain = $"calibParam_{id}_WS_{weatherSource}_";

                    string filePath = Directory
                        .GetFiles(dir, "*.csv")
                        .FirstOrDefault(f =>
                            Path.GetFileName(f).Contains(mustContain)
                        );

                    if (filePath == null)
                    {
                        throw new FileNotFoundException(
                            $"No CSV file containing '{mustContain}' found in {dir}"
                        );
                    }

                    StreamReader streamReader = new StreamReader(filePath);


                    streamReader.ReadLine();
                    while(!streamReader.EndOfStream)
                    {
                        string line = streamReader.ReadLine();
                        var values = line.Split(',');

                        string propertyClass = values[3].Split('_')[0].Trim();
                        string propertyName = values[3].Split('_')[1].Trim();
                        string propertyValue = values[4].Trim();

                        // Get the type of the parameters object
                        var parametersType = parameters.GetType();

                        // Find the class inside the parameters instance
                        var classProperty = parametersType.GetField(propertyClass);

                        if (classProperty != null)
                        {
                            var classInstance = classProperty.GetValue(parameters);
                            if (classInstance != null)
                            {
                                var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                if (propertyInfo != null && propertyInfo.CanWrite)
                                {
                                    string x = classInstance.ToString();
                                    if (!x.Contains("parPhotosynthesis"))
                                    {
                                        object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                        propertyInfo.SetValue(classInstance, convertedValue);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Class instance '{propertyClass}' is null.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                        }
                    }
                    if (calibrationVariable == "Photosynthesis")
                    {
                        if (File.Exists(outputParametersDir + "_Photosynthesis//" +
                            "calibParam_" + id + "_" + weatherSource + ".csv"))
                        {
                            streamReader = new StreamReader(outputParametersDir + "_Photosynthesis//" +
                                "calibParam_" + id + "_" + weatherSource + ".csv");
                            streamReader.ReadLine();
                            while (!streamReader.EndOfStream)
                            {
                                string line = streamReader.ReadLine();
                                var values = line.Split(',');

                                string propertyClass = values[3].Split('_')[0].Trim();
                                string propertyName = values[3].Split('_')[1].Trim();
                                string propertyValue = values[4].Trim();

                                // Get the type of the parameters object
                                var parametersType = parameters.GetType();

                                // Find the class inside the parameters instance
                                var classProperty = parametersType.GetField(propertyClass);

                                if (classProperty != null)
                                {
                                    var classInstance = classProperty.GetValue(parameters);
                                    if (classInstance != null)
                                    {
                                        var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                        if (propertyInfo != null && propertyInfo.CanWrite)
                                        {
                                            string x = classInstance.ToString();
                                            if (x.Contains("parPhotosynthesis"))
                                            {
                                                object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                                propertyInfo.SetValue(classInstance, convertedValue);
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Class instance '{propertyClass}' is null.");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                                }
                            }
                            streamReader.Close();
                        }
                    }
                    else if (calibrationVariable == "Respiration")
                    {
                        filePath =
    $"{outputParametersDir}_Photosynthesis//" +
    $"calibParam_{id}_WS_{weatherSource}_CONFIG_{configuration}.csv";

                        streamReader = new StreamReader(filePath);
                        streamReader.ReadLine();
                        while (!streamReader.EndOfStream)
                        {
                            string line = streamReader.ReadLine();
                            var values = line.Split(',');

                            string propertyClass = values[3].Split('_')[0].Trim();
                            string propertyName = values[3].Split('_')[1].Trim();
                            string propertyValue = values[4].Trim();

                            // Get the type of the parameters object
                            var parametersType = parameters.GetType();

                            // Find the class inside the parameters instance
                            var classProperty = parametersType.GetField(propertyClass);

                            if (classProperty != null)
                            {
                                var classInstance = classProperty.GetValue(parameters);
                                if (classInstance != null)
                                {
                                    var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                    if (propertyInfo != null && propertyInfo.CanWrite)
                                    {
                                        string x = classInstance.ToString();
                                        if (x.Contains("parPhotosynthesis"))
                                        {
                                            object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                            propertyInfo.SetValue(classInstance, convertedValue);
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Class instance '{propertyClass}' is null.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                            }
                        }
                        streamReader.Close();
                    }
                }

              
                //loop over dates
                foreach (var day in weatherData.Keys)
                {
                   
                    //set the simulation period: TODO: adjust according to your needs!!!
                    if (day.Year >= startYear && day.Year <= endYear)
                    {
                        weatherData[day].simulationSettings.configuration = configuration;

                        weatherData[day].vegetationIndex = vegetationIndex;
                        //call the SWELL model
                        modelCall(weatherData[day], parameters);

                        #region if calibrationVariable is phenology

                        if (calibrationVariable == "Phenology")
                        {
                            if (idPixel[id].dateVInorm.ContainsKey(day) && day.Year >= startYear + 1)
                            {
                                simulated_evi.Add(outputT1.vi / 100);
                                measured_evi.Add(idPixel[id].dateVInorm[day]);

                                //this is used to weight less the errors when the NDVI is very low
                                errors_evi.Add(Math.Pow(idPixel[id].dateVInorm[day] - outputT1.vi / 100, 2));
                                
                            }
                            if (day.DayOfYear == 365 && outputT1.egsDOY == 0 && day.Year >= startYear + 1)
                            {
                                errors_evi.Add(999);
                            }
                        }
                        #endregion

                        #region if calibrationVariable is exchanges
                        else
                        {
                            if (calibrationVariable == "Photosynthesis")
                            {
                                for (int h = 0; h < 24; h++)
                                {
                                    DateTime key = day.AddHours(h);

                                    if (idPixel[id].dateGPP.ContainsKey(key) && idPixel[id].dateGPP[key].isCalibration == "x")
                                    {
                                        float gppVal = idPixel[id].dateGPP[key].value;

                                        if (!float.IsNaN(gppVal))
                                        {
                                            simulated_gpp.Add(outputT1.exchanges.GPP[h]);
                                            measured_gpp.Add(gppVal);

                                            float error = (float)Math.Pow(gppVal - outputT1.exchanges.GPP[h], 2);
                                            errors_gpp.Add(error);
                                        }


                                        int hour = key.Hour;

                                        gppObsSum[h] += idPixel[id].dateGPP[key].value;
                                        gppSimSum[h] += outputT1.exchanges.GPP[h];
                                        gppCount[h]++;
                                    }
                                }
                            }
                            if (calibrationVariable == "Respiration")
                            {
                                for (int h = 0; h < 24; h++)
                                {
                                    DateTime key = day.AddHours(h);

                                    if (idPixel[id].dateRECO.ContainsKey(key) && 
                                        !float.IsNaN(idPixel[id].dateRECO[key].value) &&
                                        idPixel[id].dateRECO[key].isCalibration == "x")
                                    {
                                        simulated_reco.Add(outputT1.exchanges.RECO[h]);
                                        measured_reco.Add(idPixel[id].dateRECO[key].value);
                                        errors_reco.Add(Math.Pow(idPixel[id].dateRECO[key].value - outputT1.exchanges.RECO[h], 2));

                                        if (idPixel[id].dateNEE.ContainsKey(key))
                                        {
                                            simulated_nee.Add(outputT1.exchanges.NEE[h]);
                                            measured_nee.Add(idPixel[id].dateNEE[key].value);
                                            errors_nee.Add(Math.Pow(idPixel[id].dateNEE[key].value - 
                                                outputT1.exchanges.NEE[h], 2));


                                            neeObsSum[h] += idPixel[id].dateNEE[key].value;
                                            neeSimSum[h] += outputT1.exchanges.NEE[h];
                                            neeCount[h]++;
                                        }

                                        int hour = key.Hour;

                                        recoObsSum[h] += idPixel[id].dateRECO[key].value;
                                        recoSimSum[h] += outputT1.exchanges.RECO[h];
                                        recoCount[h]++;

                                    }
                                }
                            }
                        }

                        #endregion
                    }
                }


            }

            double pearsonR_evi = 0;
            double RMSE_evi = 0;
            double pearsonR_nee = 0;
            double RMSE_nee = 0;
            double pearsonR_gpp = 0;
            double RMSE_gpp = 0;
            double pearsonR_reco = 0;
            double RMSE_reco = 0;


            if (calibrationVariable == "Phenology")
            {
                if (simulated_evi.Count > 0)
                {
                    pearsonR_evi = Math.Round(ComputePearsonR(measured_evi, simulated_evi), 3);
                    RMSE_evi = Math.Round(Math.Sqrt(errors_evi.Sum() / errors_evi.Count), 3);


                }
                else
                {
                    pearsonR_evi = 0;
                    RMSE_evi = 99;
                }
                //compute objective function
                objFun = (1 - pearsonR_evi) + RMSE_evi;

                Console.Write("\r" + new string(' ', Console.WindowWidth));
                Console.Write("\rpixel {0} : RMSE evi = {1:F3}, r evi = {2:F3}",
                    idPixel.Keys.First(),
                    RMSE_evi, pearsonR_evi);
            }
            else if (calibrationVariable == "Respiration")
            {
                #region Respiration
                float[] recoObsMean = new float[24];
                float[] recoSimMean = new float[24];

                for (int hour = 0; hour < 24; hour++)
                {
                    if (recoCount[hour] > 0)
                    {
                        recoObsMean[hour] = recoObsSum[hour] / recoCount[hour];
                        recoSimMean[hour] = recoSimSum[hour] / recoCount[hour];
                    }
                    else
                    {
                        recoObsMean[hour] = float.NaN;
                        recoSimMean[hour] = float.NaN;
                    }
                }

                double Rcirc = ComputePearsonR(
        recoObsMean.Where(x => !float.IsNaN(x)).ToList(),
        recoSimMean.Where(x => !float.IsNaN(x)).ToList()
    );

                int HourOfMax(float[] x)
                {
                    int hMax = 0;
                    float maxVal = float.MinValue;
                    for (int h = 0; h < 24; h++)
                    {
                        if (!float.IsNaN(x[h]) && x[h] > maxVal)
                        {
                            maxVal = x[h];
                            hMax = h;
                        }
                    }
                    return hMax;
                }

                int hObs = HourOfMax(recoObsMean);
                int hSim = HourOfMax(recoSimMean);

                double phaseError = Math.Abs(hSim - hObs);  // ore
                double phaseErrorNorm = phaseError / 12.0;  // normalizzato

                double MeanRange(float[] x, int h1, int h2)
                {
                    var vals = Enumerable.Range(h1, h2 - h1 + 1)
                        .Select(h => x[h])
                        .Where(v => !float.IsNaN(v));
                    return vals.Average();
                }

                double ampObs = MeanRange(recoObsMean, 8, 18) - MeanRange(recoObsMean, 0, 5);
                double ampSim = MeanRange(recoSimMean, 8, 18) - MeanRange(recoSimMean, 0, 5);

                double ampErrorNorm = Math.Abs(ampSim - ampObs) / Math.Abs(ampObs + 1e-6);

                pearsonR_nee = Math.Round(ComputePearsonR(measured_nee, simulated_nee), 3);
                RMSE_nee = Math.Round(Math.Sqrt(errors_nee.Sum() / errors_nee.Count), 3);

                pearsonR_reco = Math.Round(ComputePearsonR(measured_reco, simulated_reco), 3);
                RMSE_reco = Math.Round(Math.Sqrt(errors_reco.Sum() / errors_reco.Count), 3);
                #endregion

                #region NEE
                float[] neeObsMean = new float[24];
                float[] neeSimMean = new float[24];

                for (int hour = 0; hour < 24; hour++)
                {
                    if (neeCount[hour] > 0)
                    {
                        neeObsMean[hour] = neeObsSum[hour] / neeCount[hour];
                        neeSimMean[hour] = neeSimSum[hour] / neeCount[hour];
                    }
                    else
                    {
                        neeObsMean[hour] = float.NaN;
                        neeSimMean[hour] = float.NaN;
                    }
                }

                double Rcirc_nee = ComputePearsonR(
        neeObsMean.Where(x => !float.IsNaN(x)).ToList(),
        neeSimMean.Where(x => !float.IsNaN(x)).ToList()
    );

                int hObs_nee = HourOfMax(neeObsMean);
                int hSim_nee = HourOfMax(neeSimMean);

                double phaseError_nee = Math.Abs(hSim_nee - hObs_nee);  // ore
                double phaseErrorNorm_nee = phaseError_nee / 12.0;  // normalizzato

                double ampObs_nee = MeanRange(neeObsMean, 8, 18) - MeanRange(neeObsMean, 0, 5);
                double ampSim_nee = MeanRange(neeSimMean, 8, 18) - MeanRange(neeSimMean, 0, 5);

                double ampErrorNorm_nee = Math.Abs(ampSim_nee - ampObs_nee) / Math.Abs(ampObs_nee + 1e-6);





                #endregion

                pearsonR_nee = Math.Round(ComputePearsonR(measured_nee, simulated_nee), 3);
                RMSE_nee = Math.Round(Math.Sqrt(errors_nee.Sum() / errors_nee.Count), 3);

                pearsonR_reco = Math.Round(ComputePearsonR(measured_reco, simulated_reco), 3);
                RMSE_reco = Math.Round(Math.Sqrt(errors_reco.Sum() / errors_reco.Count), 3);

                double objFun_reco = .25f * ((1 - pearsonR_reco) + RMSE_reco) +               
                .25 * (1 - Rcirc) +
                .25 * phaseErrorNorm +
                .25 * ampErrorNorm;

                double objFun_nee = .25f * ((1 - pearsonR_nee) + RMSE_nee) +
                .25 * (1 - Rcirc_nee) +
                .25 * phaseErrorNorm_nee +
                .25 * ampErrorNorm_nee;

                objFun =.5f*objFun_reco + .5f*objFun_nee;

                Console.Write("\r" + new string(' ', Console.WindowWidth));
                Console.Write("\rpixel {0} : " +
                              "RMSE reco = {1:F3}, r reco = {2:F3}, " +
                              "RMSE nee = {3:F3}, r nee = {4:F3}",
                    idPixel.Keys.First(),
                    RMSE_reco, pearsonR_reco,
                    RMSE_nee, pearsonR_nee);

            }
            else if (calibrationVariable == "Photosynthesis")
            {
              
                pearsonR_gpp = Math.Round(ComputePearsonR(measured_gpp, simulated_gpp), 3);
                RMSE_gpp = Math.Round(Math.Sqrt(errors_gpp.Sum() / errors_gpp.Count), 3);

                float[] gppObsMean = new float[24];
                float[] gppSimMean = new float[24];

                for (int hour = 0; hour < 24; hour++)
                {
                    if (gppCount[hour] > 0)
                    {
                        gppObsMean[hour] = gppObsSum[hour] / gppCount[hour];
                        gppSimMean[hour] = gppSimSum[hour] / gppCount[hour];
                    }
                    else
                    {
                        gppObsMean[hour] = float.NaN;
                        gppSimMean[hour] = float.NaN;
                    }
                }

                double Rcirc = ComputePearsonR(
        gppObsMean.Where(x => !float.IsNaN(x)).ToList(),
        gppSimMean.Where(x => !float.IsNaN(x)).ToList()
    );

                int HourOfMax(float[] x)
                {
                    int hMax = 0;
                    float maxVal = float.MinValue;
                    for (int h = 0; h < 24; h++)
                    {
                        if (!float.IsNaN(x[h]) && x[h] > maxVal)
                        {
                            maxVal = x[h];
                            hMax = h;
                        }
                    }
                    return hMax;
                }

                int hObs = HourOfMax(gppObsMean);
                int hSim = HourOfMax(gppSimMean);

                double phaseError = Math.Abs(hSim - hObs);  // ore
                double phaseErrorNorm = phaseError / 12.0;  // normalizzato

                double MeanRange(float[] x, int h1, int h2)
                {
                    var vals = Enumerable.Range(h1, h2 - h1 + 1)
                        .Select(h => x[h])
                        .Where(v => !float.IsNaN(v));
                    return vals.Average();
                }

                double ampObs = MeanRange(gppObsMean, 8, 18) - MeanRange(gppObsMean, 0, 5);
                double ampSim = MeanRange(gppSimMean, 8, 18) - MeanRange(gppSimMean, 0, 5);

                double ampErrorNorm = Math.Abs(ampSim - ampObs) / Math.Abs(ampObs + 1e-6);



                objFun = 1 * ((1 - pearsonR_gpp) + RMSE_gpp) +
                // =====================================================
                // B) STRUTTURA TEMPORALE RECO (30%)
                // =====================================================
                .25 * (1 - Rcirc) +
                .25 * phaseErrorNorm +
                .25 * ampErrorNorm;



                Console.Write("\r" + new string(' ', Console.WindowWidth));
                Console.Write("\rpixel {0} : RMSE gpp = {1:F3}, r gpp = {2:F3}, ",  
                    idPixel.Keys.First(),
                    RMSE_gpp, pearsonR_gpp,
                      .25 * (1 - Rcirc) +
                .25 * phaseErrorNorm +
                .25 * ampErrorNorm);
            }

                //return the objective function
                return objFun;
        }

        public static double ComputePearsonR(List<float> listX, List<float> listY)
        {
            if (listX == null || listY == null || listX.Count != listY.Count || listX.Count == 0)
                throw new ArgumentException("Input lists must be non-null, of equal length, and not empty.");
            
            int n = listX.Count;

            double meanX = listX.Average();
            double meanY = listY.Average();

            double covariance = 0;
            double varianceX = 0;
            double varianceY = 0;

            for (int i = 0; i < n; i++)
            {
                double diffX = listX[i] - meanX;
                double diffY = listY[i] - meanY;

                covariance += diffX * diffY;
                varianceX += diffX * diffX;
                varianceY += diffY * diffY;
            }

            double denominator = Math.Sqrt(varianceX) * Math.Sqrt(varianceY);

            if (denominator == 0)
            {
                return -99;
            }

            return covariance / denominator;
        }


        //this method is called in the validation run
        public void oneShot(Dictionary<string, float> paramValue, out Dictionary<DateTime, output> date_outputs, int parset)
        {

            //reinitialize the date_outputs object
            date_outputs = new Dictionary<DateTime, output>();

            #region assign parameters
            // Get the type of the parameters object
            source.data.parameters parameters = new parameters();
            var _parametersType = parameters.GetType();

            foreach (var param in paramValue.Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                object convertedValue = Convert.ChangeType(paramValue[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);
            }

            foreach (var param in param_outCalibration.Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);


                object convertedValue = Convert.ChangeType(param_outCalibration[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);

            }

            #endregion

            //loop over pixels
            foreach (var id in idPixel.Keys)
            {
                //reinitialize variables for each site
                output = new output();
                outputT1 = new output();

                exchanges = new source.functions.exchanges();

                // Find the closest point
                string closestFile = FindClosestPoint(idPixel[id].latitude, idPixel[id].longitude, allWeatherDataFiles);


                //read weather
                Dictionary<DateTime, input> weatherData = new Dictionary<DateTime, input>();
                if (weatherSource != "Tower")
                {
                    weatherData = weatherReader.readWeatherNASA_ERA5(weatherDir + "//" + closestFile, calibrationVariable, idPixel[id], startYear, weatherSource);
                }
                else
                {
                    weatherData = weatherReader.readWeatherFluxNet(weatherDir + "//" + closestFile, calibrationVariable, idPixel[id], startYear);
                }

                //load calibrated phenology parameters
                #region load parameters
                string dir = outputParametersDir + "_Phenology";

                string mustContain = $"calibParam_{id}_WS_{weatherSource}_";

                string filePath = Directory
                    .GetFiles(dir, "*.csv")
                    .FirstOrDefault(f =>
                        Path.GetFileName(f)
                            .IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) >= 0
                    );

                if (filePath == null)
                {
                    throw new FileNotFoundException(
                        $"No CSV file containing '{mustContain}' found in {dir}"
                    );
                }

                using StreamReader streamReader = new StreamReader(filePath);

                LoadParametersFromFile(
                    filePath,
                    parameters,
                    paramValue,
                    x => !x.Contains("parPhotosynthesis")
                );


                string PhotofilePath = Path.Combine(
    outputParametersDir + "_Photosynthesis",
    $"calibParam_{id}_WS_{weatherSource}_CONFIG_{configuration}.csv"
);
                LoadParametersFromFile(PhotofilePath, parameters,
                    paramValue, x => x.Contains("parPhotosynthesis"));

                string RespfilePath = Path.Combine(
  outputParametersDir + "_Respiration",
  $"calibParam_{id}_WS_{weatherSource}_CONFIG_{configuration}.csv"
);
                LoadParametersFromFile(RespfilePath, parameters,
                    paramValue);

                #endregion

            


                //reinitialize the date_outputs object
                date_outputs = new Dictionary<DateTime, output>();

                //loop over dates
                foreach (var day in weatherData.Keys)
                {
                    if (day.Year >= startYear && day.Year <= endYear)
                    {
                        //assing latitude
                        weatherData[day].latitude = idPixel[id].latitude;
                        weatherData[day].vegetationIndex = vegetationIndex;
                        weatherData[day].simulationSettings.configuration = configuration;

                        //call the SWELL model
                        modelCall(weatherData[day], parameters);

                        //add weather data to output object
                        outputT1.weather.airTemperatureMinimum = weatherData[day].airTemperatureMinimum;
                        outputT1.weather.airTemperatureMaximum = weatherData[day].airTemperatureMaximum;
                        outputT1.weather.precipitation = weatherData[day].precipitation;
                        outputT1.weather.radData.dayLength = weatherData[day].radData.dayLength;
                        outputT1.weather.radData.etr = weatherData[day].radData.etr;
                        outputT1.weather.airTemperatureH = weatherData[day].airTemperatureH;
                        outputT1.weather.referenceET0H = weatherData[day].referenceET0H;
                        outputT1.weather.precipitationH = weatherData[day].precipitationH;
                        outputT1.weather.vaporPressureDeficitH = weatherData[day].vaporPressureDeficitH;
                        outputT1.weather.relativeHumidityH = weatherData[day].relativeHumidityH;
                        outputT1.weather.solarRadiationH = weatherData[day].solarRadiationH;

                        //add the NDVI data
                        if (idPixel[id].dateVInorm.ContainsKey(day))
                        {
                            outputT1.viReference = idPixel[id].dateVInorm[day];
                        }

                        //add the object to the output dictionary
                        date_outputs.Add(day, outputT1);
                    }
                }

                //write the outputs from the calibration run
                writeOutputsCalibration(id, date_outputs, isCalibration);

                if (calibrationVariable != "Phenology")
                {
                    writeOutputsCalibrationHourly(id, date_outputs, isCalibration);
                }
            }
        }


        void LoadParametersFromFile(
      string filePath,
      parameters parameters,
      Dictionary<string, float> paramValue,
      Func<string, bool> classFilter = null
  )
        {
            if (!File.Exists(filePath))
                return;

            using StreamReader streamReader = new StreamReader(filePath);
            streamReader.ReadLine(); // header

            while (!streamReader.EndOfStream)
            {
                string line = streamReader.ReadLine();
                var values = line.Split(',');
                int fullNamePos = 3;
                int valueStrPos = 4;
                if(values.Length > 4 && values[4].StartsWith("par"))
                {
                    fullNamePos = 4;
                    valueStrPos = 5;
                }

                string fullName = values[fullNamePos].Trim();   // es. parRespiration_alpha
                string valueStr = values[valueStrPos].Trim();

                // ✅ REGOLA 2: se ottimizzato, SALTA
                if (paramValue.ContainsKey(fullName))
                    continue;

                string paramClass = fullName.Split('_')[0];
                string propertyName = fullName.Split('_')[1];

                var parametersType = parameters.GetType();
                var classField = parametersType.GetField(paramClass);
                if (classField == null)
                    continue;

                var classInstance = classField.GetValue(parameters);
                if (classInstance == null)
                    continue;

                if (classFilter != null && !classFilter(classInstance.ToString()))
                    continue;

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                if (propertyInfo == null || !propertyInfo.CanWrite)
                    continue;

                object convertedValue =
                    Convert.ChangeType(valueStr, propertyInfo.PropertyType);

                propertyInfo.SetValue(classInstance, convertedValue);
            }
        }


        #region write output files from calibration and validation
        //write outputs from the calibration run
        public void writeOutputsCalibration(string id, Dictionary<DateTime, output> date_outputs, bool isCalibration)
        {
            if (isCalibration)
            {
            #region write outputs
            //empty list to store outputs
            List<string> toWrite = new List<string>();

            #region full output file header
            //define the file header
            //string header = "pixel,date," +
            //"tmax,tmin,prec,dayLength,photoInduction,temperatureInduction," +
            //"dormancyInductionRate,dormancyInductionState,dormancyPercentage," +
            //"endodormancyRate,endodormancyState,endodormancyPercentage," +
            //"ecodormancyRate,ecodormancyState,ecodormancyPercentage," +
            //"growthRate,growthState,growthPercentage," +
            // "greendownRate,greendownState,greendownPercentage," +
            //"declineRate,declineState,declinePercentage," +
            //"NDVIswell_rate,NDVI_swell,reference,phenoCode";
            #endregion

            string header = "source,pixel,group,date,year,doy,tmax,tmin,dayLength,phenoPhase," +
            "dormancyInductionRate,dormancyInductionState,dormancyPercentage," +
            "endodormancyRate,endodormancyState,endodormancyPercentage," +
            "ecodormancyRate,ecodormancyState,ecodormancyPercentage," +
            "growthRate,growthState,growthPercentage," +
            "greendownRate,greendownState,greendownPercentage," +
            "declineRate,declineState,declinePercentage," +
            "SWELL_rate,SWELL,reference";
            //add the header to the list
            toWrite.Add(header);

            #region full output file
            //loop over days
            //foreach (var weather in date_outputs.Keys)
            //{
            //    //empty string to store outputs
            //    string line = "";
            //
            //    //populate this line
            //    line += id + ",";
            //    line += weather.ToString() + ",";
            //    line += date_outputs[weather].weather.airTemperatureMaximum + ",";
            //    line += date_outputs[weather].weather.airTemperatureMinimum + ",";
            //    line += date_outputs[weather].weather.precipitation + ",";
            //    line += date_outputs[weather].weather.radData.dayLength + ",";
            //    line += date_outputs[weather].dormancyInduction.photoperiodDormancyInductionRate + ",";
            //    line += date_outputs[weather].dormancyInduction.temperatureDormancyInductionRate + ",";
            //    line += date_outputs[weather].dormancyInduction.photoThermalDormancyInductionRate + ",";
            //    line += date_outputs[weather].dormancyInduction.photoThermalDormancyInductionState + ",";
            //    line += date_outputs[weather].dormancyInductionPercentage + ",";
            //    line += date_outputs[weather].endodormancy.endodormancyRate + ",";
            //    line += date_outputs[weather].endodormancy.endodormancyState + ",";
            //    line += date_outputs[weather].endodormancyPercentage + ",";
            //    line += date_outputs[weather].ecodormancy.ecodormancyRate + ",";
            //    line += date_outputs[weather].ecodormancy.ecodormancyState + ",";
            //    line += date_outputs[weather].ecodormancyPercentage + ",";
            //    line += date_outputs[weather].growth.growthRate + ",";
            //    line += date_outputs[weather].growth.growthState + ",";
            //    line += date_outputs[weather].growthPercentage + ",";
            //    line += date_outputs[weather].greenDown.greenDownRate + ",";
            //    line += date_outputs[weather].greenDown.greenDownState + ",";
            //    line += date_outputs[weather].greenDownPercentage + ",";
            //    line += date_outputs[weather].decline.declineRate + ",";
            //    line += date_outputs[weather].decline.declineState + ",";
            //    line += date_outputs[weather].declinePercentage + ",";
            //    line += date_outputs[weather].ndviRate + ",";
            //    line += date_outputs[weather].ndvi / 100 + ",";
            //    if (idPixel[id].dateNDVInorm.ContainsKey(weather))
            //    {
            //        line += idPixel[id].dateNDVInorm[weather] + ",";
            //    }
            //    else
            //    {
            //        line += ",";
            //    }
            //    line += date_outputs[weather].phenoCode;
            //
            //    //add the line to the list
            //    toWrite.Add(line);
            //}
            ////save the file
            //System.IO.File.WriteAllLines(outputsCalibrationDir + "//" + id + ".csv", toWrite);
            #endregion

            //TODO: FIX IT
            #region R file output
            foreach (var weather in date_outputs.Keys)
            {
                //empty string to store outputs
                string line = "";

                    //populate this line
                    line += weatherSource + ","; 
                    line += id + ",";
                line += idPixel[id].ecoName + ",";
                line += weather.ToShortDateString() + ",";
                line += weather.Year + ",";
                line += weather.DayOfYear + ",";
                line += date_outputs[weather].weather.airTemperatureMaximum + ",";
                line += date_outputs[weather].weather.airTemperatureMinimum + ",";
                line += Math.Round(date_outputs[weather].weather.radData.dayLength, 3) + ",";
                #region phenocodes
                if (date_outputs[weather].phenoCode == 1)
                {
                    line += "Dormancy induction,";
                }
                else if (date_outputs[weather].phenoCode == 2)
                {
                    line += "Dormancy,";
                }
                else if (date_outputs[weather].phenoCode == 3)
                {
                    line += "Growth,";
                }
                else if (date_outputs[weather].phenoCode == 4)
                {
                    line += "Greendown,";
                }
                else if (date_outputs[weather].phenoCode == 5)
                {
                    line += "Senescence,";
                }
                #endregion
                line += Math.Round(date_outputs[weather].dormancyInduction.photoThermalDormancyInductionRate, 3) + ",";
                line += Math.Round(date_outputs[weather].dormancyInduction.photoThermalDormancyInductionState, 3) + ",";
                line += Math.Round(date_outputs[weather].dormancyInductionPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancy.endodormancyRate, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancy.endodormancyState, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancyPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancy.ecodormancyRate, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancy.ecodormancyState, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancyPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].growth.growthRate, 3) + ",";
                line += Math.Round(date_outputs[weather].growth.growthState, 3) + ",";
                line += Math.Round(date_outputs[weather].growthPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDown.greenDownRate, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDown.greenDownState, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDownPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].decline.declineRate, 3) + ",";
                line += Math.Round(date_outputs[weather].decline.declineState, 3) + ",";
                line += Math.Round(date_outputs[weather].declinePercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].viRate, 3) + ",";
                line += Math.Round(date_outputs[weather].vi / 100, 3) + ",";
                if (idPixel[id].dateVInorm.ContainsKey(weather))
                {
                    line += Math.Round(idPixel[id].dateVInorm[weather], 3);
                }
                else
                {
                    line += "";
                }

                //add the line to the list
                toWrite.Add(line);
            }
                if (isCalibration)
                {
                    //save the file
                    System.IO.File.WriteAllLines(outputsCalibrationDir + "//" + id + "_" + weatherSource + ".csv", toWrite);
                }
                else
                {
                    //save the file
                    System.IO.File.WriteAllLines(outputsValidationDir + "//" + id + "_" + weatherSource + ".csv", toWrite);
                }
                #endregion


                #endregion
            }
        }

        public void writeOutputsCalibrationHourly(string id, Dictionary<DateTime, output> date_outputs, 
            bool isCalibration)
        {
            if (isCalibration)
            {
                #region write outputs
                //empty list to store outputs
                List<string> toWrite = new List<string>();

                string header = "pixel,source,configuration,date,year,doy,hour," +
                   // "t,p,sw,rh,vpd,et0," +
                    "phenoPhase,SWELL,reference,vegetationCover,"+
                "TR,PARscale,waterStress,PhenologyScale,VPDscale," +
                "halfSat,QY,GPP,GPPReference," +
                "TscaleRECO,PhenoRECO,RECO_TWS,RECOgpp,CUE,metActPhoto,metActReco," +
                "RECO,RECOReference," +
                "NEE,NEEReference,calibration," +
                "FastPool,SlowPool";
                //add the header to the list
                toWrite.Add(header);
            
                //TODO: FIX IT
                #region R file output
                foreach (var weather in date_outputs.Keys)
                {
                    for (int hour = 0; hour < 24; hour++)
                    {
                        var w = date_outputs[weather];
                        var ex = w.exchanges;
                        var wd = w.weather;

                        string fmt(float v) => float.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
                        bool isCalib =
    idPixel[id].dateNEE.ContainsKey(weather.AddHours(hour)) &&
    !string.IsNullOrWhiteSpace(
        idPixel[id].dateNEE[weather.AddHours(hour)].isCalibration
    );

                    

                        string line =                             
                              $"{id}," +
                               $"{weatherSource}," +
                                $"{configuration}," +
                            $"{weather:yyyy-MM-dd}," +
                            $"{weather.Year}," +
                            $"{weather.DayOfYear}," +
                            $"{hour + 1}," +

                            // --- Weather ---
                            //$"{fmt(wd.airTemperatureH[hour])}," +
                            //$"{fmt(wd.precipitationH[hour])}," +
                            //$"{fmt(wd.solarRadiationH[hour])}," +
                            //$"{fmt(wd.relativeHumidityH[hour])}," +
                            //$"{fmt(wd.vaporPressureDeficitH[hour])}," +
                            //$"{fmt(wd.referenceET0H[hour])}," +

                            // --- Phenocode ---
                            (w.phenoCode switch
                            {
                                1 => "Dormancy induction,",
                                2 => "Dormancy,",
                                3 => "Growth,",
                                4 => "Greendown,",
                                5 => "Senescence,",
                                _ => ","
                            }) +

                            // --- Vegetation indices ---
                            $"{fmt(w.vi / 100)}," +
                            $"{(idPixel[id].dateVInorm.ContainsKey(weather) ? fmt(idPixel[id].dateVInorm[weather]) : "")}," +
                            //$"{fmt(ex.viHourly[hour])}," +
                            $"{fmt(ex.vegetationCover)}," +
                            $"{fmt(ex.temperatureScale[hour])}," +
                            $"{fmt(ex.PARscale[hour])}," +
                            $"{fmt(ex.Wscale[hour])}," +
                            $"{fmt(ex.phenologyScale)}," +
                            $"{fmt(ex.vpdScale[hour])}," +
                            $"{fmt(ex.halfSaturation[hour])}," +
                            $"{fmt(ex.QY[hour])}," +
                            $"{fmt(ex.GPP[hour])}," +


                            // --- Fluxes ---

                            $"{(idPixel[id].dateGPP.ContainsKey(weather.AddHours(hour)) ? fmt(idPixel[id].dateGPP[weather.AddHours(hour)].value) : "")}," +
                            $"{fmt(ex.TscaleReco[hour])}," +
                            $"{fmt(ex.PhenologyscaleReco[hour])}," +
                            $"{fmt(ex.recoTandWS[hour])}," +
                            $"{fmt(ex.recoGPP[hour])}," +
                            $"{fmt(ex.CUE[hour])}," +
                            $"{fmt(ex.metActivationPhoto[hour])}," +
                            $"{fmt(ex.metActivationReco[hour])}," +
                            $"{fmt(ex.RECO[hour])}," +
                            $"{(idPixel[id].dateRECO.ContainsKey(weather.AddHours(hour)) ? fmt(idPixel[id].dateRECO[weather.AddHours(hour)].value) : "")}," +
                            $"{fmt(ex.NEE[hour])}," +
                            $"{(idPixel[id].dateNEE.ContainsKey(weather.AddHours(hour)) ? fmt(idPixel[id].dateNEE[weather.AddHours(hour)].value) : "")}," +
                            $"{(isCalib ? 1 : 0)}," +
                            $"{fmt(ex.fastPoolSeries[hour])}," +
                            $"{fmt(ex.slowPoolSeries[hour])}";


                        toWrite.Add(line);
                    }


                }
                #endregion

                #region write file

                if (isCalibration)
                {
                   string path = outputsCalibrationDir + "_exchanges//" + id + "_WS_" + weatherSource + 
                        "_CONFIG_" + configuration + ".csv";
                    string directory = Path.GetDirectoryName(path);

                    // Check if the directory exists, if not, create it
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    // Write the file
                    System.IO.File.WriteAllLines(path, toWrite);
                }
                else
                {
                    string path = outputsValidationDir + "_exchanges//" + id + "_" + weatherSource + ".csv";
                    string directory = Path.GetDirectoryName(path);

                    // Check if the directory exists, if not, create it
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    // Write the file
                    System.IO.File.WriteAllLines(path, toWrite);
                }
                #endregion

                #endregion
            }
        }
        //write outputs from the validation run
        public void writeOutputsValidation(string id, Dictionary<DateTime, output> date_outputs, 
            Dictionary<DateTime, Dictionary<string, float>> ndviSimulations)
        {
            #region write outputs
            //empty list of strings to store outputs
            List<string> toWrite = new List<string>();

            // Define a base header
            string header = "pixel,group,date,year,doy,tmax,tmin,dayLength,phenoPhase," +
                "dormancyInductionRate,dormancyInductionState,dormancyPercentage," +
                "endodormancyRate,endodormancyState,endodormancyPercentage," +
                "ecodormancyRate,ecodormancyState,ecodormancyPercentage," +
                "growthRate,growthState,growthPercentage," +
                "greendownRate,greendownState,greendownPercentage," +
                "declineRate,declineState,declinePercentage," +
                "SWELL_rate,SWELL,reference,SWELL_10,SWELL_25,SWELL_40,SWELL_60,SWELL_75,SWELL_90";
          

            // Add the header to the list
            toWrite.Add(header);

            // Loop over days
            foreach (var weather in date_outputs.Keys)
            {
                // Empty first line
                string line = "";

                //populate this line
                line += id + ",";
                line += idPixel[id].ecoName + ",";
                line += weather.ToShortDateString() + ",";
                line += weather.Year + ",";
                line += weather.DayOfYear + ",";
                line += date_outputs[weather].weather.airTemperatureMaximum + ",";
                line += date_outputs[weather].weather.airTemperatureMinimum + ",";
                line += Math.Round(date_outputs[weather].weather.radData.dayLength, 3) + ",";
                #region phenocodes
                if (date_outputs[weather].phenoCode < 2)
                {
                    line += "Dormancy induction,";
                }
                else if (date_outputs[weather].phenoCode < 3)
                {
                    line += "Dormancy,";
                }
                else if (date_outputs[weather].phenoCode < 4)
                {
                    line += "Growth,";
                }
                else if (date_outputs[weather].phenoCode < 5)
                {
                    line += "Greendown,";
                }
                else 
                {
                    line += "Senescence,";
                }
                
                #endregion
                line += Math.Round(date_outputs[weather].dormancyInduction.photoThermalDormancyInductionRate, 3) + ",";
                line += Math.Round(date_outputs[weather].dormancyInduction.photoThermalDormancyInductionState, 3) + ",";
                line += Math.Round(date_outputs[weather].dormancyInductionPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancy.endodormancyRate, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancy.endodormancyState, 3) + ",";
                line += Math.Round(date_outputs[weather].endodormancyPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancy.ecodormancyRate, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancy.ecodormancyState, 3) + ",";
                line += Math.Round(date_outputs[weather].ecodormancyPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].growth.growthRate, 3) + ",";
                line += Math.Round(date_outputs[weather].growth.growthState, 3) + ",";
                line += Math.Round(date_outputs[weather].growthPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDown.greenDownRate, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDown.greenDownState, 3) + ",";
                line += Math.Round(date_outputs[weather].greenDownPercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].decline.declineRate, 3) + ",";
                line += Math.Round(date_outputs[weather].decline.declineState, 3) + ",";
                line += Math.Round(date_outputs[weather].declinePercentage, 3) + ",";
                line += Math.Round(date_outputs[weather].viRate, 3) + ",";
                line += Math.Round(date_outputs[weather].vi / 100, 3) + ",";
                if (idPixel[id].dateVInorm.ContainsKey(weather))
                {
                    line += Math.Round(idPixel[id].dateVInorm[weather], 3) + ",";
                }
                else
                {
                    line += ",";
                }

                // Add NDVI simulation values
                if (ndviSimulations.ContainsKey(weather))
                {
                    line += Math.Round(ndviSimulations[weather]["10th"] / 100, 3) + ",";
                    line += Math.Round(ndviSimulations[weather]["25th"] / 100, 3) + ",";
                    line += Math.Round(ndviSimulations[weather]["40th"] / 100, 3) + ",";
                    line += Math.Round(ndviSimulations[weather]["60th"] / 100, 3) + ",";
                    line += Math.Round(ndviSimulations[weather]["75th"] / 100, 3) + ",";
                    line += Math.Round(ndviSimulations[weather]["90th"] / 100, 3);
                }
               

                // Trim trailing comma (optional)
                line = line.TrimEnd(',');

                // Add the line to the list
                toWrite.Add(line);
            }

            // Create the directory if it doesn't exist
            string dirPath = outputsValidationDir  + "//";
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            //save the file
            System.IO.File.WriteAllLines(dirPath + id  + ".csv", toWrite);
            #endregion
        }
        #endregion

        //call the SWELL functions
        public void modelCall(input weatherData, parameters parameters)
        {
            //pass values from the previous day
            output = outputT1;
            outputT1 = new output();
            outputT1.exchanges.ET0memory = output.exchanges.ET0memory;
            outputT1.exchanges.PrecipitationMemory = output.exchanges.PrecipitationMemory;
            // ======================================================
            // PASS AUTOTROPHIC RESPIRATION STATE (TWO-POOL)
            // ======================================================
            outputT1.exchanges.fastPool = output.exchanges.fastPool;
            outputT1.exchanges.slowPool= output.exchanges.slowPool;

            outputT1.exchanges.vegetationCover = output.exchanges.vegetationCover;

            //call the functions
            //dormancy season
            dormancy.induction(weatherData, parameters, output, outputT1);
            dormancy.endodormancy(weatherData, parameters, output, outputT1);
            dormancy.ecodormancy(weatherData, parameters, output, outputT1);
            //growing season
            growing.growthRate(weatherData, parameters, output, outputT1);
            growing.greendownRate(weatherData, parameters, output, outputT1);
            growing.declineRate(weatherData, parameters, output, outputT1);
            //NDVI dynamics
            VIdynamics.ndviNormalized(weatherData, parameters, output, outputT1);
            if (calibrationVariable != "Phenology")
            {
                exchanges.VPRM(weatherData, parameters, output, outputT1);
            }

        }

        #region associate the correct grid weather to the corresponding remote sensing pixel
        //find the nearest weather grid with respect to pixel latitude and longitude
        private string FindClosestPoint(
      double targetLatitude,
      double targetLongitude,
      List<string> fileNames)
        {
            double closestDistance = double.MaxValue;
            string closestFileName = null;

            foreach (string fileName in fileNames)
            {
                try
                {
                    string fileFullName = weatherDir + "\\" + fileName;
                    using (var reader = new StreamReader(fileFullName))
                    {
                        // 1. Leggi header
                        string headerLine = reader.ReadLine().Replace("\"", "");
                        if (headerLine == null) continue;

                        string[] headers = headerLine.Split(',');

                        // normalize to lowercase
                        string[] headersLower = headers
                            .Select(h => h.Trim().ToLowerInvariant())
                            .ToArray();

                        // accepted aliases
                        string[] latAliases = { "lat", "latitude" };
                        string[] lonAliases = { "lon", "longitude", "long" };

                        int latIndex = Array.FindIndex(headersLower,
                            h => latAliases.Contains(h));

                        int lonIndex = Array.FindIndex(headersLower,
                            h => lonAliases.Contains(h));

                        if (latIndex == -1 || lonIndex == -1)
                            throw new Exception("LAT/LON columns not found in file.");

                        
                        // 2. Leggi prima riga dati
                        string dataLine = reader.ReadLine();
                        if (dataLine == null) continue;

                        string[] values = dataLine.Split(',');

                        double latitude = double.Parse(
                            values[latIndex],
                            CultureInfo.InvariantCulture
                        );

                        double longitude = double.Parse(
                            values[lonIndex],
                            CultureInfo.InvariantCulture
                        );

                        // 3. Distanza Haversine
                        double distance = CalculateDistance(
                            targetLatitude,
                            targetLongitude,
                            latitude,
                            longitude
                        );

                        // 4. Aggiorna il più vicino
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestFileName = fileName;
                        }
                    }
                }
                catch
                {
                    // ignora file corrotti o non leggibili
                    continue;
                }
            }

            return closestFileName;
        }
        //calculate distance between pixel and weather grid centroids
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Radius of the Earth in kilometers
            const double R = 6371;
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        //conversion to radians
        static double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }
        #endregion
    }
}
