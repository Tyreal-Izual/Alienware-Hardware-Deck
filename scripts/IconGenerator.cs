using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

// Small renderer for the rect and M/L/H/V paths used by the source SVG; no external tooling.
class IconGenerator
{
    static float Number(string text) { return float.Parse(text,CultureInfo.InvariantCulture); }
    static float Attr(XmlElement element,string name) { return Number(element.GetAttribute(name)); }
    static GraphicsPath Shape(XmlElement element)
    {
        var path=new GraphicsPath();
        if(element.LocalName=="rect"){
            float x=Attr(element,"x"),y=Attr(element,"y"),w=Attr(element,"width"),h=Attr(element,"height"),d=Attr(element,"rx")*2;
            path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);
            path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();
        }else if(element.LocalName=="path"){
            float x=0,y=0;
            foreach(Match command in Regex.Matches(element.GetAttribute("d"),"([MLHV])([^MLHV]*)")){
                var numbers=Regex.Matches(command.Groups[2].Value,@"-?\d+(?:\.\d+)?").Cast<Match>().Select(m=>Number(m.Value)).ToArray();
                string kind=command.Groups[1].Value;
                for(int i=0;i<numbers.Length;){
                    float nextX=x,nextY=y;
                    if(kind=="H")nextX=numbers[i++];
                    else if(kind=="V")nextY=numbers[i++];
                    else { nextX=numbers[i++];nextY=numbers[i++]; }
                    if(kind=="M"){path.StartFigure();kind="L";}else path.AddLine(x,y,nextX,nextY);
                    x=nextX;y=nextY;
                }
            }
        }else throw new NotSupportedException("Unsupported SVG element: "+element.LocalName);
        return path;
    }
    static Bitmap Render(XmlDocument svg,int size)
    {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(bitmap)){
            graphics.Clear(Color.Transparent);graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.ScaleTransform(size/256f,size/256f);
            foreach(XmlNode node in svg.DocumentElement.ChildNodes){
                var element=node as XmlElement;if(element==null)continue;
                using(var shape=Shape(element)){
                    string fill=element.GetAttribute("fill");
                    if(fill!="none")using(var brush=new SolidBrush(ColorTranslator.FromHtml(fill)))graphics.FillPath(brush,shape);
                    using(var pen=new Pen(ColorTranslator.FromHtml(element.GetAttribute("stroke")),Attr(element,"stroke-width"))){
                        if(element.GetAttribute("stroke-linecap")=="round")pen.StartCap=pen.EndCap=LineCap.Round;
                        if(element.GetAttribute("stroke-linejoin")=="round")pen.LineJoin=LineJoin.Round;
                        graphics.DrawPath(pen,shape);
                    }
                }
            }
        }
        return bitmap;
    }
    static void Main(string[] args)
    {
        var svg=new XmlDocument();svg.XmlResolver=null;svg.Load(args[0]);
        int[] sizes={16,20,24,32,40,48,64,128,256};
        var frames=new List<byte[]>();
        foreach(int size in sizes)using(var bitmap=Render(svg,size))using(var stream=new MemoryStream()){
            bitmap.Save(stream,ImageFormat.Png);frames.Add(stream.ToArray());
        }
        using(var writer=new BinaryWriter(File.Create(args[1]))){
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;
            }
            foreach(var frame in frames)writer.Write(frame);
        }
        if(args.Length>2)using(var preview=Render(svg,512))preview.Save(args[2],ImageFormat.Png);
    }
}
