using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("Mouse Nudge")]
[assembly: AssemblyDescription("거리와 간격을 조절하는 마우스 좌우 이동 도구")]
[assembly: AssemblyProduct("Mouse Nudge")]
[assembly: AssemblyVersion("1.2.1.0")]

namespace MouseNudge {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct INPUT {public uint type; public INPUTUNION data;}
        [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {public int dx,dy;public uint mouseData,dwFlags,time;public UIntPtr dwExtraInfo;}
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {public ushort wVk,wScan;public uint dwFlags,time;public UIntPtr dwExtraInfo;}
        [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT {public uint uMsg;public ushort wParamL,wParamH;}
        [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll",SetLastError=true)] public static extern bool UnregisterHotKey(IntPtr window,int id);
        public static int LastError;
        public static bool Move(int x) {
            INPUT i = new INPUT(); i.data.mouse.dx=x;i.data.mouse.dwFlags=1;
            bool ok=SendInput(1,new [] {i},Marshal.SizeOf(typeof(INPUT)))==1;
            LastError=ok?0:Marshal.GetLastWin32Error();return ok;
        }
    }

    public sealed class NudgeSettings {
        public readonly int Distance,ReturnMilliseconds,WaitMilliseconds,StopAfterMinutes;
        public NudgeSettings(int distance,int returnMilliseconds,int waitMilliseconds,int stopAfterMinutes=0) {
            if(distance<1 || distance>1000)throw new ArgumentOutOfRangeException("distance");
            if(returnMilliseconds<50 || returnMilliseconds>60000)throw new ArgumentOutOfRangeException("returnMilliseconds");
            if(waitMilliseconds<100 || waitMilliseconds>3600000)throw new ArgumentOutOfRangeException("waitMilliseconds");
            if(stopAfterMinutes<0 || stopAfterMinutes>1440)throw new ArgumentOutOfRangeException("stopAfterMinutes");
            Distance=distance;ReturnMilliseconds=returnMilliseconds;WaitMilliseconds=waitMilliseconds;StopAfterMinutes=stopAfterMinutes;
        }
        public static NudgeSettings Defaults {get {return new NudgeSettings(3,300,10000);}}
        public bool SameAs(NudgeSettings other) {return other!=null && Distance==other.Distance && ReturnMilliseconds==other.ReturnMilliseconds && WaitMilliseconds==other.WaitMilliseconds && StopAfterMinutes==other.StopAfterMinutes;}
    }

    public static class SettingsStore {
        public static NudgeSettings Load(string path,out string notice) {
            notice=null;
            if(!File.Exists(path))return NudgeSettings.Defaults;
            try {
                var options=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
                var document=new XmlDocument {XmlResolver=null};
                using(var reader=XmlReader.Create(path,options))document.Load(reader);
                var root=document.DocumentElement;
                if(root==null || root.Name!="MouseNudgeSettings" || root.GetAttribute("version")!="1")throw new FormatException();
                return new NudgeSettings(int.Parse(root.GetAttribute("distance"),CultureInfo.InvariantCulture),int.Parse(root.GetAttribute("returnMilliseconds"),CultureInfo.InvariantCulture),int.Parse(root.GetAttribute("waitMilliseconds"),CultureInfo.InvariantCulture),root.HasAttribute("stopAfterMinutes")?int.Parse(root.GetAttribute("stopAfterMinutes"),CultureInfo.InvariantCulture):0);
            } catch(Exception e) {
                if(!(e is IOException || e is UnauthorizedAccessException || e is XmlException || e is ArgumentException || e is FormatException || e is OverflowException))throw;
                notice="저장된 설정을 읽지 못해 기본값을 사용합니다.";return NudgeSettings.Defaults;
            }
        }
        public static void Save(string path,NudgeSettings value) {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var writer=XmlWriter.Create(temporary,new XmlWriterSettings {Indent=true})) {
                    writer.WriteStartElement("MouseNudgeSettings");writer.WriteAttributeString("version","1");
                    writer.WriteAttributeString("distance",value.Distance.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("returnMilliseconds",value.ReturnMilliseconds.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("waitMilliseconds",value.WaitMilliseconds.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("stopAfterMinutes",value.StopAfterMinutes.ToString(CultureInfo.InvariantCulture));writer.WriteEndElement();
                }
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            } finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
    }

    // Monotonic deadlines keep movement timing independent of UI rendering.
    public sealed class Movement {
        readonly Func<int,bool> send;
        int pendingDistance;
        public NudgeSettings Settings {get;private set;}
        public bool Running {get;private set;}
        public bool PendingReturn {get;private set;}
        public long Due {get;private set;}
        public long Cycles {get;private set;}
        public bool Failed {get;private set;}
        public Movement(Func<int,bool> sender):this(sender,NudgeSettings.Defaults) {}
        public Movement(Func<int,bool> sender,NudgeSettings settings) {send=sender;Settings=settings;}
        bool Send(int delta) {if(send(delta))return true;Running=false;Failed=true;return false;}
        public void Start(long now) {
            if(Running)return;
            Failed=false;
            if(PendingReturn) {if(!Send(-pendingDistance))return;PendingReturn=false;}
            if(!Send(Settings.Distance))return;
            pendingDistance=Settings.Distance;PendingReturn=true;Running=true;Due=now+Settings.ReturnMilliseconds;
        }
        public void Tick(long now) {
            if(!Running || now<Due)return;
            if(PendingReturn) {
                if(!Send(-pendingDistance))return;
                PendingReturn=false;Cycles++;Due=now+Settings.WaitMilliseconds;
            } else {
                if(!Send(Settings.Distance))return;
                pendingDistance=Settings.Distance;PendingReturn=true;Due=now+Settings.ReturnMilliseconds;
            }
        }
        public void Pause() {
            Running=false;
            if(PendingReturn && Send(-pendingDistance))PendingReturn=false;
        }
        public void ApplySettings(NudgeSettings settings,long now) {
            if(settings==null)throw new ArgumentNullException("settings");
            bool resume=Running;Pause();Settings=settings;
            if(resume && !Failed)Start(now);
        }
    }

    sealed class SoftPanel:Panel {
        public SoftPanel() {DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);using(var p=new Pen(Color.FromArgb(226,233,235)))e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);
        }
    }

    public sealed class MainForm:Form {
        readonly Color ink=Color.FromArgb(22,48,58),muted=Color.FromArgb(96,115,122),green=Color.FromArgb(14,128,107);
        readonly Movement movement;
        readonly string settingsPath;
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        readonly InstanceSignals signals;
        readonly SessionLimit sessionLimit=new SessionLimit();
        readonly ToolTip tips=new ToolTip();
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly ToolStripMenuItem trayToggle;
        readonly Icon appIcon,activeIcon,pausedIcon;
        readonly Bitmap logo;
        Label status,description,cycle,settingsHint,activeSummary,hotkeyHint;
        Button toggle,applySettings,cancelSettings;
        NumericUpDown distanceInput,returnInput,waitInput,stopInput;
        CheckBox stopCheck;
        bool cleaned,sessionLocked,automaticStopped,hotkeyRegistered;
        const int HotkeyId=0x484e;
        readonly bool startPaused;
        readonly DpiLayout dpiLayout;
        readonly Panel surface;

        public MainForm(bool paused,InstanceSignals instanceSignals,string preferencesPath,Func<int,bool> sender) {
            SuspendLayout();
            startPaused=paused;signals=instanceSignals;
            settingsPath=preferencesPath;string loadNotice;
            movement=new Movement(sender,SettingsStore.Load(settingsPath,out loadNotice));
            sessionLimit.Configure(movement.Settings.StopAfterMinutes,0,false);
            Text="Mouse Nudge · 마우스 살짝";
            // All controls are built in logical pixels before applying one DPI
            // transform to both their geometry and their fonts.
            AutoScaleMode=AutoScaleMode.None;Font=DpiLayout.Font(10f);
            ClientSize=new Size(560,638);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;
            StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,248,249);
            AutoScroll=true;
            surface=new Panel {Location=Point.Empty,Size=ClientSize,BackColor=BackColor};Controls.Add(surface);
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("MouseNudge.ico"))
            using(var loaded=new Icon(s)) appIcon=(Icon)loaded.Clone();
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("MouseNudge.png"))
            using(var loaded=new Bitmap(s)) logo=new Bitmap(loaded);
            Icon=appIcon;activeIcon=StatusIcon(Color.FromArgb(45,202,150));pausedIcon=StatusIcon(Color.FromArgb(246,185,64));
            BuildUI();
            dpiLayout=new DpiLayout(this);
            ResumeLayout(false);
            if(loadNotice!=null) {settingsHint.Text=loadNotice;settingsHint.ForeColor=Color.FromArgb(175,80,51);}
            menu=new ContextMenuStrip();
            menu.Items.Add("창 열기",null,delegate {ShowMain();});
            trayToggle=new ToolStripMenuItem("일시정지",null,delegate {Toggle();});menu.Items.Add(trayToggle);
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("종료",null,delegate {Close();});
            tray=new NotifyIcon {Icon=pausedIcon,Text="Mouse Nudge · 일시정지",ContextMenuStrip=menu,Visible=true};
            tray.DoubleClick+=delegate {ShowMain();};
            timer.Interval=25;timer.Tick+=delegate {
                if(signals.Quit.WaitOne(0)) {Close();return;}
                if(signals.Pause.WaitOne(0))PauseSession(false);
                if(signals.Activate.WaitOne(0))ShowMain();
                long now=clock.ElapsedMilliseconds;
                if(movement.Running && sessionLimit.Expired(now))PauseSession(true);
                else {movement.Tick(now);if(!movement.Running)sessionLimit.Pause(now);}
                UpdateState();
            };timer.Start();
            Shown+=delegate {if(!startPaused)StartSession();UpdateState();};
            Microsoft.Win32.SystemEvents.SessionSwitch+=OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged+=OnPowerMode;
            FormClosing+=delegate {Cleanup();};
            if(IsHandleCreated)dpiLayout.Apply(DpiLayout.WindowDpi(Handle));
            UpdateState();
        }

        Icon StatusIcon(Color badge) {
            using(var canvas=new Bitmap(32,32)) {
                using(var g=Graphics.FromImage(canvas)) {
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(logo,0,0,32,32);
                    using(var rim=new SolidBrush(Color.White))g.FillEllipse(rim,20,20,12,12);
                    using(var fill=new SolidBrush(badge))g.FillEllipse(fill,22,22,8,8);
                }
                IntPtr h=canvas.GetHicon();try {using(var borrowed=Icon.FromHandle(h))return (Icon)borrowed.Clone();}finally {Native.DestroyIcon(h);}
            }
        }
        Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,Color color,bool bold) {
            var l=new Label {Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=DpiLayout.Font(size,bold),ForeColor=color,BackColor=Color.Transparent};
            parent.Controls.Add(l);return l;
        }
        Button ButtonAt(string text,int x,int y,int w,int h,Color background,Color foreground) {
            var b=new Button {Text=text,Location=new Point(x,y),Size=new Size(w,h),BackColor=background,ForeColor=foreground,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,Font=DpiLayout.Font(10f,true),UseVisualStyleBackColor=false};
            b.FlatAppearance.BorderSize=0;surface.Controls.Add(b);return b;
        }
        void BuildUI() {
            var picture=new PictureBox {Location=new Point(28,21),Size=new Size(48,48),Image=logo,SizeMode=PictureBoxSizeMode.Zoom};surface.Controls.Add(picture);
            LabelAt(surface,"Mouse Nudge",90,15,330,35,17,ink,true);
            LabelAt(surface,"마우스 살짝 · 필요한 만큼, 간편하게",92,53,420,23,9,muted,false);

            var statePanel=new SoftPanel {Location=new Point(28,92),Size=new Size(504,108)};surface.Controls.Add(statePanel);
            status=LabelAt(statePanel,"일시정지",16,10,470,28,14,ink,true);
            description=LabelAt(statePanel,"시작하면 설정한 간격으로 움직입니다.",17,42,469,22,9,muted,false);
            activeSummary=LabelAt(statePanel,"",17,66,469,21,8,muted,false);
            cycle=LabelAt(statePanel,"",17,87,469,19,8,muted,false);
            toggle=ButtonAt("시작",28,212,504,43,green,Color.White);toggle.Click+=delegate {Toggle();};

            LabelAt(surface,"동작 설정",28,272,235,24,11,ink,true);
            string[] presetNames={"기본","작은 움직임","느린 반복"};
            var presets=new [] {NudgeSettings.Defaults,new NudgeSettings(1,100,30000),new NudgeSettings(3,300,60000)};
            for(int i=0;i<presets.Length;i++) {
                var preset=presets[i];
                var button=ButtonAt(presetNames[i],28+i*172,301,160,28,Color.FromArgb(229,237,239),ink);
                button.Font=DpiLayout.Font(9f);
                button.Click+=delegate {SetMovementInputs(preset);UpdateDirty();};
                tips.SetToolTip(button,preset.Distance+" px · 되돌림 "+(preset.ReturnMilliseconds/1000m).ToString("0.##")+"초 · 반복 대기 "+(preset.WaitMilliseconds/1000m).ToString("0.#")+"초\n저장·적용을 누르면 반영됩니다. 자동 정지 예약은 유지합니다.");
            }
            string[] captions={"좌우 이동","되돌림 간격","반복 대기"};
            string[] ranges={"1 ~ 1,000 px","0.05 ~ 60 초","0.1 ~ 3,600 초"};
            var inputs=new NumericUpDown[3];
            for(int i=0;i<3;i++) {
                var p=new SoftPanel {Location=new Point(28+i*172,341),Size=new Size(160,92)};surface.Controls.Add(p);
                LabelAt(p,captions[i],12,7,138,22,10,ink,true);
                inputs[i]=new NumericUpDown {Location=new Point(12,32),Size=new Size(112,34),Minimum=i==0?1m:i==1?0.05m:0.1m,Maximum=i==0?1000m:i==1?60m:3600m,DecimalPlaces=i==0?0:i==1?2:1,Increment=i==0?1m:i==1?0.05m:0.1m,Font=DpiLayout.Font(15f),ForeColor=ink,AccessibleName=captions[i]};
                p.Controls.Add(inputs[i]);LabelAt(p,i==0?"px":"초",128,40,29,23,9,muted,false);
                LabelAt(p,ranges[i],12,69,141,19,8,muted,false);
            }
            distanceInput=inputs[0];returnInput=inputs[1];waitInput=inputs[2];
            stopCheck=new CheckBox {Text="자동 정지 예약",Location=new Point(28,449),Size=new Size(165,27),ForeColor=ink,Font=DpiLayout.Font(10f)};surface.Controls.Add(stopCheck);
            stopInput=new NumericUpDown {Location=new Point(202,447),Size=new Size(112,30),Minimum=1,Maximum=1440,Value=60,Font=DpiLayout.Font(12f),ForeColor=ink,AccessibleName="자동 정지 시간(분)"};surface.Controls.Add(stopInput);
            LabelAt(surface,"분 후 · 최대 24시간",328,453,204,23,9,muted,false);
            tips.SetToolTip(stopCheck,"실제 동작한 시간만 계산합니다. 일시정지 중에는 남은 시간이 줄지 않습니다.");
            SetInputs(movement.Settings);
            applySettings=ButtonAt("설정 저장·적용",28,493,320,34,Color.FromArgb(229,237,239),ink);applySettings.Click+=delegate {SaveSettings();};
            cancelSettings=ButtonAt("변경 취소",360,493,172,34,Color.FromArgb(229,237,239),ink);cancelSettings.Click+=delegate {SetInputs(movement.Settings);UpdateDirty();};
            settingsHint=LabelAt(surface,"설정이 적용되어 있습니다.",28,535,504,23,8,muted,false);
            foreach(var input in new [] {distanceInput,returnInput,waitInput,stopInput}) {
                input.ValueChanged+=delegate {UpdateDirty();};
                // NumericUpDown commits typed text when focus leaves. Enable Apply while
                // the user is still typing, without clamping partial values such as "0.".
                input.TextChanged+=delegate {MarkEdited();};
            }
            stopCheck.CheckedChanged+=delegate {stopInput.Enabled=stopCheck.Checked;UpdateDirty();};
            applySettings.Enabled=false;cancelSettings.Enabled=false;
            var hide=ButtonAt("알림 영역으로",28,567,331,32,Color.FromArgb(229,237,239),ink);hide.Click+=delegate {HideToTray();};
            var exit=ButtonAt("종료",371,567,161,32,Color.FromArgb(229,237,239),ink);exit.Click+=delegate {Close();};
            hotkeyHint=LabelAt(surface,"Ctrl + Shift + J · 시작 / 일시정지",28,611,504,20,8,muted,false);
            UpdateHotkeyHint();
        }
        void SetMovementInputs(NudgeSettings settings) {distanceInput.Value=settings.Distance;returnInput.Value=settings.ReturnMilliseconds/1000m;waitInput.Value=settings.WaitMilliseconds/1000m;}
        void SetInputs(NudgeSettings settings) {
            SetMovementInputs(settings);stopCheck.Checked=settings.StopAfterMinutes>0;stopInput.Value=settings.StopAfterMinutes>0?settings.StopAfterMinutes:60;stopInput.Enabled=stopCheck.Checked;
        }
        NudgeSettings ReadInputs() {return new NudgeSettings((int)distanceInput.Value,(int)(returnInput.Value*1000m),(int)(waitInput.Value*1000m),stopCheck.Checked?(int)stopInput.Value:0);}
        void MarkEdited() {
            if(applySettings==null || settingsHint==null)return;
            applySettings.Enabled=true;cancelSettings.Enabled=true;
            settingsHint.Text="변경사항이 있습니다. 저장·적용을 눌러 반영하세요.";settingsHint.ForeColor=ink;
        }
        void UpdateDirty() {
            if(applySettings==null || settingsHint==null)return;
            bool changed=!ReadInputs().SameAs(movement.Settings);
            applySettings.Enabled=changed;cancelSettings.Enabled=changed;
            settingsHint.Text=changed?"변경사항이 있습니다. 저장·적용을 눌러 반영하세요.":"설정이 적용되어 있습니다.";settingsHint.ForeColor=changed?ink:muted;
        }
        void SaveSettings() {
            // Validate pending text edits before reading NumericUpDown.Value.
            ValidateChildren();var settings=ReadInputs();
            try {SettingsStore.Save(settingsPath,settings);}
            catch(IOException) {SettingsSaveFailed();return;}
            catch(UnauthorizedAccessException) {SettingsSaveFailed();return;}
            long now=clock.ElapsedMilliseconds;bool limitChanged=settings.StopAfterMinutes!=movement.Settings.StopAfterMinutes;
            movement.ApplySettings(settings,now);
            if(limitChanged) {sessionLimit.Configure(settings.StopAfterMinutes,now,movement.Running);automaticStopped=false;}
            else if(!movement.Running)sessionLimit.Pause(now);
            UpdateDirty();
            settingsHint.Text="저장했습니다. 다음 실행에도 이 설정을 사용합니다.";settingsHint.ForeColor=green;UpdateState();
        }
        void SettingsSaveFailed() {
            settingsHint.Text="저장할 수 없습니다. 쓰기 가능한 폴더에서 실행하세요.";settingsHint.ForeColor=Color.FromArgb(175,80,51);
        }
        void Toggle() {
            if(movement.Running)PauseSession(false);else if(!sessionLocked)StartSession();
            UpdateState();
        }
        void StartSession() {
            long now=clock.ElapsedMilliseconds;automaticStopped=false;movement.Start(now);
            if(movement.Running)sessionLimit.Resume(now);else sessionLimit.Pause(now);
        }
        void PauseSession(bool automatic) {
            movement.Pause();sessionLimit.Pause(clock.ElapsedMilliseconds);automaticStopped=automatic;
        }
        void UpdateState() {
            bool running=movement.Running;
            status.Text=movement.Failed?"입력 전송을 멈췄습니다":running?"●  동작 중":automaticStopped?"●  자동 정지 완료":"●  일시정지";
            status.ForeColor=movement.Failed?Color.FromArgb(175,80,51):running?green:ink;
            if(movement.Failed)description.Text="입력이 제한되었습니다. 일반 화면에서 다시 시작하세요.";
            else if(sessionLocked)description.Text="화면 잠금이 해제되면 시작할 수 있습니다.";
            else if(automaticStopped)description.Text="예약한 시간이 끝났습니다. 시작하면 새로 동작합니다.";
            else if(!running)description.Text="시작하면 마우스가 좌우로 조금씩 움직입니다.";
            else if(movement.PendingReturn)description.Text="오른쪽으로 이동 · 잠시 후 왼쪽으로 돌아옵니다.";
            else description.Text="다음 움직임까지 "+Math.Max(0,(movement.Due-clock.ElapsedMilliseconds)/1000.0).ToString("0.0")+"초";
            var current=movement.Settings;
            activeSummary.Text="현재 적용 · "+current.Distance+" px   /   되돌림 "+(current.ReturnMilliseconds/1000m).ToString("0.##")+"초   /   반복 대기 "+(current.WaitMilliseconds/1000m).ToString("0.#")+"초";
            string limit="자동 정지 없음";
            if(sessionLimit.Enabled) {
                var remaining=TimeSpan.FromSeconds(Math.Ceiling(sessionLimit.Remaining(clock.ElapsedMilliseconds)/1000.0));
                limit="자동 정지까지 "+string.Format("{0:00}:{1:00}:{2:00}",(int)remaining.TotalHours,remaining.Minutes,remaining.Seconds);
                if(!running && !automaticStopped)limit+=" (일시정지)";
            }
            cycle.Text="왕복 "+movement.Cycles.ToString("N0")+"회   ·   "+limit;
            toggle.Text=running?"일시정지":"시작";toggle.BackColor=running?ink:green;toggle.Enabled=!sessionLocked;
            trayToggle.Text=running?"일시정지":"시작";trayToggle.Enabled=!sessionLocked;
            string trayText=running?"Mouse Nudge · 동작 중":"Mouse Nudge · 일시정지";
            if(tray.Text!=trayText) {tray.Text=trayText;tray.Icon=running?activeIcon:pausedIcon;}
        }
        void HideToTray() {ShowInTaskbar=false;Hide();}
        void ShowMain() {ShowInTaskbar=true;Show();WindowState=FormWindowState.Normal;Activate();}
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            if(dpiLayout!=null) {ApplyLayoutDpi(DpiLayout.WindowDpi(Handle));FitToWorkingArea(Screen.FromHandle(Handle).WorkingArea);}
            hotkeyRegistered=Native.RegisterHotKey(Handle,HotkeyId,0x4000|0x0002|0x0004,0x4a);UpdateHotkeyHint();
        }
        protected override void OnHandleDestroyed(EventArgs e) {
            if(hotkeyRegistered) {Native.UnregisterHotKey(Handle,HotkeyId);hotkeyRegistered=false;}
            base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message message) {
            if(message.Msg==0x0312 && message.WParam.ToInt32()==HotkeyId) {Toggle();return;}
            if(message.Msg==0x02e0 && dpiLayout!=null) {
                var suggested=(DpiLayout.SuggestedBounds)Marshal.PtrToStructure(message.LParam,typeof(DpiLayout.SuggestedBounds));
                // Windows supplies the new monitor position. Geometry and fonts
                // use the original logical layout to avoid rounding drift.
                ApplyLayoutDpi((int)(message.WParam.ToInt64()&0xffff));
                Location=new Point(suggested.Left,suggested.Top);
                FitToWorkingArea(Screen.FromRectangle(Rectangle.FromLTRB(suggested.Left,suggested.Top,suggested.Right,suggested.Bottom)).WorkingArea);
                message.Result=IntPtr.Zero;return;
            }
            base.WndProc(ref message);
        }
        internal void ApplyLayoutDpi(int dpi) {
            AutoScrollPosition=Point.Empty;AutoScrollMinSize=Size.Empty;dpiLayout.Apply(dpi);
            // Restore the full client after a smaller monitor's scroll viewport.
            ClientSize=surface.Size;
        }
        internal void FitToWorkingArea(Rectangle area) {
            // Derive the non-client frame from the window style. Size minus
            // ClientSize is unreliable after Windows clamps an oversized window.
            Size frame=SizeFromClientSize(Size.Empty);
            Size viewport=ViewportSize(surface.Size,frame,area.Size,DpiLayout.Pixels(17,dpiLayout.Dpi));
            AutoScrollMinSize=surface.Size;
            Size=new Size(Math.Min(area.Width,viewport.Width+frame.Width),Math.Min(area.Height,viewport.Height+frame.Height));
            int x=Math.Max(area.Left,Math.Min(Left,area.Right-Width));
            int y=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));
            Location=new Point(x,y);
        }
        protected override void OnResizeEnd(EventArgs e) {
            base.OnResizeEnd(e);
            if(dpiLayout!=null) {ApplyLayoutDpi(DpiLayout.WindowDpi(Handle));FitToWorkingArea(Screen.FromHandle(Handle).WorkingArea);}
        }
        internal static Size ViewportSize(Size content,Size frame,Size available,int scrollbarWidth) {
            int width=Math.Max(1,available.Width-frame.Width),height=Math.Max(1,available.Height-frame.Height);
            return new Size(Math.Min(content.Width+(content.Height>height?scrollbarWidth:0),width),Math.Min(content.Height,height));
        }
        void UpdateHotkeyHint() {
            if(hotkeyHint==null)return;
            hotkeyHint.Text=hotkeyRegistered?"Ctrl + Shift + J · 어디서나 시작 / 일시정지":"단축키 사용 중 · 창과 알림 영역 버튼으로 조작하세요.";
        }
        void OnSessionSwitch(object sender,Microsoft.Win32.SessionSwitchEventArgs e) {
            if(IsDisposed || !IsHandleCreated)return;
            if(e.Reason==Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason==Microsoft.Win32.SessionSwitchReason.SessionLogoff || e.Reason==Microsoft.Win32.SessionSwitchReason.RemoteDisconnect || e.Reason==Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
                BeginInvoke((Action)delegate {sessionLocked=true;PauseSession(false);UpdateState();});
            else if(e.Reason==Microsoft.Win32.SessionSwitchReason.SessionUnlock || e.Reason==Microsoft.Win32.SessionSwitchReason.SessionLogon || e.Reason==Microsoft.Win32.SessionSwitchReason.RemoteConnect || e.Reason==Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
                BeginInvoke((Action)delegate {sessionLocked=false;UpdateState();});
        }
        void OnPowerMode(object sender,Microsoft.Win32.PowerModeChangedEventArgs e) {
            if(e.Mode==Microsoft.Win32.PowerModes.Suspend && IsHandleCreated && !IsDisposed)
                Invoke((Action)delegate {PauseSession(false);UpdateState();});
        }
        void Cleanup() {
            if(cleaned)return;cleaned=true;timer.Stop();movement.Pause();
            Microsoft.Win32.SystemEvents.SessionSwitch-=OnSessionSwitch;Microsoft.Win32.SystemEvents.PowerModeChanged-=OnPowerMode;
            tray.Visible=false;tray.Dispose();menu.Dispose();timer.Dispose();tips.Dispose();
            activeIcon.Dispose();pausedIcon.Dispose();appIcon.Dispose();logo.Dispose();
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !cleaned && tray!=null)Cleanup();
            base.Dispose(disposing);
            if(disposing && dpiLayout!=null)dpiLayout.Dispose();
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            if(args.Length>0 && args[0]=="--self-test")return Verification.Run(args.Length>1?args[1]:"verification.txt");
            if(args.Length>0 && args[0]=="--layout-test") {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                return LayoutVerification.Run(args.Length>1?args[1]:"layout-verification");
            }
            bool paused=Array.IndexOf(args,"--paused")>=0;
            string suffix="";
            string preferencesPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MouseNudge.settings.xml");
            Func<int,bool> sender=Native.Move;
#if UI_PREVIEW
            paused=true;sender=delegate(int delta) {return true;};
            suffix=".preview";
#endif
            if(Array.IndexOf(args,"--pause")>=0)return InstanceSignals.Send("Pause",suffix);
            if(Array.IndexOf(args,"--quit")>=0)return InstanceSignals.Send("Quit",suffix);
            if(Array.IndexOf(args,"--show")>=0)return InstanceSignals.Send("Activate",suffix);
            bool created;using(var signals=new InstanceSignals(suffix))
            using(var mutex=new Mutex(true,"Local\\MouseNudge.SingleInstance.1"+suffix,out created)) {
                if(!created) {signals.Activate.Set();return 0;}
                try {
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm(paused,signals,preferencesPath,sender));return 0;
                } catch(Exception ex) {MessageBox.Show("프로그램을 실행할 수 없습니다.\n"+ex.Message,"Mouse Nudge",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
                finally {mutex.ReleaseMutex();}
            }
        }
    }
}
