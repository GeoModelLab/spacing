using System.Collections.Generic;
using runner.data;
using System.IO;

namespace runner
{
    public class paramReader
    {
        public Dictionary<string, parameter> read(string file)
        {
            Dictionary<string, parameter> nameParam = new Dictionary<string, parameter>();

            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            while(!sr.EndOfStream)
            {
                string[] line = sr.ReadLine().Split(',');

                
                nameParam.Add(line[0] + "_" + line[1], new parameter());
                parameter parameter = new parameter();
                parameter.value = float.Parse(line[4]);
                parameter.minimum = float.Parse(line[2]);
                parameter.maximum = float.Parse(line[3]);
                parameter.calibration = line[5];
                parameter.classParam = line[0];
                nameParam[line[0] + "_" + line[1]] = parameter;
                              
            }

            return nameParam;
        }
    }
}
