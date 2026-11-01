using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseNudge {
    static class Verification {
        static void Check(bool ok,string message,List<string> lines) {if(!ok)throw new Exception(message);lines.Add("PASS "+message);}
        public static int Run(string path) {
            var lines=new List<string>();
            try {
                Check(Marshal.SizeOf(typeof(Native.INPUT))==(IntPtr.Size==8?40:28),"Native INPUT size ("+(IntPtr.Size*8)+"-bit)",lines);
                Check(Marshal.OffsetOf(typeof(Native.INPUT),"data").ToInt32()==(IntPtr.Size==8?8:4),"Native union alignment",lines);
                var moves=new List<int>();var m=new Movement(delegate(int x) {moves.Add(x);return true;});
                m.Start(0);Check(m.Running && moves.Count==1 && moves[0]==3,"Start immediately sends +3",lines);
                m.Tick(299);Check(moves.Count==1,"No return before 300 ms",lines);
                m.Tick(300);Check(moves.Count==2 && moves[1]==-3 && m.Cycles==1,"Return -3 at 300 ms",lines);
                m.Tick(10299);Check(moves.Count==2,"Wait a full 10 seconds after return",lines);
                m.Tick(10300);Check(moves.Count==3 && moves[2]==3,"Next cycle at 10.3 seconds",lines);
                m.Pause();Check(!m.Running && !m.PendingReturn && moves[moves.Count-1]==-3,"Pause during outward move completes return",lines);
                int count=moves.Count;m.Tick(1000000);Check(count==moves.Count,"Paused state never sends input",lines);
                m.Start(1000000);m.Tick(1000300);m.Pause();count=moves.Count;m.Pause();Check(moves.Count==count,"Repeated pause sends no extra input",lines);
                m.Start(2000000);m.Tick(3000000);Check(m.Due==3010000,"Delayed timer never creates catch-up bursts",lines);
                var failure=new Movement(delegate(int x) {return false;});failure.Start(0);
                Check(!failure.Running && failure.Failed,"Failed input pauses instead of looping",lines);
                bool failReturn=true;var retryMoves=new List<int>();var retry=new Movement(delegate(int x) {retryMoves.Add(x);return x==3 || !failReturn;});
                retry.Start(0);retry.Tick(300);Check(retry.PendingReturn && !retry.Running,"Failed return remains pending",lines);
                failReturn=false;retry.Start(500);Check(retryMoves.Count==4 && retryMoves[2]==-3 && retryMoves[3]==3 && retry.Running,"Restart settles a pending return first",lines);

                var customMoves=new List<int>();var custom=new Movement(delegate(int x) {customMoves.Add(x);return true;},new NudgeSettings(17,120,2300));
                custom.Start(100);Check(customMoves[0]==17 && custom.Due==220,"Custom distance and return interval apply at start",lines);
                custom.Tick(219);Check(customMoves.Count==1,"Custom return deadline is respected",lines);
                custom.Tick(220);Check(customMoves[1]==-17 && custom.Due==2520,"Custom return distance and repeat wait are respected",lines);
                custom.Tick(2519);Check(customMoves.Count==2,"Custom wait is measured after the return",lines);
                custom.Tick(2520);Check(customMoves[2]==17,"Custom cycle restarts at the selected deadline",lines);
                custom.ApplySettings(new NudgeSettings(25,500,2000),2600);
                Check(customMoves[3]==-17 && customMoves[4]==25 && custom.Due==3100,"Changing a running distance settles the old movement before using the new distance",lines);
                custom.Pause();Check(customMoves[5]==-25,"Pause returns by the new distance",lines);
                int pausedCount=customMoves.Count;custom.ApplySettings(NudgeSettings.Defaults,3000);
                Check(!custom.Running && customMoves.Count==pausedCount,"Changing paused settings never starts movement",lines);
                custom.Start(4000);Check(customMoves[customMoves.Count-1]==3 && custom.Due==4300,"Restoring defaults restores the original movement",lines);

                bool blockReturn=true;var changedMoves=new List<int>();var changedFailure=new Movement(delegate(int x) {changedMoves.Add(x);return x>0 || !blockReturn;},new NudgeSettings(12,100,1000));
                changedFailure.Start(0);changedFailure.ApplySettings(new NudgeSettings(44,500,1500),50);
                Check(changedFailure.Failed && !changedFailure.Running && changedFailure.PendingReturn,"An unsuccessful return while applying settings pauses movement",lines);
                blockReturn=false;changedFailure.Start(100);
                Check(changedMoves[changedMoves.Count-2]==-12 && changedMoves[changedMoves.Count-1]==44,"Retry after settings change returns by the original pending distance",lines);

                int rejected=0;
                foreach(var values in new [] {new [] {0,300,10000},new [] {1001,300,10000},new [] {3,49,10000},new [] {3,60001,10000},new [] {3,300,99},new [] {3,300,3600001}}) {
                    try {new NudgeSettings(values[0],values[1],values[2]);}catch(ArgumentOutOfRangeException) {rejected++;}
                }
                Check(rejected==6,"Out-of-range distances and time intervals are rejected",lines);
                var minimum=new NudgeSettings(1,50,100);var maximum=new NudgeSettings(1000,60000,3600000);
                Check(minimum.Distance==1 && maximum.WaitMilliseconds==3600000,"All selectable range boundaries are accepted",lines);

                var limit=new SessionLimit();limit.Configure(1,1000,false);
                Check(limit.Remaining(30000)==60000,"Automatic-stop timer does not count while paused",lines);
                limit.Resume(1000);Check(limit.Remaining(11000)==50000,"Automatic-stop timer counts active time",lines);
                limit.Pause(11000);Check(limit.Remaining(90000)==50000,"Pausing freezes the remaining time",lines);
                limit.Resume(100000);Check(!limit.Expired(149999) && limit.Expired(150000),"Resume preserves the exact remaining-time deadline",lines);
                limit.Pause(150000);limit.Resume(200000);Check(limit.Remaining(200000)==60000,"Starting after automatic stop creates a fresh timed session",lines);
                limit.Configure(0,200000,true);Check(!limit.Enabled && !limit.Expired(long.MaxValue),"Disabling automatic stop restores unlimited operation",lines);
                limit.Configure(1440,0,true);Check(limit.Remaining(0)==86400000,"A 24-hour limit does not overflow",lines);
                bool badLimit=false;try {limit.Configure(1441,0,false);}catch(ArgumentOutOfRangeException) {badLimit=true;}
                Check(badLimit,"Automatic-stop range is validated",lines);
                string signalSuffix=".test."+Guid.NewGuid().ToString("N");
                Check(InstanceSignals.Send("Pause",signalSuffix)==2,"Control commands do not launch or create a missing instance",lines);
                using(var signals=new InstanceSignals(signalSuffix)) {
                    Check(InstanceSignals.Send("Pause",signalSuffix)==0 && signals.Pause.WaitOne(0) && !signals.Pause.WaitOne(0),"Pause command signals exactly once",lines);
                    Check(InstanceSignals.Send("Quit",signalSuffix)==0 && signals.Quit.WaitOne(0),"Quit command reaches the running instance",lines);
                    Check(InstanceSignals.Send("Activate",signalSuffix)==0 && signals.Activate.WaitOne(0),"Show command requests the existing window",lines);
                }

                string directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"settings-test-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try {
                    string settingsFile=Path.Combine(directory,"settings.xml"),notice;
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(NudgeSettings.Defaults) && notice==null,"First launch uses the original defaults",lines);
                    SettingsStore.Save(settingsFile,new NudgeSettings(25,500,2000));
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(new NudgeSettings(25,500,2000)) && notice==null,"Settings persist and reload with the same values",lines);
                    SettingsStore.Save(settingsFile,new NudgeSettings(5,200,30000,90));
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(new NudgeSettings(5,200,30000,90)),"Automatic-stop duration persists with the movement settings",lines);
                    File.WriteAllText(settingsFile,"<MouseNudgeSettings version=\"1\" distance=\"7\" returnMilliseconds=\"500\" waitMilliseconds=\"2000\" />");
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(new NudgeSettings(7,500,2000)),"Settings from version 1.1 remain compatible with automatic stop disabled",lines);
                    SettingsStore.Save(settingsFile,maximum);
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(maximum),"Updating saved settings replaces the previous values",lines);
                    var previousCulture=System.Threading.Thread.CurrentThread.CurrentCulture;
                    try {
                        System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("de-DE");
                        SettingsStore.Save(settingsFile,new NudgeSettings(9,150,3300));
                        Check(SettingsStore.Load(settingsFile,out notice).SameAs(new NudgeSettings(9,150,3300)),"Settings are independent of decimal separator and locale",lines);
                    } finally {System.Threading.Thread.CurrentThread.CurrentCulture=previousCulture;}
                    File.WriteAllText(settingsFile,"broken <xml");
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(NudgeSettings.Defaults) && notice!=null,"Malformed settings fall back to defaults with a notice",lines);
                    File.WriteAllText(settingsFile,"<MouseNudgeSettings version=\"1\" distance=\"-7\" returnMilliseconds=\"300\" waitMilliseconds=\"10000\" />");
                    Check(SettingsStore.Load(settingsFile,out notice).SameAs(NudgeSettings.Defaults) && notice!=null,"Invalid saved values cannot reach native mouse input",lines);
                    string blockedPath=Path.Combine(directory,"directory-instead-of-file");Directory.CreateDirectory(blockedPath);bool saveFailed=false;
                    try {SettingsStore.Save(blockedPath,NudgeSettings.Defaults);}catch(IOException) {saveFailed=true;}
                    Check(saveFailed && Directory.GetFiles(directory,"*.tmp").Length==0,"A failed save cleans up its temporary file",lines);
                } finally {Directory.Delete(directory,true);}
                lines.Add("All checks passed. UI and native movement are verified separately.");
                File.WriteAllLines(path,lines.ToArray());return 0;
            } catch(Exception ex) {lines.Add("FAIL "+ex.Message);File.WriteAllLines(path,lines.ToArray());return 1;}
        }
    }
}
