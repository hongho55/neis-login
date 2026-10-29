using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;

internal static class RegressionTests {
 static int count;static string root;
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static bool Refused(Action action){try{action();return false;}catch(SafeFailure){return true;}}
 static IntPtr H(int id){return new IntPtr(id);}
 static PortalTabFact Tab(string id,string title){return new PortalTabFact{Key=id,Name=title};}
 static void Baseline(){
  Check(WindowFlow.LaunchArguments("Default","https://example.test").StartsWith("--new-window --profile-directory=\"Default\" "),"baseline/default profile new window");
  Check(WindowFlow.LaunchArguments("Profile 2","https://example.test").Contains("\"Profile 2\""),"baseline/numbered profile quoted");
  Check(Refused(()=>WindowFlow.LaunchArguments("Default\" --inject","x")),"baseline/profile injection rejected");
  Check(Refused(()=>WindowFlow.LaunchArguments(null,"x")),"baseline/null profile rejected");
  var before=new HashSet<IntPtr>{H(1),H(2)};
  Check(WindowFlow.NewWindow(new[]{H(1),H(3)},before)==H(3),"baseline/old windows excluded");
  Check(WindowFlow.NewWindow(new[]{H(1),H(2)},before)==IntPtr.Zero,"baseline/no new window does not reuse old");
  Check(WindowFlow.NewWindow(new[]{H(3),H(3)},before)==H(3),"baseline/duplicate enumeration de-duplicated");
  Check(Refused(()=>WindowFlow.NewWindow(new[]{H(3),H(4)},before)),"baseline/multiple new windows refused");
  Check(PortalTabPolicy.Resolve(new[]{Tab("p","업무포털 로그인")},null)=="p","baseline/login title resolves");
  Check(PortalTabPolicy.Resolve(new[]{Tab("p","SSO LOGIN")},"p")=="p","baseline/tab identity survives title transition");
  Check(PortalTabPolicy.Resolve(new[]{Tab("q","업무포털 메인")},"p")=="q","baseline/provider rebuild resolves unique portal");
  Check(PortalTabPolicy.Resolve(new[]{Tab("q","나이스")},null)==null,"baseline/no portal remains unresolved");
  Check(Refused(()=>PortalTabPolicy.Resolve(new[]{Tab("p","업무포털 메인"),Tab("q","업무포털 로그인")},"p")),"baseline/duplicate portals refused despite remembered id");
  IntPtr current=H(2);int activated=0;bool usable=true;
  var restore=new ForegroundReturn(H(1),()=>current,h=>usable,h=>{current=h;activated++;return true;});restore.Own(H(2));
  Check(restore.TryReturn()&&current==H(1)&&activated==1,"baseline/owned window returns to original");
  current=H(9);Check(!restore.TryReturn()&&current==H(9)&&activated==1,"baseline/user-selected app preserved");
  current=H(2);usable=false;Check(!restore.TryReturn()&&activated==1,"baseline/unusable original not activated");
  var missing=new ForegroundReturn(IntPtr.Zero,()=>H(2),h=>true,h=>{activated++;return true;});missing.Own(H(2));Check(!missing.TryReturn()&&activated==1,"baseline/no original window not activated");
 }
 static void Cancellation(){
  var gate=new OperationSafety();int persisted=0,input=0,submits=0;
  Check(!gate.RequestStop(),"A02/stop before input accepted");
  Check(Refused(()=>gate.Input(()=>persisted++,()=>input++))&&persisted==0&&input==0,"A02/no state or input after pre-input stop");
  Check(Refused(()=>gate.Submit(()=>submits++))&&submits==0,"A02/pre-input stop prevents submission");
  gate.Begin();gate.Input(()=>persisted++,()=>{input++;gate.RequestStop();});
  Check(Refused(()=>gate.Submit(()=>submits++))&&submits==0,"A02/stop after input prevents submission");
  gate.Begin();gate.Check();gate.RequestStop();Check(Refused(()=>gate.Submit(()=>submits++))&&submits==0,"A02/stop after final guard prevents submission");
  gate.Begin();using(var claimed=new ManualResetEvent(false))using(var proceed=new ManualResetEvent(false)){
   Exception failure=null;var worker=new Thread(()=>{try{gate.Submit(()=>{claimed.Set();if(!proceed.WaitOne(5000))throw new Exception("dispatch timeout");Interlocked.Increment(ref submits);});}catch(Exception e){failure=e;}});worker.Start();
   Check(claimed.WaitOne(5000),"A02/submission boundary reached");Check(gate.RequestStop(),"A02/stop after submission start reported as too late");proceed.Set();Check(worker.Join(5000)&&failure==null&&submits==1,"A02/already-started submission completes at most once");
  }
  Check(Refused(()=>gate.Submit(()=>submits++))&&submits==1,"A02/no second submission after stop");
  gate.Begin();gate.Submit(()=>submits++);Check(Refused(()=>gate.Submit(()=>submits++))&&submits==2,"A02/normal operation submits exactly once");
  bool invariant=true;
  for(int i=0;i<200;i++){
   var raced=new OperationSafety();bool tooLate=false;int calls=0;using(var start=new ManualResetEvent(false)){
    var cancel=new Thread(()=>{start.WaitOne();tooLate=raced.RequestStop();});
    var submit=new Thread(()=>{start.WaitOne();try{raced.Submit(()=>Interlocked.Increment(ref calls));}catch(SafeFailure){}});
    cancel.Start();submit.Start();start.Set();cancel.Join();submit.Join();invariant&=tooLate?calls==1:calls==0;
   }
  }
  Check(invariant,"A02/200 concurrent stop-submit orderings preserve the boundary");
 }
 static void LaunchRecovery(){
  var state=new WindowLaunchState();var old=new[]{H(1)};state.Begin(old);state.MarkFailed();state.PrepareExplicitRetry(old);
  Check(!state.Pending,"A03/closed failed request permits explicit retry");state.Begin(old);Check(state.Pending,"A03/new explicit retry can start a request");
  state.MarkFailed();state.PrepareExplicitRetry(new[]{H(1),H(2)});Check(state.Pending&&state.Failed,"A03/live pending request does not create duplicate");
  Check(WindowFlow.NewWindow(new[]{H(2)},state.Before)==H(2),"A03/late prepared window still eligible");
  state.Reset();state.Begin(old);state.PrepareExplicitRetry(old);Check(state.Pending&&!state.Failed,"A03/in-flight initial request not restarted");
  state.MarkFailed();state.PrepareExplicitRetry(old);Check(!state.Pending,"A03/next user retry recovers after request closes");
 }
 static void LogsAndSettings(){
  string logs=Path.Combine(root,"logs");Directory.CreateDirectory(logs);string events=Path.Combine(logs,"events.log"),status=Path.Combine(logs,"status.json");File.WriteAllText(events,"");
  using(var locked=new FileStream(events,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!DiagnosticLog.Record(logs,"TEST")&&File.Exists(status),"A04/locked event log does not suppress status write or throw");
  using(var locked=new FileStream(status,FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(!DiagnosticLog.Record(logs,"TEST2"),"A04/locked status does not throw");
  Check(File.ReadAllText(events).Contains("TEST2"),"A04/event log still attempted when status write fails");
  Check(DiagnosticLog.Record(logs,"RECOVERED"),"A04/log writer recovers after lock released");
  using(var locked=new FileStream(events,FileMode.Open,FileAccess.ReadWrite,FileShare.None))AppState.RecordTo(logs,"ERROR_HANDLER_LOG_FAILURE");
  string visible=AppState.WithLogNotice("ORIGINAL_ERROR");Check(visible.Contains("ORIGINAL_ERROR")&&visible.Contains("진단 기록"),"A04/original error and log failure both visible");
  typeof(AppState).GetField("logFailed",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,false);
  string path=Path.Combine(root,"settings.json");var s=new Settings();s.SaveTo(path);int input=0,submits=0;var op=new OperationSafety();
  using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
   Check(Refused(()=>{op.Input(()=>{s.PendingAttempt=true;s.SaveTo(path);},()=>input++);op.Submit(()=>submits++);})&&input==0&&submits==0,"A04/pending-state save failure blocks credential access, input and submit");
  }
  SelectionPreferences.Save(s,false,true,()=>s.SaveTo(path));var loaded=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
  Check(!loaded.OpenNeis&&loaded.OpenKedu,"A05/selection survives save-reload without starting work");
  SelectionPreferences.Save(s,true,false,()=>s.SaveTo(path));loaded=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
  Check(loaded.OpenNeis&&!loaded.OpenKedu,"A05/settings-close save keeps current selection");
  using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
   bool failed=false;try{SelectionPreferences.Save(s,false,true,()=>s.SaveTo(path));}catch(IOException){failed=true;}
   Check(failed&&s.OpenNeis&&!s.OpenKedu,"A05/save failure restores in-memory prior selection");
  }
 }
 static void Hotkeys(){
  int add=0,remove=0;using(var registration=new StopHotkeyRegistration(()=>{add++;return true;},()=>remove++)){Check(registration.Register()&&registration.Register()&&add==1,"A01/normal hotkey registered once");}
  Check(remove==1,"A01/successful hotkey unregistered once");
  using(var registration=new StopHotkeyRegistration(()=>false,()=>remove++))Check(!registration.Register()&&!registration.Active,"A01/registration failure stays inactive");
  Check(remove==1,"A01/failed registration not unregistered");Check(StopHotkeyRegistration.Key!=0x7B,"A01/reserved F12 not used");
  bool first=RegisterHotKey(IntPtr.Zero,0x531,StopHotkeyRegistration.Modifiers,StopHotkeyRegistration.Key);
  if(first){try{
   bool second=RegisterHotKey(IntPtr.Zero,0x532,StopHotkeyRegistration.Modifiers,StopHotkeyRegistration.Key);if(second)UnregisterHotKey(IntPtr.Zero,0x532);Check(!second,"A01/Windows rejects conflicting live registration");
   using(var form=new MainWindow(new Settings(),()=>{})){
    IntPtr handle=form.Handle;
    var note=(System.Windows.Forms.Label)typeof(MainWindow).GetField("hotkeyNote",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
    var stop=(System.Windows.Forms.Button)typeof(MainWindow).GetField("stop",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
    Check(note.Text.Contains("등록하지 못")&&stop.Enabled,"A01/real GUI shows registration failure and retains stop button");
   }
  }finally{UnregisterHotKey(IntPtr.Zero,0x531);}}
  else Console.WriteLine("SKIP live normal hotkey registration: current desktop already reserves combination; failure policy tested");
 }
 static void SelectionEvents(){
  string path=Path.Combine(root,"gui-selection.json");var settings=new Settings();
  using(var form=new MainWindow(settings,()=>settings.SaveTo(path))){
   var neis=(System.Windows.Forms.CheckBox)typeof(MainWindow).GetField("neis",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   var kedu=(System.Windows.Forms.CheckBox)typeof(MainWindow).GetField("kedu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   neis.Checked=false;kedu.Checked=false;
  }
  var reloaded=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
  using(var restarted=new MainWindow(reloaded,()=>reloaded.SaveTo(path))){
   var neis=(System.Windows.Forms.CheckBox)typeof(MainWindow).GetField("neis",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restarted);
   var kedu=(System.Windows.Forms.CheckBox)typeof(MainWindow).GetField("kedu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restarted);
   Check(!neis.Checked&&!kedu.Checked,"A05/real WinForms checkbox events survive close and reconstruction without work");
  }
  using(var failing=new MainWindow(new Settings(),()=>{throw new IOException("injected selection write failure");})){
   var neis=(System.Windows.Forms.CheckBox)typeof(MainWindow).GetField("neis",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(failing);
   var status=(System.Windows.Forms.Label)typeof(MainWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(failing);
   neis.Checked=false;Check(neis.Checked&&status.Text.Contains("저장하지 못해"),"A05/real checkbox event reverts display and reports write failure");
  }
 }
 static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
 static int Child(string name,string source,string target){
  var info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--lease-child "+Quote(name)+" "+Quote(source)+" "+Quote(target)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
  using(var process=Process.Start(info)){if(!process.WaitForExit(10000))throw new Exception("lease child timeout");return process.ExitCode;}
 }
 static void Installation(){
  string source=Path.Combine(root,"source.txt"),target=Path.Combine(root,"installed.txt");File.WriteAllText(source,"NEW");File.WriteAllText(target,"OLD");
  string name="Local\\TeacherWorkStart-Regression-"+Guid.NewGuid().ToString("N");
  using(var lease=new InstanceLease(name)){
   Check(lease.Owned,"B01/first process owns lease");Check(Child(name,source,target)==2&&File.ReadAllText(target)=="OLD","B01/second process refuses before installation copy");
   lease.Release();Check(Child(name,source,target)==0&&File.ReadAllText(target)=="NEW","B01/released parent hands off without blocking child");
  }
  File.WriteAllText(target,"KEEP");bool failed=false;using(var locked=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){try{InstallFiles.CopyAtomic(source,target);}catch(IOException){failed=true;}}
  Check(failed&&File.ReadAllText(target)=="KEEP","B01/locked target preserves existing executable bytes");
  Check(!Directory.GetFiles(root,"*.update-*").Any(),"B01/staged update cleaned after copy failure");
  InstallFiles.CopyAtomic(source,target);Check(File.ReadAllText(target)=="NEW","B01/closed target updates normally");
  string shortcut=Path.Combine(root,"desktop-test.lnk");
  Check(InstallFiles.CanWriteShortcut(shortcut,target),"C02/missing desktop shortcut may be created");
  ShortcutFile.Write(shortcut,source,"","test");Check(!InstallFiles.CanWriteShortcut(shortcut,target),"C02/another target desktop shortcut preserved");
  ShortcutFile.Write(shortcut,target,"--auto","test");Check(!InstallFiles.CanWriteShortcut(shortcut,target),"C02/different shortcut purpose preserved");
  ShortcutFile.Write(shortcut,target,"","test");Check(InstallFiles.CanWriteShortcut(shortcut,target),"C02/own ordinary desktop shortcut remains maintainable");
 }
 static void StrictPending(){
  var session=(StrictSession)FormatterServices.GetUninitializedObject(typeof(StrictSession));var type=typeof(StrictSession);var s=new Settings{PendingAttempt=true};
  type.GetField("settings",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,s);type.GetField("freshDialog",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,true);type.GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(session,true);
  bool blocked=false;try{type.GetMethod("Login",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,null);}catch(TargetInvocationException e){blocked=e.InnerException is SafeFailure&&e.InnerException.Message.Contains("미확인");}
  Check(blocked&&!(bool)type.GetField("attempted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session)&&(int)type.GetField("submissions",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session)==0,"B02/actual StrictSession guard rejects saved pending before any UI or credential operation");
  Check(s.PendingAttempt,"B02/new diagnostic session does not clear pending");s.PendingAttempt=false;OperationSafety.RequireResolved(s.PendingAttempt);Check(!s.PendingAttempt,"B02/explicitly resolved state may proceed to normal checks");
 }
 [STAThread]static int Main(string[] args){
  if(args.Length==1&&args[0]=="--quiet-hidden-guide")return QuietModeTests.HiddenGuideChild();
  if(args.Length==4&&args[0]=="--lease-child"){using(var lease=new InstanceLease(args[1])){if(!lease.Owned)return 2;InstallFiles.CopyAtomic(args[2],args[3]);return 0;}}
  string temp=Path.GetFullPath(Path.GetTempPath());root=Path.GetFullPath(Path.Combine(temp,"TeacherWorkStart-Regression-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
  try{Baseline();Cancellation();LaunchRecovery();LogsAndSettings();SelectionEvents();Hotkeys();Installation();StrictPending();QuietModeTests.Run(Check);Console.WriteLine("PASS "+count+" regression checks; no real credentials, browser login, user settings or Startup writes");return 0;}
  catch(Exception e){Console.WriteLine("FAIL: "+e.GetType().Name+" "+e.Message);return 1;}
  finally{if(root.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("TeacherWorkStart-Regression-"))Directory.Delete(root,true);}
 }
}
