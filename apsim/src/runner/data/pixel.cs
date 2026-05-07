using System;
using System.Collections.Generic;

namespace runner.data
{
    //this class define a pixel: the corresponding class, the minimum, maximum values, and the inclusion of the parameter in the calibration subset
    public class pixel
    {
        public string ecoName { get; set; }
        public string id { get; set; }
        public int cluster { get; set; }
        public float latitude { get; set; }
        public float longitude { get; set; }      
        public Dictionary<DateTime, float> dateVInorm = new Dictionary<DateTime, float>();
        public Dictionary<DateTime, referenceData> dateGPP = new Dictionary<DateTime, referenceData>();
        public Dictionary<DateTime, referenceData> dateRECO = new Dictionary<DateTime, referenceData>();
        public Dictionary<DateTime, referenceData> dateNEE = new Dictionary<DateTime, referenceData>();
    }

    public class referenceData
    {
        public float value { get; set; }
        public string isCalibration { get; set; }
    }
}
