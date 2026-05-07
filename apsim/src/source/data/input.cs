using System;

// data namespace. It contains all the classes that are used to pass data to the swell model
namespace source.data
{

    // input data class. It is used as argument in each computing method
    public class input
    {
        public string vegetationIndex { get; set; }  //vegetation index (NDVI/EVI)
        public float airTemperatureMaximum { get; set; }  //air temperature maximum, °C
        public float airTemperatureMinimum { get; set; } //air temperature minimum, °C
        public float precipitation { get; set; }   //precipitation, mm
        public float referenceEvapotranspiration { get; set; } //reference evapotranspiration, mm/day
        public float dewPointTemperature { get; set; } //dew point temperature, °C
        public float PAR { get; set; } //photosynthetically active radiation, μmol/m2/s
        public float solarRadiation { get; set; } //solar radiation, W/m2
        public DateTime date { get; set; }     //date, DateTime object
        public float latitude { get; set; }     //latitude, decimal degrees

        public radData radData = new radData(); //radiation data, see below
        public float[] airTemperatureH = new float[24]; //hourly air temperature, °C
        public float[] solarRadiationH = new float[24]; //solar radiation hourly, W/m2
        public float[] vaporPressureDeficitH = new float[24]; //vapor pressure deficit hourly, kPa
        public float[] relativeHumidityH = new float[24]; //relative humidity hourly, %
        public float[] referenceET0H = new float[24]; //reference evapotranspiration hourly, mm/hour
        public float[] precipitationH = new float[24]; //precipitation hourly, mm/hour

        public simulationSettings simulationSettings = new simulationSettings(); //simulation settings, see below
    }

    public class simulationSettings
    {
        public string configuration { get; set; } //which configuration to use for the simulation (baseline, pheno, pheno_circ)
    }

    // separate object containing radiation data    
    public class radData
    {
       public float dayLength { get; set; } //hours

       public float hourSunrise { get; set; } //hour of sunrise, decimal hours

       public float hourSunriseTomorrow { get; set; } //hour of sunrise tomorrow, decimal hours

        public float hourSunset { get; set; } //hour of sunset, decimal hours

        public float dayLengthTomorrow { get; set; } 

        public float etr { get; set; } //reference evapotranspiration, mm/day

        public float[] etrHourly = new float[24]; //reference evapotranspiration hourly, mm/hour
        public float gsr { get; set; } //global solar radiation, MJ/m2/day
        public float[] gsrHourly = new float[24]; //global solar radiation hourly, MJ/m2/hour


    }
}
