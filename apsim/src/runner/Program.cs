using UNIMI.optimizer;
using source.data;
using runner;
using runner.data;
using MathNet.Numerics.Distributions;
using System.Text.Json;
using System.Collections.Concurrent;


#region read the configuration file (SWELLConfig.config)
// Check if the config file path is passed as an argument
string configFilePath = string.Empty;

if (args.Length > 0)
{
    // If an argument is provided, use it as the path to the config file
    configFilePath = args[0];
}
else
{
    // Fallback to default directory if not passed
    // Use the current directory or modify this to a predefined path
    configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "BreathConfig.json");
}

// Check if the configuration file exists
if (!File.Exists(configFilePath))
{
    Console.WriteLine($"Error: The configuration file was not found at {configFilePath}");
    return;  // Exit if the file is not found
}

// Read the JSON configuration file
string jsonString = File.ReadAllText(configFilePath);
var config = JsonSerializer.Deserialize<root>(jsonString);

//Console.WriteLine("Configuration loaded from: " + configFilePath);

//switch between calibration and validation
bool isCalibration = bool.Parse(config.settings.calibration.ToString());
List<string> calibrationVariable = config.settings.calibrationVariable;
List<string> configuration = config.settings.configuration;
List<string> weatherSource = config.settings.weatherSource;

string runningMode = "";
if (isCalibration) runningMode = "calibration"; else runningMode = "validation";

Console.WriteLine("RUNNING MODE: {0}", runningMode);

//set start pixel and number of pixels (for calibration)
List<string> pixelsRun = config?.settings?.pixelsRun ?? new List<string>();

int simplexes = int.Parse(config.settings.simplexes.ToString());
int iterations = int.Parse(config.settings.iterations.ToString());

int validationReplicates = int.Parse(config.settings.validationReplicates.ToString());
string parametersDistribution = config.settings.parametersDistributions.ToString();

if (isCalibration)
{
    Console.WriteLine("SIMPLEXES: {0} ITERATIONS: {1}", simplexes, iterations);
}
else
{
    Console.WriteLine("REPLICATES: {0} DISTRIBUTION: {1}", validationReplicates, parametersDistribution);
}


//set weather directory
var weatherDirBase = config.settings.weatherDirectory;
//Console.WriteLine("WEATHER DIRECTORY: {0}", weatherDir);


//set parameters file
string parametersDataFile = config.settings.parametersDataFile.ToString();
string referenceDataFile = config.settings.referenceDataFile.ToString();
string parametersValidationFile = config.settings.parametersValidationFile.ToString();

//set start and end year
int startYear = int.Parse(config.settings.startYear.ToString());
int endYear = int.Parse(config.settings.endYear.ToString());

string outputsCalibrationDir = config.settings.outputCalibrationDir.ToString();
string outputsValidationDir = config.settings.outputValidationDir.ToString();
string outputParametersDir = config.settings.outputParametersDir.ToString();

string vegetationIndex = config.settings.vegetationIndex.ToString();

string referenceFluxesDir = config.settings.referenceFluxesDir.ToString();

#endregion

#region read reference NDVI data
//message to console
//Console.WriteLine("reading all pixels NDVI data....");

//instance of reference reader class
referenceReader referenceReader = new referenceReader();

//read simulated pixels
Dictionary<string, pixel> allPixels = referenceReader.readReferenceData(referenceDataFile);
#endregion


#region get all weather files
//message to console
Console.WriteLine("reading weather files....");
//optimizer class
optimizer optimizer = new optimizer();
optimizer.startYear = startYear;
optimizer.endYear = endYear;
optimizer.outputsCalibrationDir = outputsCalibrationDir;
optimizer.outputsValidationDir = outputsValidationDir;
optimizer.outputParametersDir = outputParametersDir;
optimizer.vegetationIndex = vegetationIndex;
optimizer.referenceFluxesDir = referenceFluxesDir;

#endregion


#region read SWELL parameter files

//list of already calibrated files
var calibratedFilesInfo = new DirectoryInfo(outputsCalibrationDir).GetFiles();

// Convert FileInfo[] to List<string>
List<string> calibratedFiles = calibratedFilesInfo.Select(file => Path.GetFileNameWithoutExtension(file.FullName)).ToList();



//data structure to store calibrated parameters
Dictionary<string, float> paramCalibValue = new Dictionary<string, float>();
#endregion

#region switch between calibration and validation
if (isCalibration)
{
    foreach (var pixel in pixelsRun)
    {
        referenceReader = new referenceReader();
        string fluxesDir = referenceFluxesDir;
        //set pixel to calibrate
        optimizer.idPixel = allPixels.Where(x => x.Key == pixel).ToDictionary(p => p.Key, p => p.Value); ;

        referenceReader.readReferenceDataFluxes(fluxesDir, optimizer.idPixel[pixel]);


        foreach (var calVar in calibrationVariable)
        {
            foreach (var conf in configuration)
            {
                foreach (var wSource in weatherSource)
                {
                    //read parameter file with limits
                    paramReader paramReader = new paramReader();
                    string parDataFile = parametersDataFile + "_" + calVar + ".csv";
                    optimizer.nameParam = paramReader.read(parDataFile);


                    string weatherDir = weatherDirBase + "\\" + wSource;
                    optimizer.weatherDir = weatherDir.ToString();
                    //read weather files
                    var weatherFiles = new DirectoryInfo(weatherDir.ToString()).GetFiles();

                    // Convert FileInfo[] to List<string>
                    optimizer.allWeatherDataFiles = weatherFiles.Select(file => Path.GetFileName(file.FullName)).ToList();


                    //set weather source
                    optimizer.weatherSource = wSource;

                    //set optimizer properties
                    optimizer.configuration = conf;


                    #region SWELL calibration

                    #region define optimizer settings
                    //optimizer instance
                    MultiStartSimplex msx = new MultiStartSimplex();

                    msx.NofSimplexes = simplexes;// 19; //5;
                    msx.Ftol = 0.000001;
                    msx.Itmax = iterations;// 999;

                    #endregion

                    #region loop over pixels

                    Console.WriteLine("SITE: {0} SOURCE:{1} VARIABLE: {2} CONFIGURATION: {3}",
                    pixel, wSource, calVar, conf);

                    #region define parameters settings for calibration
                    //count parameters under calibration
                    int paramCalibrated = 0;
                    //loop over parameters
                    foreach (var name in optimizer.nameParam.Keys)
                    {
                        //add parameter to calibration if calibration field is not empty (x)
                        if (optimizer.nameParam[name].calibration != "") { paramCalibrated++; }

                    }
                    //set number of dimension in the matrix
                    double[,] Limits = new double[paramCalibrated, 2];

                    //parameters out of calibration
                    Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
                    //populate limits 
                    //count parameters under calibration
                    int i = 0;
                    foreach (var name in optimizer.nameParam.Keys)
                    {
                        if (optimizer.nameParam[name].calibration != "")
                        {
                            Limits[i, 1] = optimizer.nameParam[name].maximum;
                            Limits[i, 0] = optimizer.nameParam[name].minimum;
                            i++;
                        }
                        else
                        {
                            param_outCalibration.Add(name, optimizer.nameParam[name].value);
                        }
                    }
                    double[,] results = new double[1, 1];
                    #endregion

                    //set optimizer calibration properties
                    optimizer.isCalibration = isCalibration;
                    optimizer.param_outCalibration = param_outCalibration;
                    optimizer.calibrationVariable = calVar;


                    //run optimizer
                    if (paramCalibrated > 0)
                    {

                        msx.Multistart(optimizer, paramCalibrated, Limits, out results);

                        //get calibrated parameters
                        paramCalibValue = new Dictionary<string, float>();
                        int count = 0;

                        #region write calibrated parameters

                        string header = "pixelID,wSource,configuration,param,value";

                        // directory
                        string directoryPath = outputParametersDir + "_" + calVar;
                        Directory.CreateDirectory(directoryPath);

                        string calibFileName = $"calibParam_{pixel}_WS_{wSource}_CONFIG_{conf}.csv";
                        string filePath = Path.Combine(directoryPath, calibFileName);

                        // lock per filePath (thread-safe, condiviso a livello processo)
                        object lockObj = FileLockRegistry.Locks.GetOrAdd(filePath, _ => new object());

                        lock (lockObj)
                        {
                            // param -> value
                            Dictionary<string, float> paramValues = new Dictionary<string, float>();

                            // 1) Se il file esiste, carica i parametri già presenti
                            if (File.Exists(filePath))
                            {
                                using FileStream fs = new FileStream(
                                    filePath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read);

                                using StreamReader sr = new StreamReader(fs);

                                // salta header
                                _ = sr.ReadLine();

                                while (!sr.EndOfStream)
                                {
                                    string line = sr.ReadLine();
                                    if (string.IsNullOrWhiteSpace(line)) continue;

                                    var parts = line.Split(',');
                                    if (parts.Length < 5) continue;

                                    string paramName = parts[3].Trim();
                                    if (float.TryParse(parts[4], out float oldVal))
                                    {
                                        paramValues[paramName] = oldVal;
                                    }
                                }
                            }

                            // 2) Aggiorna / aggiungi SOLO i parametri calibrati
                            count = 0;
                            foreach (var thisParam in optimizer.nameParam.Keys)
                            {
                                if (optimizer.nameParam[thisParam].calibration != "")
                                {
                                    float value = (float)Math.Round(results[0, count], 3);

                                    // se esiste, sovrascrive; se non esiste, aggiunge
                                    paramValues[thisParam] = value;
                                    paramCalibValue[thisParam] = value;

                                    count++;
                                }
                            }

                            // 3) Riscrittura atomica del file
                            using FileStream fsw = new FileStream(
                                filePath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.Read);

                            using StreamWriter sw = new StreamWriter(fsw);

                            sw.WriteLine(header);

                            foreach (var kvp in paramValues)
                            {
                                sw.WriteLine(
                                      pixel + "," +
                                      wSource + "," +

                                    conf + "," +
                                    kvp.Key + "," +
                                    kvp.Value
                                );
                            }
                        }

                        #endregion


                    #endregion
                    }
                    else
                    {
                        #region read calibrated parameters

                        // Example: read all calibrated parameter files from directory
                        string directoryPath = $"{outputParametersDir}_{calibrationVariable}";

                        string[] calibFiles = Directory.GetFiles(
                            directoryPath,
                            $"calibParam_*_{wSource}_{config}.csv"
                        );

                        // Dictionary to hold pixelID → param → value
                        Dictionary<int, Dictionary<string, float>> param_value = new Dictionary<int, Dictionary<string, float>>();

                        foreach (string file in calibFiles)
                        {
                            // Read all lines, skip header
                            var lines = File.ReadAllLines(file).Skip(1);

                            foreach (var line in lines)
                            {
                                var parts = line.Split(',');
                                if (parts.Length < 4) continue;

                                string ecoRegion = parts[0]; // optional if you need it
                                string thisParam = parts[4];
                                float value = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);

                                // Initialize nested dictionary if needed
                                if (!paramCalibValue.ContainsKey(thisParam))
                                    paramCalibValue.Add(thisParam, value);

                            }
                        }

                        #endregion

                    }



                    //empty dictionary of dates and outputs objects
                    var dateOutputs = new Dictionary<DateTime, output>();
                    //execute model with calibrated parameters
                    optimizer.oneShot(paramCalibValue, out dateOutputs, optimizer.parset);

                    //message to console
                    Console.WriteLine("pixel {0} calibrated", pixel);


                }
                    #endregion


            }
        }


    }
}
else
{
    //message to console
    Console.WriteLine("selecting pixels for validation...");
    Dictionary<string, pixel> allPixelsToValidate = referenceReader.readReferenceData(referenceDataFile);

    //message to console
    Console.WriteLine("validation loop starts...");

    List<string> availableGroups = new List<string>();
    StreamReader sr = new StreamReader(parametersValidationFile);
    sr.ReadLine();
    while (!sr.EndOfStream)
    {
        string group = sr.ReadLine().Split(',')[0];
        if (!availableGroups.Contains(group))
        {
            availableGroups.Add(group);
        }
    }


    #region loop over pixels 
    foreach (var pixel in allPixelsToValidate.Keys)
    {

        //check if the pixels has been already validated
        if (!availableGroups.Contains(allPixelsToValidate[pixel].ecoName))
        {
            Console.WriteLine("pixel {0} not run as {1} is not present in the calibrated file", pixel,
                allPixelsToValidate[pixel].ecoName);
        }
        else
        {
            //message to console
            //Console.WriteLine("pixel {0} start", pixel);

            //get calibrated parameters
            paramCalibValue = new Dictionary<string, float>();

            //get mean and standard deviation for each parameter
            Dictionary<string, List<float>> param_meanSD = new Dictionary<string, List<float>>();

            #region assign default parameters (out of calibration subset)
            //parameters out of calibration
            Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();

            //populate parameters out of calibration
            foreach (var name in optimizer.nameParam.Keys)
            {
                if (optimizer.nameParam[name].calibration == "")
                {
                    //add parameter to calibration if calibration field is not empty (x)
                    param_outCalibration.Add(name, optimizer.nameParam[name].value);
                }
            }

            optimizer.param_outCalibration = param_outCalibration;
            #endregion

            //set optimizer properties
            optimizer.isCalibration = isCalibration;

            //set pixel to calibrate
            Dictionary<string, pixel> keyValuePairs = new Dictionary<string, pixel>();

            //add pixel to dictionary
            keyValuePairs.Add(pixel, allPixelsToValidate[pixel]);
            optimizer.idPixel = keyValuePairs;

            //structure to store outputs from each parset
            var parsetOutputs = new Dictionary<int, Dictionary<DateTime, output>>();

            #region loop over calibrated parsets for each pixel
            for (int parset = 0; parset < validationReplicates; parset++)
            {
                //message to console
                //Console.WriteLine("pixel {0}, parset {1} run", pixel, parset);

                //reinitialize calibrated parameters
                paramCalibValue = new Dictionary<string, float>();

                //read calibrated parameters
                sr = new StreamReader(parametersValidationFile);
                sr.ReadLine();

                //read calibrated parameters and sample from distribution
                while (!sr.EndOfStream)
                {
                    string[] line = sr.ReadLine().Split(',', '"');
                    if (allPixelsToValidate[pixel].ecoName == line[0])
                    {
                        string sd = line[7];
                        if (line[7] == "NA")
                        {
                            sd = (float.Parse(line[7]) * 0.05F).ToString();
                        }

                        float randomSample = 0;
                        if (parametersDistribution == "uniform")
                        {
                            //uniform distribution from median +- one standard deviation
                            ContinuousUniform uniformDistribution = new ContinuousUniform(float.Parse(line[6]) - Math.Abs(double.Parse(sd)),
                                float.Parse(line[6]) + Math.Abs(double.Parse(sd)));
                            //sample one parset
                            randomSample = (float)uniformDistribution.Sample();
                        }
                        else if (parametersDistribution == "normal")
                        {
                            Normal normal = new Normal(float.Parse(line[6]), double.Parse(sd));
                            //sample one parset
                            randomSample = (float)normal.Sample();
                        }
                        paramCalibValue.Add(line[2] + "_" + line[3], randomSample);
                    }

                }
                sr.Close();

                optimizer.parset = parset;

                //empty list of dates and SWELL outputs
                var dateOutputs = new Dictionary<DateTime, output>();

                //run SWELL in validation (from optimizer class)
                optimizer.oneShot(paramCalibValue, out dateOutputs, parset);

                parsetOutputs.Add(parset, dateOutputs);
            }
            #endregion

            // Flatten the nested dictionary and group by DateTime
            var flattenedAndGrouped = parsetOutputs.SelectMany(kv => kv.Value).GroupBy(kv => kv.Key);


            // Dictionary to hold the results
            Dictionary<DateTime, output> datePixelMeans = new Dictionary<DateTime, output>();

            // Dictionary to store all NDVI simulations for each date
            var ndviSimulations = new Dictionary<DateTime, Dictionary<string, float>>();

            // Messaging to console
            Console.WriteLine("Compute synthetic outputs per pixel {0}, group {1}", pixel,
                allPixelsToValidate[pixel].ecoName);

            // Loop over dates and outputs
            foreach (var group in flattenedAndGrouped)
            {
                // Define pixel mean output instance
                output pixelOutput = new output();

                // Get the date
                DateTime date = group.Key;

                // Compute median for each output except NDVI
                pixelOutput.phenoCode = Median(group.Select(kv => kv.Value.phenoCode)); // Assuming phenoCode is averaged
                pixelOutput.weather.date = date;
                pixelOutput.weather.airTemperatureMaximum = Median(group.Select(kv => kv.Value.weather.airTemperatureMaximum));
                pixelOutput.weather.airTemperatureMinimum = Median(group.Select(kv => kv.Value.weather.airTemperatureMinimum));
                pixelOutput.weather.radData.dayLength = Median(group.Select(kv => kv.Value.weather.radData.dayLength));
                pixelOutput.dormancyInduction.photoThermalDormancyInductionRate =
                    Median(group.Select(kv => kv.Value.dormancyInduction.photoThermalDormancyInductionRate));
                pixelOutput.dormancyInduction.photoThermalDormancyInductionState =
                    Median(group.Select(kv => kv.Value.dormancyInduction.photoThermalDormancyInductionState));
                pixelOutput.dormancyInductionPercentage = Median(group.Select(kv => kv.Value.dormancyInductionPercentage));
                pixelOutput.endodormancy.endodormancyRate = Median(group.Select(kv => kv.Value.endodormancy.endodormancyRate));
                pixelOutput.endodormancy.endodormancyState = Median(group.Select(kv => kv.Value.endodormancy.endodormancyState));
                pixelOutput.endodormancyPercentage = Median(group.Select(kv => kv.Value.endodormancyPercentage));
                pixelOutput.ecodormancy.ecodormancyRate = Median(group.Select(kv => kv.Value.ecodormancy.ecodormancyRate));
                pixelOutput.ecodormancy.ecodormancyState = Median(group.Select(kv => kv.Value.ecodormancy.ecodormancyState));
                pixelOutput.ecodormancyPercentage = Median(group.Select(kv => kv.Value.ecodormancyPercentage));
                pixelOutput.growth.growthRate = Median(group.Select(kv => kv.Value.growth.growthRate));
                pixelOutput.growth.growthState = Median(group.Select(kv => kv.Value.growth.growthState));
                pixelOutput.growthPercentage = Median(group.Select(kv => kv.Value.growthPercentage));
                pixelOutput.greenDown.greenDownRate = Median(group.Select(kv => kv.Value.greenDown.greenDownRate));
                pixelOutput.greenDown.greenDownState = Median(group.Select(kv => kv.Value.greenDown.greenDownState));
                pixelOutput.greenDownPercentage = Median(group.Select(kv => kv.Value.greenDownPercentage));
                pixelOutput.decline.declineRate = Median(group.Select(kv => kv.Value.decline.declineRate));
                pixelOutput.decline.declineState = Median(group.Select(kv => kv.Value.decline.declineState));
                pixelOutput.declinePercentage = Median(group.Select(kv => kv.Value.declinePercentage));
                pixelOutput.viReference = Median(group.Select(kv => kv.Value.viReference));
                pixelOutput.vi = Median(group.Select(kv => kv.Value.vi));

                // Handle NDVI separately: Store all simulations
                if (!ndviSimulations.ContainsKey(date))
                {
                    ndviSimulations.Add(date, new Dictionary<string, float>());
                    ndviSimulations[date].Add("10th", CalculatePercentile(group.Select(kv => kv.Value.vi), 10));
                    ndviSimulations[date].Add("25th", CalculatePercentile(group.Select(kv => kv.Value.vi), 25));
                    ndviSimulations[date].Add("40th", CalculatePercentile(group.Select(kv => kv.Value.vi), 40));
                    ndviSimulations[date].Add("60th", CalculatePercentile(group.Select(kv => kv.Value.vi), 60));
                    ndviSimulations[date].Add("75th", CalculatePercentile(group.Select(kv => kv.Value.vi), 75));
                    ndviSimulations[date].Add("90th", CalculatePercentile(group.Select(kv => kv.Value.vi), 90));
                }

                // Add the pixel output to the results dictionary
                datePixelMeans.Add(date, pixelOutput);
            }


            // group by DateTime
            var groupedByDate = parsetOutputs.GroupBy(kv => kv.Value);

            //write outputs in validation
            optimizer.writeOutputsValidation(pixel, datePixelMeans, ndviSimulations);
            //}
        }
        #endregion


        #endregion
    }
}



#region compute statistics from the multiple validation runs
static float CalculatePercentile(IEnumerable<float> values, int percentile)
        {
            // Ensure the values are sorted
            var sortedValues = values.OrderBy(v => v).ToList();

            // Calculate the index of the percentile
            int index = (int)Math.Ceiling((percentile / 100.0) * sortedValues.Count) - 1;

            // Return the value at the calculated index
            return sortedValues[index];
        }

        // Median function
        static float Median(IEnumerable<float> values)
{
    List<float> sortedValues = values.OrderBy(x => x).ToList();
    int count = sortedValues.Count;

    if (count % 2 == 0)
    {
        // Even number of elements, average the middle two
        return (sortedValues[count / 2 - 1] + sortedValues[count / 2]) / 2.0F;
    }
    else
    {
        // Odd number of elements, return the middle one
        return sortedValues[count / 2];
    }
}
        #endregion
    

#region settings from json

public class root
{
    public settings? settings { get; set; }

}

//contains the parameters in the json configuration file
public class settings
{
    public object startYear { get; set; }
    public object endYear { get; set; }
    public object calibration { get; set; }
    public object simplexes { get; set; }
    public object iterations { get; set; }
    public object georeferencingFile { get; set; }
    public object referenceDataFile { get; set; }
    public object referenceFluxesDir { get; set; }
    public object parametersDataFile { get; set; }
    public object weatherDirectory { get; set; }
    public List<string> weatherSource { get; set; }
    public List<string> calibrationVariable { get; set; }
    public List<string> configuration { get; set; }
    public List<string> pixelsRun { get; set; }
    public object outputParametersDir { get; set; }
    public object validationReplicates { get; set; }
    public object parametersDistributions { get; set; }
    public object outputCalibrationDir { get; set; }
    public object outputValidationDir { get; set; }
    public object vegetationIndex { get; set; }
    public object parametersValidationFile { get; set; }
}

#endregion

