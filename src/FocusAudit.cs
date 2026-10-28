using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Observation only. No activation, input injection, key hooks, or focus restoration.
internal sealed class FocusAudit:IDisposable {
 internal sealed class Entry {public string utc,stage,kind;public uint eventTick,pid;public long hwnd;}
 delegate void Callback(IntPtr hook,uint kind,IntPtr hwnd,int objectId,int child,uint thread,uint time);
 [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,Callback callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll")]static extern bool PostThreadMessage(uint thread,uint message,UIntPtr w,IntPtr l);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 readonly object sync=new object();readonly List<Entry> entries=new List<Entry>();readonly HashSet<string> targets=new HashSet<string>();
 readonly ManualResetEvent ready=new ManualResetEvent(false);readonly Thread monitor;Callback callback;IntPtr hook;uint monitorId;string stage="BASELINE";bool overflow;
 internal FocusAudit(){monitor=new Thread(Monitor){IsBackground=true};monitor.SetApartmentState(ApartmentState.STA);monitor.Start();if(!ready.WaitOne(5000)||hook==IntPtr.Zero)throw new SafeFailure("전면 창 이벤트 관찰을 시작하지 못했어요. 시험하지 않습니다.");Record(Native.GetForegroundWindow(),"sample",0);}
 void Monitor(){monitorId=GetCurrentThreadId();callback=(a,b,h,c,d,e,t)=>Record(h,"foreground-event",t);hook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);ready.Set();if(hook==IntPtr.Zero)return;try{Application.Run();}finally{UnhookWinEvent(hook);}}
 void Record(IntPtr hwnd,string kind,uint tick){uint pid;Native.GetWindowThreadProcessId(hwnd,out pid);lock(sync){if(entries.Count>=4000){overflow=true;return;}entries.Add(new Entry{utc=DateTimeOffset.UtcNow.ToString("o"),stage=stage,kind=kind,eventTick=tick,hwnd=hwnd.ToInt64(),pid=pid});}}
 string Key(long hwnd,uint pid){return hwnd.ToString()+":"+pid.ToString();}
 internal void AddTarget(IntPtr hwnd){uint pid;Native.GetWindowThreadProcessId(hwnd,out pid);lock(sync)targets.Add(Key(hwnd.ToInt64(),pid));}
 internal void Stage(string name){lock(sync)stage=name;Record(Native.GetForegroundWindow(),"stage-sample",0);AssertBackground();}
 internal void AssertBackground(){Record(Native.GetForegroundWindow(),"sample",0);lock(sync){if(overflow)throw new SafeFailure("전면 창 기록 한도를 넘어 백그라운드 성공을 판정하지 않습니다.");if(entries.Any(e=>targets.Contains(Key(e.hwnd,e.pid))))throw new SafeFailure("업무 창이 전면으로 나타난 기록이 있어요. 완전 백그라운드 성공으로 처리하지 않고 중단합니다.");}}
 internal object Snapshot(){lock(sync)return new {hookActive=hook!=IntPtr.Zero,overflow=overflow,targetForegroundEvents=entries.Count(e=>e.kind=="foreground-event"&&targets.Contains(Key(e.hwnd,e.pid))),targetForegroundSamples=entries.Count(e=>e.kind!="foreground-event"&&targets.Contains(Key(e.hwnd,e.pid))),events=entries.ToArray()};}
 public void Dispose(){if(monitorId!=0)PostThreadMessage(monitorId,0x12,UIntPtr.Zero,IntPtr.Zero);monitor.Join(2000);ready.Dispose();}
}
