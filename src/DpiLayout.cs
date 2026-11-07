using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseNudge {
    // A single 96-DPI coordinate system for the portable EXE. Recalculate from
    // the original geometry, never from the last monitor's rounded dimensions.
    // Pixel fonts also avoid GDI applying the system DPI a second time.
    sealed class DpiLayout:IDisposable {
        sealed class Item {
            public Control Control;
            public Rectangle Bounds;
            public Font OriginalFont,ScaledFont;
        }
        readonly Form form;
        readonly Size clientSize;
        readonly List<Item> items=new List<Item>();
        readonly List<Font> originalFonts=new List<Font>();
        bool disposed;
        public int Dpi {get;private set;}

        public static Font Font(float points,bool bold=false) {
            return new Font("맑은 고딕",points*96f/72f,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        }
        public DpiLayout(Form target) {
            form=target;clientSize=target.ClientSize;Capture(target);
        }
        void Capture(Control control) {
            items.Add(new Item {Control=control,Bounds=control.Bounds,OriginalFont=control.Font});
            if(!originalFonts.Exists(delegate(Font font) {return object.ReferenceEquals(font,control.Font);}))originalFonts.Add(control.Font);
            // Native compound controls (NumericUpDown) lay out their own children.
            if(control==form || control is Panel)
                foreach(Control child in control.Controls)Capture(child);
        }
        internal static int Pixels(int logical,int dpi) {return (int)Math.Round(logical*dpi/96.0,MidpointRounding.AwayFromZero);}
        public void Apply(int dpi) {
            if(dpi<=0 || Dpi==dpi)return;
            Dpi=dpi;
            foreach(var item in items)item.Control.SuspendLayout();
            try {
                foreach(var item in items) {
                    var old=item.ScaledFont;
                    item.ScaledFont=new Font(item.OriginalFont.FontFamily,item.OriginalFont.Size*dpi/96f,item.OriginalFont.Style,GraphicsUnit.Pixel);
                    item.Control.Font=item.ScaledFont;
                    if(old!=null)old.Dispose();
                    if(item.Control==form)continue;
                    var b=item.Bounds;
                    item.Control.Bounds=Rectangle.FromLTRB(Pixels(b.Left,dpi),Pixels(b.Top,dpi),Pixels(b.Right,dpi),Pixels(b.Bottom,dpi));
                }
                form.ClientSize=new Size(Pixels(clientSize.Width,dpi),Pixels(clientSize.Height,dpi));
            } finally {
                for(int i=items.Count-1;i>=0;i--)items[i].Control.ResumeLayout(true);
            }
            form.Invalidate(true);
        }
        public static int WindowDpi(IntPtr window) {
            try {uint dpi=GetDpiForWindow(window);if(dpi>0)return (int)dpi;}
            catch(EntryPointNotFoundException) {}
            using(var graphics=Graphics.FromHwnd(window))return (int)Math.Round(graphics.DpiX);
        }
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [StructLayout(LayoutKind.Sequential)] internal struct SuggestedBounds {public int Left,Top,Right,Bottom;}

        public void Dispose() {
            if(disposed)return;disposed=true;
            foreach(var item in items)if(item.ScaledFont!=null)item.ScaledFont.Dispose();
            foreach(var font in originalFonts)font.Dispose();
        }
    }
}
