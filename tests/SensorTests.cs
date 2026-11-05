using System;
using HardwareDeck;
using System.Collections.Generic;
using System.IO;
public static class SensorTests
{
    static int count;
    static void Check(bool good,string name){if(!good)throw new Exception(name);count++;}
    static void Reject(Action action,string name){try{action();}catch(ArgumentOutOfRangeException){count++;return;}throw new Exception(name);}
    public static int Main(){
        Check(AwccReader.QueryArgument(5,0x32)==0x3205,"RPM read encoding");
        Check(AwccReader.QueryArgument(4,1)==0x104,"Temperature read encoding");
        Reject(()=>AwccReader.QueryArgument(1,0),"Unimplemented Thermal_Information opcode 1");
        Reject(()=>AwccReader.QueryArgument(2,0),"Opcode 2 is not routed through Thermal_Information by this reader");
        Reject(()=>AwccReader.QueryArgument(5,256),"Reject overflowing sensor ID");
        Check(Values.Range(5000,0,150)==null,"Reject invalid firmware temperature");
        Check(Values.Range(0,0,30000)==0,"A stopped fan is zero");
        Check(CpuTelemetryReader.Frequency(3200,150)==4.8,"Windows frequency scaling");
        Check(CpuTelemetryReader.Frequency(3200,0)==null,"Missing frequency remains unavailable");
        var catalog=ModeCatalog.Parse(@"{""profiles"":[{""name"":""UsttQuiet"",""thermal_mode"":163,""readonly"":true},{""name"":""UsttBalanced"",""thermal_mode"":160,""readonly"":true},{""name"":""UsttPerformance"",""thermal_mode"":161,""readonly"":true}]}",new[]{160,161,163});
        Check(catalog.Count==3,"Only discovered standard presets");
        Check(ModeCatalog.Parse(@"{""profiles"":[{""name"":""UsttBalanced"",""thermal_mode"":160.2,""readonly"":true}]}",new[]{160}).Count==0,"Fractional preset IDs must not round into a valid mode");
        Check(ModeCatalog.Parse(@"{""profiles"":[{""name"":""UsttBalanced"",""thermal_mode"":""160"",""readonly"":true}]}",new[]{160}).Count==0,"String preset IDs must not enable hardware control");
        Check(ModeCatalog.ControlArgument(163,catalog)==0xa301,"Quiet command encoding");
        Reject(()=>ModeCatalog.ControlArgument(0,catalog),"Reject manual mode");
        Reject(()=>ModeCatalog.ControlArgument(171,catalog),"Reject overdrive mode");
        var swapped=ModeCatalog.Parse(@"{""profiles"":[{""name"":""UsttQuiet"",""thermal_mode"":161,""readonly"":true}]}",new[]{160,161,163});
        Check(swapped.Count==0,"Reject mismatched preset labels");
        var unavailable=ModeCatalog.Parse(@"{""profiles"":[{""name"":""UsttQuiet"",""thermal_mode"":163,""readonly"":true}]}",new[]{160});
        Check(unavailable.Count==0,"Reject firmware-unavailable preset");
        Check(CpuTelemetryReader.RaplPower(495)==49.5,"RAPL tenths of a watt");
        Check(CpuTelemetryReader.RaplPower(6509)==null,"Reject observed invalid startup spike");
        Check(AwccReader.FanSensorMethod=="GetFanSensors","Association uses the upstream GetFanSensors method");
        Check(AwccReader.FanSensorArgument(50)==0x3202,"Fan association request encoding");
        Reject(()=>AwccReader.FanSensorArgument(0),"Reject absent fan ID");
        Reject(()=>AwccReader.FanSensorArgument(256),"Reject oversized fan ID");
        Check(AwccReader.MatchFanTemperature(6,new[]{1,6})=="awcc.temp.6","Match known short association ID");
        Check(AwccReader.MatchFanTemperature(0x106,new[]{1,6})=="awcc.temp.6","Match tagged association ID");
        Check(AwccReader.MatchFanTemperature(255,new[]{1,6})==null,"Unknown firmware code is not a sensor");
        Check(AwccReader.MatchFanTemperature(0,new[]{1,6})==null,"Zero association is unknown");
        Check(AwccReader.MatchFanTemperature(5,new[]{1,6})==null,"Do not invent absent temperature sensors");
        Check(AwccReader.MatchFanTemperature(null,new[]{1,6})==null,"Unavailable query remains unknown");
        var first=new Reading{Id="awcc.temp.1",Kind="temperature",Value=30};
        var selected=new Reading{Id="awcc.temp.5",Kind="temperature",Value=45};
        var readings=new List<Reading>{first,selected};
        var config=SensorConfiguration.Parse(@"{""cpuTemperatureSensor"":""awcc.temp.5"",""labels"":{""awcc.temp.5"":""Chosen CPU""}}");
        Check(config.SelectCpuTemperature(readings)==selected,"Explicit CPU source overrides enumeration order");
        readings.Reverse();
        Check(config.SelectCpuTemperature(readings)==selected,"Explicit CPU source survives reordering");
        Check(config.Labels["awcc.temp.5"]=="Chosen CPU","Structured aliases are applied");
        readings.Remove(selected);
        Check(config.SelectCpuTemperature(readings)==null,"Missing selected sensor never silently falls back");
        readings.Add(new Reading{Id="awcc.temp.5",Kind="fan",Value=900});
        Check(config.SelectCpuTemperature(readings)==null,"A fan cannot be selected as CPU temperature");
        readings.Add(new Reading{Id="awcc.temp.5",Kind="temperature",Value=null});
        Check(!config.SelectCpuTemperature(readings).Value.HasValue,"Missing chosen reading is not substituted");
        var legacy=SensorConfiguration.Parse(@"{""awcc.temp.1"":""Legacy alias""}");
        Check(legacy.Labels["awcc.temp.1"]=="Legacy alias" && legacy.SelectCpuTemperature(readings)==first,"Legacy flat label maps remain compatible");
        var file=Path.GetTempFileName();
        try{
            File.WriteAllText(file,@"{""cpuTemperatureSensor"":""awcc.fan.50""}");
            var invalid=SensorConfiguration.Load(file);
            Check(invalid.Error!=null && invalid.SelectCpuTemperature(readings)==null,"Invalid configuration reports error rather than choosing a fallback");
        }finally{File.Delete(file);}
        Console.WriteLine(count+" sensor, configuration and mode contract checks passed.");return 0;
    }
}
