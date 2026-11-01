using System;
using System.Threading;

namespace MouseNudge {
    // Tracks active session time using the same monotonic clock as mouse movement.
    public sealed class SessionLimit {
        long duration,remaining,activeSince;
        bool active;
        public bool Enabled {get {return duration>0;}}
        public void Configure(int minutes,long now,bool running) {
            if(minutes<0 || minutes>1440)throw new ArgumentOutOfRangeException("minutes");
            duration=(long)minutes*60000;remaining=duration;activeSince=now;active=running;
        }
        public long Remaining(long now) {
            if(!Enabled)return long.MaxValue;
            return Math.Max(0,remaining-(active?Math.Max(0,now-activeSince):0));
        }
        public void Resume(long now) {
            if(active)return;
            if(Enabled && remaining==0)remaining=duration;
            activeSince=now;active=true;
        }
        public void Pause(long now) {if(active) {remaining=Remaining(now);active=false;}}
        public bool Expired(long now) {return Enabled && Remaining(now)==0;}
    }

    public sealed class InstanceSignals:IDisposable {
        public readonly EventWaitHandle Activate,Pause,Quit;
        public const string Prefix="Local\\MouseNudge.";
        public InstanceSignals(string suffix) {
            Activate=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"Activate.1"+suffix);
            try {
                Pause=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"Pause.1"+suffix);
                Quit=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"Quit.1"+suffix);
            } catch {Activate.Dispose();if(Pause!=null)Pause.Dispose();throw;}
        }
        public static int Send(string action,string suffix) {
            if(action!="Activate" && action!="Pause" && action!="Quit")return 2;
            try {using(var signal=EventWaitHandle.OpenExisting(Prefix+action+".1"+suffix))signal.Set();return 0;}
            catch(WaitHandleCannotBeOpenedException) {return 2;}
            catch(UnauthorizedAccessException) {return 3;}
        }
        public void Dispose() {Quit.Dispose();Pause.Dispose();Activate.Dispose();}
    }
}
