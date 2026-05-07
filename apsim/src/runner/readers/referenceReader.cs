using runner.data;
using source.data;
using source.functions;

namespace runner
{
    //this class reads the NDVI reference data 
    internal class referenceReader
    {
        //this method reads the NDVI data from the .csv file
        internal Dictionary<string, pixel> read(string file)
        {
            //create the dictionary
            Dictionary<string, pixel> idPixel = new Dictionary<string, pixel>();

            //open the stream
            StreamReader sr = new StreamReader(file);
            //read the first line
            sr.ReadLine();

            //loop over lines
            while (!sr.EndOfStream)
            {
                //read the line and split
                string[] line = sr.ReadLine().Split(',', '"');
                //assign the pixel
                string pixel = line[3];

                //if the dictionary does not contain the pixel, add it
                if (!idPixel.ContainsKey(pixel))
                {
                    idPixel.Add(pixel, new pixel());
                    idPixel[pixel].id = line[3];
                    idPixel[pixel].ecoName = line[4];
                    idPixel[pixel].cluster = int.Parse(line[13]);
                    idPixel[pixel].latitude = float.Parse(line[33]);
                }
                int year = int.Parse(line[8]);
                //
                DateTime date = new DateTime(year, 1, 1).AddDays(int.Parse(line[9]));              
            }

            return idPixel;
        }

        public Dictionary<string, pixel> readReferenceData(string file)
        {
            Dictionary<string, pixel> idPixel = new Dictionary<string, pixel>();
            //open a stream
            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            while (!sr.EndOfStream)
            {
                string[] line = sr.ReadLine().Split(',', '"');
                string pixel = line[0];

                if (line.Length == 7)
                {
                    if (!idPixel.ContainsKey(pixel))
                    {

                        idPixel.Add(pixel, new pixel());
                        idPixel[pixel].id = line[0];
                        idPixel[pixel].longitude = float.Parse(line[4]);
                        idPixel[pixel].latitude = float.Parse(line[5]);
                        idPixel[pixel].ecoName=line[1];
                    }
                    int year = int.Parse(line[2]);

                    if (line[6] != "NA")
                    {
                        DateTime date = new DateTime(year, 1, 1).AddDays(int.Parse(line[3]));
                        if (!idPixel[pixel].dateVInorm.ContainsKey(date))
                        {                           
                            idPixel[pixel].dateVInorm.Add(date, float.Parse(line[6]));
                        }
                    }
                }
            }
            //close the file
            sr.Close();

            return idPixel;
        }

        public pixel readReferenceDataFluxes(string referenceFluxesDir, pixel pixel)
        {
            pixel _pixel = pixel;
            //open a stream
            StreamReader sr = new StreamReader(referenceFluxesDir + "//fluxes_" + pixel.id + ".csv");
            sr.ReadLine();
            Random rng = new Random(42); // seed fisso = riproducibile
            while (!sr.EndOfStream)
            {
                string[] line = sr.ReadLine().Split(',', '"');
                DateTime dateTime = new DateTime(int.Parse(line[1])-1,12, 31).AddDays(int.Parse(line[2])).AddHours(int.Parse(line[3]));

                
                if (line[12] != "NA" && line[12] != "")
                {
                    referenceData referenceData = new referenceData();
                    referenceData.value = float.Parse(line[12]);
                    referenceData.isCalibration = line[14];
                    _pixel.dateRECO.Add(dateTime, referenceData);
                }

                float lat = pixel.latitude;
                float lon = pixel.longitude;
                input input = new input();
                input.latitude = lat;
                input.date = dateTime;
                var radData = utils.astronomy(input, false);

                if (line[13] != "NA" && line[13] != "")
                {
                    referenceData referenceData = new referenceData();
                    referenceData.value = float.Parse(line[13]);
                    referenceData.isCalibration = line[14];
                    _pixel.dateGPP.Add(dateTime, referenceData);
                }

                if (_pixel.dateGPP.ContainsKey(dateTime) && _pixel.dateRECO.ContainsKey(dateTime) &&
                    line[11] != "NA" && line[11] != "")
                {
                    referenceData referenceData = new referenceData();
                    referenceData.value = _pixel.dateRECO[dateTime].value- _pixel.dateGPP[dateTime].value;
                    referenceData.isCalibration = line[14];
                    _pixel.dateNEE.Add(dateTime, referenceData);
                }



            }

            //close the file
            sr.Close();

            return _pixel;
        }

    }
}
