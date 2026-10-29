using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class QuietModeTests {
 internal static int HiddenGuideChild(){
  bool visible=false;int work=0;
  var services=new AutoRunServices{Validate=s=>"TEST_SETUP_REQUIRED",TrayVisible=false,Record=s=>{},Run=s=>{work++;return true;},Fatal=s=>{},CreateMain=(s,message,open)=>{
   var form=new Form{ShowInTaskbar=false,Opacity=0,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)};
   form.HandleCreated+=(o,e)=>form.BeginInvoke((Action)(()=>{visible=IsWindowVisible(form.Handle);form.Close();}));return form;
  }};
  using(var context=new AutoRunContext(new Settings(),services))using(var timeout=new System.Windows.Forms.Timer{Interval=5000}){
   timeout.Tick+=(o,e)=>context.ExitThread();timeout.Start();Application.Run(context);
   return visible&&work==0&&context.ResourcesReleased?0:1;
  }
 }
 sealed class Guide:Form {
  protected override bool ShowWithoutActivation {get{return true;}}
  internal Guide(){ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);Shown+=(o,e)=>BeginInvoke((Action)(()=>Close()));}
 }
 sealed class Result {
  internal int Work,Forms,Fatal;internal bool TimedOut,Released,SettingsOpened,WorkerDone,HasMainDuringWork;
  internal string Notice="";internal readonly List<string> Records=new List<string>();
 }
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
 static Settings Ready(){return new Settings{Verified=true,CertUser="TEST_ONLY",CertIssuer="TEST_ONLY",CertExpiry="2099-12-31"};}
 static Result Execute(Settings settings,Func<Settings,string> validate,Func<Settings,Result,bool> work,Action<AutoRunContext,Result> tick=null,bool logFailed=false,bool factoryFails=false,bool visibleTray=false){
  var result=new Result();AutoRunContext context=null;
  var services=new AutoRunServices{
   Validate=validate,LogFailed=()=>logFailed,TrayVisible=visibleTray,
   Run=s=>{result.Work++;result.HasMainDuringWork=context.HasMainWindow;try{return work(s,result);}finally{result.WorkerDone=true;}},
   Record=code=>result.Records.Add(code),Fatal=message=>result.Fatal++,
   CreateMain=(s,message,open)=>{result.Forms++;result.Notice=message;result.SettingsOpened=open;if(factoryFails)throw new InvalidOperationException("injected guide failure");return new Guide();}
  };
  using(context=new AutoRunContext(settings,services))using(var timer=new System.Windows.Forms.Timer{Interval=10})using(var timeout=new System.Windows.Forms.Timer{Interval=5000}){
   timer.Tick+=(o,e)=>{if(tick!=null)tick(context,result);};
   timeout.Tick+=(o,e)=>{result.TimedOut=true;timeout.Stop();context.RequestStop();if(!context.Running)context.ExitThread();};
   timer.Start();timeout.Start();Application.Run(context);timer.Stop();timeout.Stop();result.Released=context.ResourcesReleased;
  }
  if(result.TimedOut)throw new Exception("Quiet mode message loop timed out");
  return result;
 }
 internal static void Run(Action<bool,string> check){
  int exists=0;var ready=Ready();
  check(AutoRunEligibility.Check(ready,()=>{exists++;return true;})==null&&exists==1,"quiet/verified setup accepted");
  var pending=Ready();pending.PendingAttempt=true;exists=0;
  check(AutoRunEligibility.Check(pending,()=>{exists++;return true;}).Contains("미확인")&&exists==0,"quiet/pending rejected before credential lookup");
  var incomplete=Ready();incomplete.Verified=false;exists=0;
  check(AutoRunEligibility.Check(incomplete,()=>{exists++;return true;})!=null&&exists==0,"quiet/incomplete setup rejected before credential lookup");
  var expired=Ready();expired.CertExpiry="2000-01-01";exists=0;
  check(AutoRunEligibility.Check(expired,()=>{exists++;return true;})!=null&&exists==0,"quiet/expired certificate does not start worker");
  var badProfile=Ready();badProfile.Profile="Default\" --extra";exists=0;
  check(AutoRunEligibility.Check(badProfile,()=>{exists++;return true;})!=null&&exists==0,"quiet/invalid profile does not reach credential lookup");
  check(AutoRunEligibility.Check(Ready(),()=>false)!=null,"quiet/missing saved credential requires guidance");

  int hotkeyCalls=0;using(var window=new StopHotkeyWindow(()=>hotkeyCalls++)){
   check(window.Handle!=IntPtr.Zero&&!IsWindowVisible(window.Handle),"quiet/hotkey receiver is hidden without main form");
   SendMessage(window.Handle,0x312,new IntPtr(1),IntPtr.Zero);check(hotkeyCalls==1,"quiet/hotkey message reaches stop callback without main form");
   window.Dispose();check(window.Handle==IntPtr.Zero,"quiet/hotkey receiver handle disposed");
  }

  var success=Execute(Ready(),s=>null,(s,r)=>{Portal.Milestone("EDGE_READY");Portal.Report("TEST_PROGRESS");Portal.Milestone("CERTIFICATE_READY");Portal.Milestone("LOGIN_COMPLETE");return true;});
  check(success.Work==1&&success.Forms==0&&!success.HasMainDuringWork,"quiet/success starts exactly one worker and no main form");
  check(success.Released&&success.WorkerDone&&success.Records.Contains("AUTO_SUCCESS"),"quiet/success exits message loop after work and releases resources");
  check(!success.Records.Contains("AUTO_GUIDANCE_OPENED"),"quiet/success never requests guidance window");
  var timing=success.Records.Where(x=>x.StartsWith("TIMING_")).ToArray();
  check(timing.Length==4&&timing.All(x=>Regex.IsMatch(x,"^TIMING_(HELPER_START|EDGE_READY|CERTIFICATE_READY|LOGIN_COMPLETE) elapsed_ms=[0-9]+$")),"quiet/four timing stages contain only names and elapsed times");
  var stageList=new List<string>();var clock=new AutoRunTiming(stageList.Add);clock.Mark("EDGE_READY");clock.Mark("EDGE_READY");
  check(stageList.Count==1,"quiet/timing stage recorded once");bool unknownRejected=false;try{clock.Mark("UNTRUSTED_VALUE");}catch(ArgumentException){unknownRejected=true;}
  check(unknownRejected,"quiet/unrecognized timing payload not logged");
  bool releaseTrayWork=false,observedTray=false;int trayDisposed=0;NotifyIcon watchedTray=null;
  var trayResult=Execute(Ready(),s=>null,(s,r)=>{while(!Volatile.Read(ref releaseTrayWork)&&!Portal.Cancelled)Thread.Sleep(2);return true;},(ctx,r)=>{
   if(ctx.Running&&!observedTray){watchedTray=(NotifyIcon)typeof(AutoRunContext).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ctx);if(watchedTray!=null){observedTray=watchedTray.Visible;watchedTray.Disposed+=(o,e)=>trayDisposed++;Volatile.Write(ref releaseTrayWork,true);}}
  },false,false,true);
  check(observedTray&&trayResult.Forms==0,"quiet/real tray icon is visible while running without main form");
  check(trayResult.Released&&trayDisposed==1&&!watchedTray.Visible,"quiet/real tray icon disposed once on successful exit");

  var missing=Execute(new Settings(),s=>AutoRunEligibility.Check(s,()=>{throw new Exception("must not read credentials");}),(s,r)=>true);
  check(missing.Forms==1&&missing.Work==0&&missing.Notice.Contains("설정"),"quiet/incomplete setup opens guidance without work");
  var pendingResult=Execute(pending,s=>AutoRunEligibility.Check(s,()=>{throw new Exception("must not read credentials");}),(s,r)=>true);
  check(pendingResult.Forms==1&&pendingResult.Work==0&&pending.PendingAttempt,"quiet/pending preserved while showing guidance");
  var failed=Execute(Ready(),s=>null,(s,r)=>{throw new SafeFailure("INJECTED_WORK_FAILURE");});
  check(failed.Work==1&&failed.Forms==1&&failed.Notice.Contains("INJECTED_WORK_FAILURE")&&failed.Released,"quiet/error opens guidance after resource cleanup");
  check(!failed.Records.Contains("AUTO_SUCCESS"),"quiet/error never records success or retries");
  var falseResult=Execute(Ready(),s=>null,(s,r)=>false);
  check(falseResult.Forms==1&&!falseResult.Records.Contains("AUTO_SUCCESS"),"quiet/false result is not success");

  bool stopped=false;int submits=0;
  var stopResult=Execute(Ready(),s=>null,(s,r)=>{
   Portal.Input(()=>{s.PendingAttempt=true;},()=>{});
   while(!Portal.Cancelled)Thread.Sleep(2);
   Portal.SubmitOnce(()=>submits++);return true;
  },(ctx,r)=>{if(ctx.Running&&!stopped){stopped=true;ctx.RequestStop();}});
  check(stopResult.Work==1&&stopResult.Forms==1&&submits==0,"quiet/tray stop before submit prevents dispatch without retry");

  bool asked=false;var settingState=Ready();int alreadySubmitted=0;using(var submitted=new ManualResetEvent(false)){
   var settingResult=Execute(settingState,s=>null,(s,r)=>{
    Portal.Input(()=>{s.PendingAttempt=true;},()=>{});Portal.SubmitOnce(()=>alreadySubmitted++);submitted.Set();
    while(!Portal.Cancelled)Thread.Sleep(2);Portal.CheckCancel();return true;
   },(ctx,r)=>{if(ctx.Running&&submitted.WaitOne(0)&&!asked){asked=true;ctx.RequestSettings();}});
   check(settingResult.Work==1&&settingResult.Forms==1&&settingResult.SettingsOpened&&settingResult.WorkerDone,"quiet/settings waits for worker then opens same-process guidance");
   check(alreadySubmitted==1&&settingState.PendingAttempt,"quiet/settings request does not undo submission or clear pending");
  }
  var warning=Execute(Ready(),s=>null,(s,r)=>true,null,true);
  check(warning.Forms==1&&warning.Records.Contains("AUTO_SUCCESS")&&warning.Notice.Contains("완료"),"quiet/log warning keeps business success but presents warning");
  var factoryError=Execute(new Settings(),s=>"SETUP_REQUIRED",(s,r)=>true,null,false,true);
  check(factoryError.Fatal==1&&factoryError.Work==0&&factoryError.Released,"quiet/guide creation failure falls back once without work");
  var childInfo=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--quiet-hidden-guide"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
  using(var child=Process.Start(childInfo)){
   check(child.WaitForExit(10000)&&child.ExitCode==0,"quiet/hidden-process startup still shows required guide without work");
  }
 }
}
