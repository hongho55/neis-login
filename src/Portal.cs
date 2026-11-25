using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

internal sealed class CertificateInfo {
 public string User,Expiry,Issuer;
 public override string ToString(){return User+" | "+Expiry+" | "+Issuer;}
}
internal sealed class PortalContext {internal IntPtr Hwnd;internal AutomationElement Window,Doc;internal string Path;}
internal sealed class BrowserTab {internal IntPtr Hwnd;internal AutomationElement Window,Tab;}

internal sealed class SystemMenuContext {internal PortalContext Portal;internal AutomationElement Link;}

internal static partial class Portal {
 internal static string Origin{get{return PortalConfig.Current.Origin;}}
 internal static string LoginPath{get{return PortalConfig.Current.LoginPath;}}
 internal static string MainPath{get{return PortalConfig.Current.MainPath;}}
 static readonly OperationSafety operation=new OperationSafety();
 internal static bool Cancelled {get{return operation.Cancelled;}}
 internal static void BeginOperation(){operation.Begin();securityGate=null;}
 internal static bool RequestStop(){return operation.RequestStop();}
 internal static void Input(Action persistPending,Action input){operation.Input(persistPending,input);}
 internal static void SubmitOnce(Action dispatch){operation.Submit(dispatch);}
 internal static Action<string> Report=s=>{};
 internal static Action<string> Milestone=s=>{};
 internal static IntPtr ReturnWindowHint,HelperWindow;
 static IntPtr workWindow;static uint workProcess;static string workProfile,workPortalTabKey;
 static readonly WindowLaunchState launch=new WindowLaunchState();
 static ForegroundReturn focusReturn;
  static void ReturnFocus(FocusPoint point){
  var restore=focusReturn;
  if(restore!=null)restore.TryReturn(outcome=>ForegroundTrace.Sample(restore.KeepWorkForeground?workWindow:restore.Target,point,outcome));
 }
 internal static void CheckCancel(){operation.Check();}
  internal static List<AutomationElement> All(AutomationElement e,ControlType t){
  return e.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,t)).Cast<AutomationElement>().ToList();
 }
 internal static AutomationElement Id(AutomationElement e,string id){return e==null?null:e.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));}
 internal static List<Tuple<IntPtr,AutomationElement>> Windows(){
  var result=new List<Tuple<IntPtr,AutomationElement>>();
   Native.EnumWindows((h,p)=>{
   uint pid;
   Native.GetWindowThreadProcessId(h,out pid);
    try{
    if(Native.IsWindowVisible(h)&&Process.GetProcessById((int)pid).ProcessName==PortalConfig.Current.BrowserProcess)result.Add(Tuple.Create(h,AutomationElement.FromHandle(h)));
    }catch(ArgumentException){
    }catch(ElementNotAvailableException){
   }
   return true;
  },IntPtr.Zero);
  return result;
 }
 static bool IsPortalTab(AutomationElement t){return PortalTabPolicy.IsPortalName(t.Current.Name);}
 internal static string DocUrl(AutomationElement e){object p;if(!e.Current.IsPassword&&e.TryGetCurrentPattern(ValuePattern.Pattern,out p))return ((ValuePattern)p).Current.Value;return "";}
 internal static void Activate(IntPtr hwnd,AutomationElement tab,bool foreground){
  if(foreground&&!Native.EnsureForeground(hwnd,FocusPoint.ACTIVATE_REQUEST,hwnd==workWindow?workProcess:0)){
   ForegroundTrace.Sample(hwnd,FocusPoint.DIRECT_CONTROL_FALLBACK);Report("창 전환 대기 없이 확인된 업무 탭을 직접 선택합니다.");
  }
  object p;
  if(!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p))throw new SafeFailure("Edge 탭을 선택할 수 없습니다.");
  var selection=(SelectionItemPattern)p;if(!selection.Current.IsSelected){selection.Select();Thread.Sleep(PortalConfig.Current.TabSettleMs);}
  ForegroundTrace.Sample(hwnd,foreground?FocusPoint.TAB_FOREGROUND:FocusPoint.TAB_BACKGROUND);
 }
 internal static void Launch(string profile){
  RunTrace.Record(TraceEvent.EDGE_LAUNCH_REQUEST,TraceStage.EDGE_LAUNCH);
  string arguments=WindowFlow.LaunchArguments(profile,Origin+LoginPath);
  string exe=FindEdgeExecutable();if(exe==null)throw new SafeFailure("Microsoft Edge 설치 경로를 찾지 못했습니다.");
  Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=true});
 }
 // Fixed install locations only; the executable path is intentionally not configurable.
 internal static string FindEdgeExecutable(){
  string[] paths={
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft","Edge","Application","msedge.exe")
  };
  return paths.FirstOrDefault(File.Exists);
 }
 static Tuple<IntPtr,AutomationElement> BoundWindow(){
  if(workWindow==IntPtr.Zero)return null;uint pid;Native.GetWindowThreadProcessId(workWindow,out pid);
  if(pid!=workProcess||!Native.IsWindow(workWindow))return null;
  return Windows().SingleOrDefault(w=>w.Item1==workWindow);
 }
 static void EnsureWindow(string profile,bool allowCreate){
  CheckCancel();if(workProfile!=profile||BoundWindow()==null){
   if(!allowCreate)throw new SafeFailure("업무 전용 창이 닫혔거나 바뀌었어요. 새 창에서 자동으로 재시도하지 않습니다.");
   if(workProfile!=profile)launch.Reset();
   launch.PrepareExplicitRetry(Windows().Select(w=>w.Item1));
   try{
    if(!launch.Pending){
     // Existing browser windows are excluded; a failed request is only retried explicitly.
     workProfile=profile;workWindow=IntPtr.Zero;workPortalTabKey=null;launch.Begin(Windows().Select(w=>w.Item1));
     Report("업무 전용 Edge 창을 열고 있어요.");Launch(profile);
    }
    var found=Wait(()=>{
     var windows=Windows();var candidates=windows.Where(w=>All(w.Item2,ControlType.TabItem).Any(IsPortalTab)).ToList();
     IntPtr chosen=WindowFlow.NewWindow(candidates.Select(w=>w.Item1),launch.Before);
     return chosen==IntPtr.Zero?null:candidates.Single(w=>w.Item1==chosen);
    },30000,"새 업무 창이 준비되지 않았어요. 요청 창이 남아 있으면 닫힌 뒤 업무 시작을 다시 눌러 주세요. 자동으로 창을 반복 생성하지 않습니다.",TraceStage.EDGE_NEW_WINDOW);
    workWindow=found.Item1;Native.GetWindowThreadProcessId(workWindow,out workProcess);launch.Reset();
   }catch{launch.MarkFailed();throw;}
  }
  if(focusReturn!=null){focusReturn.Own(workWindow);ReturnFocus(FocusPoint.EDGE_CREATED_RETURN);}
 }
 static PortalContext EnsurePortal(string profile,bool foreground,bool allowCreate=true){
  EnsureWindow(profile,allowCreate);var window=BoundWindow();
  if(window==null)throw new SafeFailure("업무 전용 창이 닫혔어요. 다른 창으로 대상을 바꾸지 않습니다.");
  var tab=Wait(()=>{
   CheckCancel();if(BoundWindow()==null)throw new SafeFailure("업무 창이 닫혔거나 바뀌었습니다. 다른 창으로 대신 진행하지 않습니다.");
   var tabs=All(window.Item2,ControlType.TabItem).Select(t=>new {Element=t,Key=String.Join("-",t.GetRuntimeId()),Name=t.Current.Name}).ToList();
   string key=PortalTabPolicy.Resolve(tabs.Select(t=>new PortalTabFact{Key=t.Key,Name=t.Name}),workPortalTabKey);
   return key==null?null:tabs.Single(t=>t.Key==key).Element;
  },15000,"업무포털 탭 표시가 아직 안정화되지 않았습니다. 로그인은 다시 제출하지 않고 중단합니다.",TraceStage.PORTAL_TAB);
  workPortalTabKey=String.Join("-",tab.GetRuntimeId());
  Activate(window.Item1,tab,foreground||(focusReturn!=null&&focusReturn.KeepWorkForeground));
  if(!foreground&&focusReturn!=null)ReturnFocus(FocusPoint.PORTAL_TAB_RETURN);
  return Wait(()=>Locate(profile),30000,"업무포털 화면을 확인하지 못했어요. 창을 최소화하거나 탭을 바꿨는지 확인해 주세요.",TraceStage.PORTAL_DOCUMENT);
 }
 // Polling observes only. It never launches, selects a tab, or changes focus.
 internal static PortalContext Locate(string profile){
  CheckCancel();if(workProfile!=profile)return null;var window=BoundWindow();if(window==null)return null;
  var candidates=new List<PortalContext>();
  foreach(var d in All(window.Item2,ControlType.Document)){
   Uri u;if(!Uri.TryCreate(DocUrl(d),UriKind.Absolute,out u))continue;
   if((PortalConfig.Current.IsPortalUrl(u,LoginPath)||PortalConfig.Current.IsPortalUrl(u,MainPath))&&!d.Current.IsOffscreen&&DocumentBelongsToWindow(d,window.Item1))
    candidates.Add(new PortalContext{Hwnd=window.Item1,Window=window.Item2,Doc=d,Path=u.AbsolutePath});
  }
  // Chromium can temporarily expose more than one document. Never choose by tree order.
  return candidates.Count==1?candidates[0]:null;
 }
 internal static bool DocumentBelongsToWindow(AutomationElement doc,IntPtr hwnd){
  for(var current=doc;current!=null;current=TreeWalker.RawViewWalker.GetParent(current)){
   int native=current.Current.NativeWindowHandle;
   if(native!=0)return Native.GetAncestor(new IntPtr(native),2)==hwnd;
  }
  return false;
 }
 internal static T Wait<T>(Func<T> f,int milliseconds,string error,TraceStage? stage=null)where T:class {
  if(stage.HasValue)RunTrace.Record(TraceEvent.WAIT_BEGIN,stage,limitMs:milliseconds);
  try{
   var sw=Stopwatch.StartNew();long securityAtStart=SecurityPaused;
    while(sw.ElapsedMilliseconds-(SecurityPaused-securityAtStart)<milliseconds){
    CheckCancel();
     try{
     T x=f();
      if(x!=null){
      if(stage.HasValue)RunTrace.Record(TraceEvent.WAIT_READY,stage);
      return x;
     }
     }catch(ElementNotAvailableException){
     }catch(System.Runtime.InteropServices.COMException){
    }
    Thread.Sleep(PortalConfig.Current.PollIntervalMs);
   }
  }catch(Exception ex){if(stage.HasValue)RunTrace.Record(TraceEvent.WAIT_FAILED,stage,ex);throw;}
  if(stage.HasValue)RunTrace.Record(TraceEvent.WAIT_TIMEOUT,stage,limitMs:milliseconds);throw new SafeFailure(error);
 }
 internal static void Validate(PortalContext c,string path,bool requireForeground){
  CheckCancel();Uri u;if(!Uri.TryCreate(DocUrl(c.Doc),UriKind.Absolute,out u)||!PortalConfig.Current.IsPortalUrl(u,path))throw new SafeFailure("현재 문서 URL이 등록 대상과 다릅니다.");
  var bars=All(c.Window,ControlType.Edit).Where(e=>!e.Current.IsPassword&&e.Current.Name=="주소 표시줄 및 검색 창").ToList();
  if(bars.Count!=1)throw new SafeFailure("현재 Edge 주소 표시줄을 하나로 확인할 수 없습니다.");
  string address=DocUrl(bars[0]);if(!address.Contains("://"))address="https://"+address;
  Uri bar;if(!Uri.TryCreate(address,UriKind.Absolute,out bar)||!PortalConfig.Current.IsPortalUrl(bar,path))throw new SafeFailure("활성 탭 주소가 업무포털 대상과 일치하지 않습니다.");
  if(requireForeground&&Native.GetForegroundWindow()!=c.Hwnd)throw new SafeFailure("입력 직전 대상 창이 바뀌었습니다. 입력하지 않고 중단합니다.");
 }
 // Setup only opens or selects the portal. The tutorial lets the user open the certificate window.
 internal static PortalContext OpenForSetup(string profile){
  Report("업무포털을 열고 있어요. 잠시만 기다려 주세요.");
  var c=EnsurePortal(profile,true);
  Validate(c,c.Path,false);return c;
 }
}
