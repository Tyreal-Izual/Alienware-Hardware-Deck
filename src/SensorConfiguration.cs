using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HardwareDeck
{
    public sealed class SensorConfiguration
    {
        public string CpuTemperatureSensorId { get; private set; }
        public Dictionary<string,string> Labels { get; private set; }
        public string Error { get; private set; }
        public SensorConfiguration(){Labels=new Dictionary<string,string>(StringComparer.Ordinal);}
        public static SensorConfiguration Parse(string json)
        {
            var root=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
            if(root==null)throw new FormatException("sensor-labels.json 必须是 JSON 对象");
            var result=new SensorConfiguration();
            object value;
            if(root.TryGetValue("cpuTemperatureSensor",out value) && value!=null){
                var id=value as string;
                int number;
                if(id==null || !id.StartsWith("awcc.temp.",StringComparison.Ordinal) ||
                    !int.TryParse(id.Substring(10),NumberStyles.None,CultureInfo.InvariantCulture,out number) ||
                    number<1 || number>255 || id!="awcc.temp."+number.ToString(CultureInfo.InvariantCulture))
                    throw new FormatException("cpuTemperatureSensor 必须是 awcc.temp.<编号>");
                result.CpuTemperatureSensorId=id;
            }
            // Older flat label maps remain valid.
            foreach(var item in root)
                if(item.Key.StartsWith("awcc.",StringComparison.Ordinal) && item.Value is string)
                    result.Labels[item.Key]=(string)item.Value;
            if(root.TryGetValue("labels",out value)){
                var labels=value as Dictionary<string,object>;
                if(labels==null)throw new FormatException("labels 必须是对象");
                foreach(var item in labels){
                    if(!(item.Value is string))throw new FormatException("传感器名称必须是字符串");
                    result.Labels[item.Key]=(string)item.Value;
                }
            }
            return result;
        }
        public static SensorConfiguration Load(string path)
        {
            try{return File.Exists(path)?Parse(File.ReadAllText(path)):new SensorConfiguration();}
            catch(Exception ex){return new SensorConfiguration{Error=ex.Message};}
        }
        public Reading SelectCpuTemperature(IEnumerable<Reading> sensors)
        {
            if(Error!=null)return null;
            var temperatures=(sensors??Enumerable.Empty<Reading>()).Where(s=>s.Kind=="temperature");
            if(CpuTemperatureSensorId!=null)
                return temperatures.FirstOrDefault(s=>s.Id==CpuTemperatureSensorId);
            return temperatures.FirstOrDefault();
        }
        public string DescribeSelection(Reading selected)
        {
            if(Error!=null)return "传感器配置无效："+Error;
            if(CpuTemperatureSensorId!=null)
                return selected!=null?"配置的 CPU 温度源："+CpuTemperatureSensorId:
                    "指定温度源 "+CpuTemperatureSensorId+" 当前不可用；未自动换源";
            return selected==null?"没有可用的 AWCC 温度源":
                "自动选择首个温度："+selected.Id+"（尚未确认 CPU 身份）";
        }
    }
}
