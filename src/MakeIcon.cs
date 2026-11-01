using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class MakeIcon {
    static GraphicsPath Round(float x, float y, float w, float h, float r) {
        var p = new GraphicsPath();
        p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
        p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90); p.CloseFigure(); return p;
    }
    static Bitmap Draw(int size) {
        var b = new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g = Graphics.FromImage(b)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size/256f,size/256f);
            using(var shape = Round(4,4,248,248,55))
            using(var fill = new LinearGradientBrush(new Rectangle(0,0,256,256),Color.FromArgb(25,63,73),Color.FromArgb(12,34,44),60f)) g.FillPath(fill,shape);
            using(var shape = Round(87,42,82,139,41))
            using(var white = new SolidBrush(Color.FromArgb(247,252,252))) g.FillPath(white,shape);
            using(var line = new Pen(Color.FromArgb(26,68,78),6)) {
                line.StartCap = line.EndCap = LineCap.Round;
                g.DrawLine(line,128,49,128,98); g.DrawLine(line,92,106,164,106);
            }
            using(var wheel = Round(122,63,12,26,6))
            using(var teal = new SolidBrush(Color.FromArgb(35,183,160))) g.FillPath(teal,wheel);
            using(var arrow = new Pen(Color.FromArgb(99,230,199),10)) {
                arrow.StartCap = arrow.EndCap = LineCap.Round; arrow.LineJoin = LineJoin.Round;
                g.DrawLine(arrow,59,211,197,211);
                g.DrawLines(arrow,new [] {new PointF(76,194),new PointF(59,211),new PointF(76,228)});
                g.DrawLines(arrow,new [] {new PointF(180,194),new PointF(197,211),new PointF(180,228)});
            }
        } return b;
    }
    static int Main(string[] args) {
        string dir = args[0]; Directory.CreateDirectory(dir);
        using(var preview = Draw(512)) preview.Save(Path.Combine(dir,"MouseNudge.png"),ImageFormat.Png);
        int[] sizes = {16,24,32,48,64,128,256}; byte[][] frames = new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(var b=Draw(sizes[i])) using(var m=new MemoryStream()) {b.Save(m,ImageFormat.Png);frames[i]=m.ToArray();}
        using(var w=new BinaryWriter(File.Create(Path.Combine(dir,"MouseNudge.ico")))) {
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+sizes.Length*16;
            for(int i=0;i<sizes.Length;i++) {w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(frames[i].Length);w.Write(offset);offset+=frames[i].Length;}
            foreach(var frame in frames) w.Write(frame);
        }
        return 0;
    }
}
