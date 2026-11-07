using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseNudge {
    static class LayoutVerification {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        sealed class Baseline {
            public Control Control;
            public Rectangle Bounds;
            public float FontPixels;
        }
        static void Check(bool ok,string message,List<string> lines) {
            if(!ok)throw new Exception(message);lines.Add("PASS "+message);
        }
        static void Capture(Control parent,List<Baseline> result) {
            foreach(Control child in parent.Controls) {
                result.Add(new Baseline {Control=child,Bounds=child.Bounds,FontPixels=child.Font.Size});
                if(child is Panel)Capture(child,result);
            }
        }
        static int Scale(int value,int dpi) {return (int)Math.Round(value*dpi/96.0,MidpointRounding.AwayFromZero);}
        public static int Run(string directory) {
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            var lines=new List<string>();
            try {
                int sends=0;
                using(var signals=new InstanceSignals(".layout-test."+Guid.NewGuid().ToString("N")))
                using(var form=new MainForm(true,signals,Path.Combine(directory,"unused.settings.xml"),delegate(int delta) {sends++;return true;})) {
                    IntPtr handle=form.Handle;
                    lines.Add("Native window DPI: "+DpiLayout.WindowDpi(handle));
                    form.Show();Application.DoEvents();
                    form.ApplyLayoutDpi(96);
                    var baseline=new List<Baseline>();Capture(form,baseline);
                    Check(form.ClientSize==new Size(560,638),"100% client size uses the original logical layout",lines);
                    Check(baseline.Count>=35,"Checks cover the complete form and nested setting panels",lines);

                    // Include standard and fractional scales. This runs the same
                    // production layout path without changing Windows settings.
                    int[] dpis={96,106,120,128,144,168,192,216,240,288,336,384,480};
                    foreach(int dpi in dpis) {
                        form.ApplyLayoutDpi(dpi);
                        Check(form.ClientSize==new Size(Scale(560,dpi),Scale(638,dpi)),"Client size at DPI "+dpi,lines);
                        foreach(var item in baseline) {
                            Control c=item.Control;var b=item.Bounds;
                            string name=c.AccessibleName??c.Text;if(string.IsNullOrEmpty(name))name=c.GetType().Name;
                            if(c.Left!=Scale(b.Left,dpi) || c.Top!=Scale(b.Top,dpi) || c.Right!=Scale(b.Right,dpi))
                                throw new Exception("Geometry at DPI "+dpi+": "+name+" "+c.Bounds);
                            if(c.Font.Unit!=GraphicsUnit.Pixel || Math.Abs(c.Font.Size-item.FontPixels*dpi/96f)>0.02f)
                                throw new Exception("Font ratio at DPI "+dpi+": "+name);
                            // NumericUpDown owns its native edit height; it must
                            // still remain inside its card and clear the range row.
                            if(!(c is NumericUpDown) && Math.Abs(c.Bottom-Scale(b.Bottom,dpi))>1)
                                throw new Exception("Height at DPI "+dpi+": "+name);
                            if(c.Parent is Panel && (c.Right>c.Parent.ClientSize.Width || c.Bottom>c.Parent.ClientSize.Height))
                                throw new Exception("Card clipping at DPI "+dpi+": "+name);
                            if(c is Label && c.Text.Length>0) {
                                Size text=TextRenderer.MeasureText(c.Text,c.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
                                if(text.Width>c.ClientSize.Width || text.Height>c.ClientSize.Height)
                                    throw new Exception("Text clipping at DPI "+dpi+": "+name+" needs "+text+" in "+c.ClientSize);
                            }
                            var input=c as NumericUpDown;
                            if(input!=null) {
                                string longest=input.Maximum.ToString("F"+input.DecimalPlaces);
                                Size text=TextRenderer.MeasureText(longest,input.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
                                if(text.Width+Scale(24,dpi)>input.ClientSize.Width || text.Height>input.ClientSize.Height)
                                    throw new Exception("Numeric text clipping at DPI "+dpi+": "+name);
                            }
                        }
                        Check(true,"All control geometry, font ratios, labels and numeric ranges at DPI "+dpi,lines);
                        if(dpi==96 || dpi==120 || dpi==144 || dpi==192 || dpi==288) {
                            Control surface=form.Controls[0];
                            using(var bitmap=new Bitmap(surface.Width,surface.Height)) {
                                surface.DrawToBitmap(bitmap,new Rectangle(Point.Empty,surface.Size));
                                bitmap.Save(Path.Combine(directory,"layout-"+dpi+"dpi.png"),ImageFormat.Png);
                            }
                        }
                    }
                    for(int iteration=0;iteration<30;iteration++) {
                        form.ApplyLayoutDpi(120);form.ApplyLayoutDpi(192);form.ApplyLayoutDpi(106);form.ApplyLayoutDpi(96);
                    }
                    foreach(var item in baseline) {
                        if(item.Control.Bounds!=item.Bounds || Math.Abs(item.Control.Font.Size-item.FontPixels)>0.02f)
                            throw new Exception("Round-trip drift: "+item.Control.GetType().Name);
                    }
                    Check(true,"Repeated mixed-DPI transitions restore the original bounds and fonts without drift",lines);
                    var area=new Rectangle(0,0,800,600);form.ApplyLayoutDpi(192);form.FitToWorkingArea(area);
                    lines.Add("Small viewport: bounds="+form.Bounds+", client="+form.ClientSize+", scroll="+form.AutoScrollMinSize+", surface="+form.Controls[0].Bounds);
                    Check(form.Width<=area.Width && form.Height<=area.Height && form.AutoScrollMinSize==new Size(1120,1276),"A small work area gets a bounded viewport with the full content reachable by scrolling",lines);
                    form.AutoScrollPosition=new Point(200,400);form.ApplyLayoutDpi(96);
                    Check(form.ClientSize==new Size(560,638) && form.Controls[0].Location==Point.Empty,"DPI change restores the full layout after scrolling a smaller display",lines);
                    Check(MainForm.ViewportSize(new Size(560,638),new Size(16,39),new Size(1920,1080),17)==new Size(560,638),"A roomy display keeps the intended window size",lines);
                    IntPtr rectangle=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DpiLayout.SuggestedBounds)));
                    try {
                        Marshal.StructureToPtr(new DpiLayout.SuggestedBounds {Left=0,Top=0,Right=720,Bottom=840},rectangle,false);
                        SendMessage(form.Handle,0x02e0,new IntPtr((120<<16)|120),rectangle);
                        Check(form.Controls[0].Size==new Size(700,798),"The native WM_DPICHANGED handler rescales the complete surface",lines);
                        foreach(var item in baseline)
                            if(Math.Abs(item.Control.Font.Size-item.FontPixels*1.25f)>0.02f)throw new Exception("WM_DPICHANGED font ratio");
                        Check(true,"The native DPI-change handler scales fonts together with geometry",lines);
                    } finally {Marshal.FreeHGlobal(rectangle);}
                    form.ShowInTaskbar=false;form.ShowInTaskbar=true;
                    int actualDpi=DpiLayout.WindowDpi(form.Handle);
                    Check(form.Controls[0].Size==new Size(Scale(560,actualDpi),Scale(638,actualDpi)),"Handle recreation refreshes layout using the window's actual monitor DPI",lines);
                    Check(sends==0,"Layout verification never sends native mouse input",lines);
                }
                Check(!File.Exists(Path.Combine(directory,"unused.settings.xml")),"Layout verification leaves user preferences untouched",lines);
                lines.Add("All layout checks passed. Images simulate the production layout at each DPI; physical monitor transitions require a separate manual check.");
                File.WriteAllLines(Path.Combine(directory,"result.txt"),lines.ToArray());return 0;
            } catch(Exception ex) {
                lines.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(directory,"result.txt"),lines.ToArray());return 1;
            }
        }
    }
}
