using System;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Automation;

internal static class SecurityLoadingTests {
 internal static void Run(Action<bool,string> check){
  long normalTime=0;int normalSleeps=0,normalReports=0;
  var conditional=new SecurityLoadingGate(()=>normalTime,ms=>{normalTime+=ms;normalSleeps++;},()=>{},s=>normalReports++);
  Func<SecurityLoadingSample> normal=()=>new SecurityLoadingSample(SecurityLoadingState.Clear,"doc");
  for(int stage=0;stage<10;stage++)conditional.WaitWhenBlocked(normal);
  check(normalTime==0&&normalSleeps==0&&normalReports==0&&conditional.PausedMilliseconds==0,"security/normal preparation stages never repeat security waiting or messages");
  int reads=0;conditional=new SecurityLoadingGate(()=>normalTime,ms=>normalTime+=ms,()=>{},s=>normalReports++,5000,1000,250);
  conditional.WaitWhenBlocked(()=>new SecurityLoadingSample(++reads<=2?SecurityLoadingState.Loading:SecurityLoadingState.Clear,"doc"));
  check(normalTime==1500&&normalReports>0,"security/wait and clear stabilization begin only after actual blocking observation");
  int reportsAfterRelease=normalReports;long afterRelease=normalTime;
  conditional.WaitWhenBlocked(normal);conditional.WaitWhenBlocked(normal);
  check(normalReports==reportsAfterRelease&&normalTime==afterRelease,"security/after release later normal stages have no repeated security confirmation");
  long now=0;int sleeps=0;var state=SecurityLoadingState.Loading;string identity="window/doc";
  Func<SecurityLoadingSample> detector=()=>new SecurityLoadingSample(state,identity);
  var gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;sleeps++;if(now>=500)state=SecurityLoadingState.Clear;},()=>{},s=>{},5000,1000,250);
  gate.Wait(detector);check(now==1500&&sleeps==6,"security/initial loading cooperatively waits then stabilizes clear");
  now=0;state=SecurityLoadingState.Clear;
  gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;if(now==500)state=SecurityLoadingState.Loading;if(now==750)state=SecurityLoadingState.Clear;},()=>{},s=>{},5000,1000,250);
  gate.Wait(detector);check(now==1750,"security/loading appearing during preparation resets clear stability");
  now=0;identity="old";state=SecurityLoadingState.Clear;
  gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;if(now==750)identity="new";},()=>{},s=>{},5000,1000,250);
  gate.Wait(detector);check(now==1750,"security/document or window replacement requires fresh stable identity");
  now=0;state=SecurityLoadingState.Unknown;
  gate=new SecurityLoadingGate(()=>now,ms=>now+=ms,()=>{},s=>{},1000,500,250);
  bool timeout=false;try{gate.Wait(detector);}catch(SafeFailure){timeout=true;}
  check(timeout&&now==1000,"security/unknown never becomes clear and has bounded wait");
  now=0;gate=new SecurityLoadingGate(()=>now,ms=>now+=ms,()=>{},s=>{},1000,500,250);
  timeout=false;try{gate.Wait(()=>{throw new InvalidOperationException();});}catch(SafeFailure){timeout=true;}
  check(timeout&&now==1000,"security/detector exception fails closed within budget");
  now=0;bool cancelled=false;gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;cancelled=true;},()=>{if(cancelled)throw new SafeFailure("cancelled");},s=>{},5000,1000,250);
  bool stopped=false;try{gate.Wait(detector);}catch(SafeFailure){stopped=true;}
  check(stopped&&now==250,"security/stop remains responsive while waiting");
  now=0;state=SecurityLoadingState.Clear;gate=new SecurityLoadingGate(()=>now,ms=>now+=ms,()=>{},s=>{},3000,500,250);
  gate.Wait(detector);long paused=gate.PausedMilliseconds;gate.Wait(detector);
  check(paused==500&&gate.PausedMilliseconds==1000,"security/security budget accounts separately across gates");
  check(SecurityLoadingSample.FromVisibility(new[]{false,true,true},"doc").State==SecurityLoadingState.Loading,"security/any of multiple overlays blocks");
  check(SecurityLoadingSample.FromVisibility(new bool[0],"doc").State==SecurityLoadingState.Clear,"security/observable empty overlay set clear");
  check(SecurityLoadingSample.FromVisibility(null,"doc").State==SecurityLoadingState.Unknown,"security/unobservable frame unknown");
  int inputs=0,submits=0;bool pending=false;var operation=new OperationSafety();
  operation.Input(()=>pending=true,()=>inputs++);state=SecurityLoadingState.Loading;
  stopped=false;try{SecurityLoadingGate.RequireClear(detector());operation.Submit(()=>submits++);}catch(SafeFailure){stopped=true;}
  check(stopped&&pending&&inputs==1&&submits==0,"security/loading after input preserves pending and never reinputs or submits");
  state=SecurityLoadingState.Unknown;stopped=false;try{SecurityLoadingGate.RequireClear(detector());operation.Submit(()=>submits++);}catch(SafeFailure){stopped=true;}
  check(stopped&&submits==0,"security/final unknown state blocks submission");
  state=SecurityLoadingState.Clear;SecurityLoadingGate.RequireClear(detector());operation.Submit(()=>submits++);
  stopped=false;try{operation.Submit(()=>submits++);}catch(SafeFailure){stopped=true;}
  check(stopped&&submits==1,"security/existing submission boundary still dispatches at most once");
  // Clear describes the overlay only; downstream readiness must still succeed.
  now=0;gate=new SecurityLoadingGate(()=>now,ms=>now+=ms,()=>{},s=>{},3000,500,250);gate.Wait(detector);
  int readyChecks=0,dispatches=0;Func<object> prepare=()=>{gate.Wait(detector);readyChecks++;return null;};
  var prepared=prepare();if(prepared!=null)dispatches++;
  check(readyChecks==1&&dispatches==0,"security/clear resumes downstream readiness check and missing target never dispatches");
  now=0;identity="doc";state=SecurityLoadingState.Clear;
  gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;if(now==750)state=SecurityLoadingState.Unknown;if(now==1000)state=SecurityLoadingState.Clear;},()=>{},s=>{},4000,1000,250);
  gate.Wait(detector);check(now==2000,"security/unknown during clear stabilization discards previous interval");
  now=0;state=SecurityLoadingState.Clear;
  gate=new SecurityLoadingGate(()=>now,ms=>{now+=ms;if(now==500||now==1250)state=SecurityLoadingState.Loading;if(now==750||now==1500)state=SecurityLoadingState.Clear;},()=>{},s=>{},5000,1000,250);
  gate.Wait(detector);check(now==2500,"security/loading recurrence after release restarts full stabilization");
  now=0;gate=new SecurityLoadingGate(()=>now,ms=>now+=ms,()=>{},s=>{},1000,0,250);
  timeout=false;try{gate.Wait(()=>{now+=1500;return detector();});}catch(SafeFailure){timeout=true;}
  check(timeout,"security/slow detector cannot return ready after security deadline");
  using(var stream=typeof(SecurityLoadingDetector).Assembly.GetManifestResourceStream("SecurityLoading.png"))using(var reference=new Bitmap(stream)){
   check(SecurityLoadingDetector.Difference(reference,new Rectangle(0,0,350,187))==0,"security/official embedded reference matches exactly");
   foreach(double scale in new[]{0.5,1.0,1.25,1.37,1.5,2.0,2.25,3.0})using(var frame=new Bitmap(1400,900)){
    int w=(int)Math.Round(350*scale),h=(int)Math.Round(187*scale);
    using(var graphics=Graphics.FromImage(frame)){graphics.Clear(Color.White);graphics.DrawImage(reference,new Rectangle((1400-w)/2,(int)Math.Round(450-100*scale),w,h));}
    check(SecurityLoadingDetector.Analyze(frame)==SecurityLoadingState.Loading,"security/whole-frame detector recognizes effective DPI/zoom scale "+scale);
    var region=new Rectangle(175,150,1050,561);
    using(var cropped=frame.Clone(region,frame.PixelFormat))check(SecurityLoadingDetector.Analyze(cropped,frame.Size,region.Location)==SecurityLoadingState.Loading,"security/cropped observation retains loading detection at scale "+scale);
   }
   using(var scaled=new Bitmap(525,281)){
    using(var graphics=Graphics.FromImage(scaled))graphics.DrawImage(reference,new Rectangle(0,0,525,281));
    check(SecurityLoadingDetector.Difference(scaled,new Rectangle(0,0,525,281))<13,"security/reference matches at 150 percent effective scale");
   }
   using(var blank=new Bitmap(350,187)){
    using(var graphics=Graphics.FromImage(blank))graphics.Clear(Color.White);
    check(SecurityLoadingDetector.Difference(blank,new Rectangle(0,0,350,187))>30,"security/blank page is not mistaken for the loading reference");
   }
  }
 }
 // Explicit read-only developer mode: never loads settings, accesses Vault, invokes or focuses UI.
 internal static int Observe(long hwnd,int duration){
  try{return ObserveCore(hwnd,duration);}catch(Exception e){Console.WriteLine("UNKNOWN "+e.GetType().FullName+" HRESULT="+e.HResult);return 2;}
 }
 static int ObserveCore(long hwnd,int duration){
  var window=AutomationElement.FromHandle(new IntPtr(hwnd));
  if(Process.GetProcessById(window.Current.ProcessId).ProcessName!="msedge")return 2;
  var clock=Stopwatch.StartNew();
  while(clock.ElapsedMilliseconds<duration){
   var documents=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)).Cast<AutomationElement>().Where(d=>!d.Current.IsOffscreen&&Portal.DocUrl(d)=="https://gwe.eduptl.kr/bpm_lgn_lg00_001.do").ToArray();
   if(documents.Length!=1){Console.WriteLine(DateTime.UtcNow.ToString("o")+" Unknown matchingDocuments="+documents.Length);System.Threading.Thread.Sleep(100);continue;}var doc=documents[0];
   SecurityLoadingSample sample;
   try{sample=SecurityLoadingDetector.Observe(doc==null?null:new PortalContext{Hwnd=new IntPtr(hwnd),Window=window,Doc=doc,Path=Portal.LoginPath});}
   catch(Exception){sample=new SecurityLoadingSample(SecurityLoadingState.Unknown,null);}
   Console.WriteLine(DateTime.UtcNow.ToString("o")+" "+sample.State+" "+sample.Reason);Console.Out.Flush();System.Threading.Thread.Sleep(100);
  }
  return 0;
 }
}
