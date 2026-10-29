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

internal static class Portal {
 internal const string Origin="https://gwe.eduptl.kr";
 internal const string LoginPath="/bpm_lgn_lg00_001.do",MainPath="/bpm_man_mn00_001.do";
 static readonly OperationSafety operation=new OperationSafety();
 internal static bool Cancelled {get{return operation.Cancelled;}}
 internal static void BeginOperation(){operation.Begin();}
 internal static bool RequestStop(){return operation.RequestStop();}
 internal static void Input(Action persistPending,Action input){operation.Input(persistPending,input);}
 internal static void SubmitOnce(Action dispatch){operation.Submit(dispatch);}
 internal static Action<string> Report=s=>{};
 internal static Action<string> Milestone=s=>{};
 internal static IntPtr ReturnWindowHint,HelperWindow;
 static IntPtr workWindow;static uint workProcess;static string workProfile,workPortalTabKey;
 static readonly WindowLaunchState launch=new WindowLaunchState();
 static ForegroundReturn focusReturn;
 internal static void CheckCancel(){operation.Check();}
 internal static List<AutomationElement> All(AutomationElement e,ControlType t){return e.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,t)).Cast<AutomationElement>().ToList();}
 internal static AutomationElement Id(AutomationElement e,string id){return e.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));}
 internal static List<Tuple<IntPtr,AutomationElement>> Windows(){
  var result=new List<Tuple<IntPtr,AutomationElement>>();
  Native.EnumWindows((h,p)=>{uint pid;Native.GetWindowThreadProcessId(h,out pid);try{if(Native.IsWindowVisible(h)&&Process.GetProcessById((int)pid).ProcessName=="msedge")result.Add(Tuple.Create(h,AutomationElement.FromHandle(h)));}catch(ArgumentException){}catch(ElementNotAvailableException){}return true;},IntPtr.Zero);return result;
 }
 static bool IsPortalTab(AutomationElement t){return PortalTabPolicy.IsPortalName(t.Current.Name);}
 internal static string DocUrl(AutomationElement e){object p;if(!e.Current.IsPassword&&e.TryGetCurrentPattern(ValuePattern.Pattern,out p))return ((ValuePattern)p).Current.Value;return "";}
 internal static void Activate(IntPtr hwnd,AutomationElement tab,bool foreground){
  if(foreground){if(Native.IsIconic(hwnd))Native.ShowWindow(hwnd,9);if(Native.GetForegroundWindow()!=hwnd)Native.SetForegroundWindow(hwnd);if(Native.GetForegroundWindow()!=hwnd)throw new SafeFailure("업무 창을 앞으로 가져오지 못했어요. 직접 창을 선택한 뒤 다시 시작해 주세요.");}
  object p;
  if(!tab.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p))throw new SafeFailure("Edge 탭을 선택할 수 없습니다.");
  var selection=(SelectionItemPattern)p;if(!selection.Current.IsSelected){selection.Select();Thread.Sleep(150);}
 }
 internal static void Launch(string profile){
  string arguments=WindowFlow.LaunchArguments(profile,Origin+LoginPath);
  string[] paths={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Microsoft","Edge","Application","msedge.exe")};
  string exe=paths.FirstOrDefault(File.Exists);if(exe==null)throw new SafeFailure("Microsoft Edge 설치 경로를 찾지 못했습니다.");
  Process.Start(new ProcessStartInfo(exe,arguments){UseShellExecute=true});
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
    },30000,"새 업무 창이 준비되지 않았어요. 요청 창이 남아 있으면 닫힌 뒤 업무 시작을 다시 눌러 주세요. 자동으로 창을 반복 생성하지 않습니다.");
    workWindow=found.Item1;Native.GetWindowThreadProcessId(workWindow,out workProcess);launch.Reset();
   }catch{launch.MarkFailed();throw;}
  }
  if(focusReturn!=null){focusReturn.Own(workWindow);focusReturn.TryReturn();}
 }
 static PortalContext EnsurePortal(string profile,bool foreground,bool allowCreate=true){
  EnsureWindow(profile,allowCreate);var window=BoundWindow();
  if(window==null)throw new SafeFailure("업무 전용 창이 닫혔어요. 다른 창으로 대상을 바꾸지 않습니다.");
  var tab=Wait(()=>{
   CheckCancel();if(BoundWindow()==null)throw new SafeFailure("업무 창이 닫혔거나 바뀌었습니다. 다른 창으로 대신 진행하지 않습니다.");
   var tabs=All(window.Item2,ControlType.TabItem).Select(t=>new {Element=t,Key=String.Join("-",t.GetRuntimeId()),Name=t.Current.Name}).ToList();
   string key=PortalTabPolicy.Resolve(tabs.Select(t=>new PortalTabFact{Key=t.Key,Name=t.Name}),workPortalTabKey);
   return key==null?null:tabs.Single(t=>t.Key==key).Element;
  },15000,"업무포털 탭 표시가 아직 안정화되지 않았습니다. 로그인은 다시 제출하지 않고 중단합니다.");
  workPortalTabKey=String.Join("-",tab.GetRuntimeId());
  Activate(window.Item1,tab,foreground);
  if(!foreground&&focusReturn!=null)focusReturn.TryReturn();
  return Wait(()=>Locate(profile),30000,"업무포털 화면을 확인하지 못했어요. 창을 최소화하거나 탭을 바꿨는지 확인해 주세요.");
 }
 // Polling observes only. It never launches, selects a tab, or changes focus.
 internal static PortalContext Locate(string profile){
  CheckCancel();if(workProfile!=profile)return null;var window=BoundWindow();if(window==null)return null;
  foreach(var d in All(window.Item2,ControlType.Document)){
   Uri u;if(!Uri.TryCreate(DocUrl(d),UriKind.Absolute,out u))continue;
   if(u.Scheme=="https"&&u.Host=="gwe.eduptl.kr"&&u.Port==443&&(u.AbsolutePath==LoginPath||u.AbsolutePath==MainPath)&&!d.Current.IsOffscreen)
    return new PortalContext{Hwnd=window.Item1,Window=window.Item2,Doc=d,Path=u.AbsolutePath};
  }
  return null;
 }
 internal static T Wait<T>(Func<T> f,int milliseconds,string error)where T:class {
  var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<milliseconds){CheckCancel();try{T x=f();if(x!=null)return x;}catch(ElementNotAvailableException){}catch(System.Runtime.InteropServices.COMException){}Thread.Sleep(250);}throw new SafeFailure(error);
 }
 internal static void Validate(PortalContext c,string path,bool requireForeground){
  CheckCancel();Uri u;if(!Uri.TryCreate(DocUrl(c.Doc),UriKind.Absolute,out u)||u.Scheme!="https"||u.Host!="gwe.eduptl.kr"||u.Port!=443||u.AbsolutePath!=path)throw new SafeFailure("현재 문서 URL이 등록 대상과 다릅니다.");
  var bars=All(c.Window,ControlType.Edit).Where(e=>!e.Current.IsPassword&&e.Current.Name=="주소 표시줄 및 검색 창").ToList();
  if(bars.Count!=1)throw new SafeFailure("현재 Edge 주소 표시줄을 하나로 확인할 수 없습니다.");
  string address=DocUrl(bars[0]);if(!address.Contains("://"))address="https://"+address;
  Uri bar;if(!Uri.TryCreate(address,UriKind.Absolute,out bar)||bar.Scheme!="https"||bar.Host!="gwe.eduptl.kr"||bar.Port!=443||bar.AbsolutePath!=path)throw new SafeFailure("활성 탭 주소가 업무포털 대상과 일치하지 않습니다.");
  if(requireForeground&&Native.GetForegroundWindow()!=c.Hwnd)throw new SafeFailure("입력 직전 대상 창이 바뀌었습니다. 입력하지 않고 중단합니다.");
 }
 internal static AutomationElement Dialog(PortalContext c){
  var explicitDialog=Id(c.Doc,"kcasedialog");if(explicitDialog!=null)return explicitDialog;
  var list=Id(c.Doc,"kc_cert_list");if(list==null||list.Current.IsOffscreen)return null;
  // Chromium omits the generic DIV id in UIA. The observed parent is the web modal Window.
  var parent=TreeWalker.ControlViewWalker.GetParent(list);
  return parent!=null&&parent.Current.ControlType==ControlType.Window?parent:null;
 }
 // Setup only opens or selects the portal. The tutorial lets the user open the certificate window.
 internal static PortalContext OpenForSetup(string profile){
  Report("업무포털을 열고 있어요. 잠시만 기다려 주세요.");
  var c=EnsurePortal(profile,true);
  Validate(c,c.Path,false);return c;
 }
 internal static PortalContext Prepare(string profile,bool allowCreate=true){
  Report("인증서창을 준비하고 있어요.");var c=EnsurePortal(profile,false,allowCreate);
  Validate(c,c.Path,false);if(c.Path==MainPath)throw new SafeFailure("현재 로그인되어 있습니다. 실제 암호 시험·등록 전 업무포털에서 직접 로그아웃해 주세요.");
  var dialog=Dialog(c);
  if(dialog==null||dialog.Current.IsOffscreen){var login=Id(c.Doc,"btnLgn");if(login==null)throw new SafeFailure("인증서 로그인 버튼을 찾지 못했습니다.");Native.Click(login,c.Hwnd);}
  if(focusReturn!=null)focusReturn.TryReturn();
  return Wait(()=>{var x=Locate(profile);if(x==null||x.Path!=LoginPath)return null;Validate(x,LoginPath,false);var d=Dialog(x);if(d==null||d.Current.IsOffscreen)return null;var list=Id(d,"kc_cert_list");return list!=null&&ReadCertificatesFromList(list).Count>0?x:null;},15000,"인증서 목록을 아직 불러오지 못했습니다. 인증서 위치와 목록 표시를 확인해 주세요. 암호는 입력하지 않았습니다.");
 }
 internal static List<CertificateInfo> Certificates(PortalContext c){
  Validate(c,LoginPath,false);var list=Id(Dialog(c),"kc_cert_list");if(list==null)throw new SafeFailure("인증서 목록을 찾지 못했습니다.");
  var result=ReadCertificatesFromList(list);
  if(result.Count==0)throw new SafeFailure("인증서 목록의 실제 행이 아직 준비되지 않았습니다. 암호를 입력하지 않습니다.");
  return result;
 }
 static List<CertificateInfo> ReadCertificatesFromList(AutomationElement list){
  var result=new List<CertificateInfo>();
  foreach(var row in All(list,ControlType.DataItem)){
   if(row.Current.IsOffscreen)continue;
   var cells=All(row,ControlType.DataItem);if(cells.Count<4)continue;
   var names=cells.Select(x=>(x.Current.Name??"").Trim()).ToArray();
   DateTime expiry;string date=names.FirstOrDefault(x=>System.Text.RegularExpressions.Regex.IsMatch(x,@"^\d{4}-\d{2}-\d{2}$"));
   if(date==null||!DateTime.TryParse(date,out expiry))continue;
   int index=Array.IndexOf(names,date);if(index<1||index+1>=names.Length)continue;
   result.Add(new CertificateInfo{User=names[index-1],Expiry=date,Issuer=names[index+1]});
  }
  return result.GroupBy(x=>x.ToString()).Select(g=>g.First()).ToList();
 }
 static List<AutomationElement> MatchingCertificateRows(AutomationElement list,Settings s){
  return All(list,ControlType.DataItem).Where(r=>{var n=All(r,ControlType.DataItem).Select(x=>(x.Current.Name??"").Trim()).ToList();return n.Contains(s.CertUser)&&n.Contains(s.CertExpiry)&&n.Contains(s.CertIssuer);}).ToList();
 }
 static PortalContext WaitRegisteredCertificate(Settings s,PortalContext initial){
  Report("인증서 목록을 불러오는 중 · 등록한 인증서 확인 대기");
  return Wait(()=>{
   var c=Locate(s.Profile);if(c==null||c.Hwnd!=initial.Hwnd)throw new SafeFailure("확인 중인 업무 창이 바뀌었습니다. 다른 창에 입력하지 않습니다.");
   Validate(c,LoginPath,false);var dialog=Dialog(c);if(dialog==null||dialog.Current.IsOffscreen)return null;var list=Id(dialog,"kc_cert_list");if(list==null)return null;
   var matches=MatchingCertificateRows(list,s);
   if(matches.Count>1)throw new SafeFailure("등록 정보가 같은 인증서가 여러 개 표시됩니다. 임의 선택하지 않고 중단합니다.");
   return matches.Count==1?c:null;
  },15000,"목록에서 등록한 이름·만료일·발급자가 일치하는 인증서를 확인하지 못했습니다. 인증서 위치와 표시된 목록을 확인해 주세요. 암호는 입력하지 않았습니다.");
 }
 internal static AutomationElement CertificateRow(PortalContext c,Settings s){
  var list=Id(Dialog(c),"kc_cert_list");if(list==null)throw new SafeFailure("인증서 목록이 없어졌습니다.");
  var matches=MatchingCertificateRows(list,s);
  if(matches.Count==0)throw new SafeFailure(ReadCertificatesFromList(list).Count==0?"인증서 목록 행이 아직 준비되지 않았습니다. 암호를 입력하지 않습니다.":"현재 목록에 등록 정보가 일치하는 인증서가 보이지 않습니다. 인증서 위치를 확인해 주세요.");
  if(matches.Count>1)throw new SafeFailure("등록 정보가 같은 인증서가 여러 개 표시됩니다. 임의 선택하지 않습니다.");return matches[0];
 }
 internal static void ConfirmSelectedCertificate(PortalContext c,Settings s){
  var row=CertificateRow(c,s);
  if(!(row.Current.Name??"").StartsWith("선택됨"))throw new SafeFailure("등록한 인증서가 선택된 상태인지 확인하지 못했습니다. 암호 입력·제출을 중단합니다.");
 }
 internal static AutomationElement Password(PortalContext c){var d=Dialog(c);if(d==null)throw new SafeFailure("인증서창이 없어졌습니다.");var fields=All(d,ControlType.Edit).Where(e=>e.Current.IsPassword&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();if(fields.Count!=1)throw new SafeFailure("암호 입력칸을 하나로 식별하지 못했습니다. 입력 차단으로 단정하지 않고 중단합니다.");return fields[0];}
 internal static void BindProbeWindow(Settings s,long hwnd){
  IntPtr handle=new IntPtr(hwnd);var target=Windows().SingleOrDefault(w=>w.Item1==handle);
  if(target==null)throw new SafeFailure("검사에서 지정한 Edge 창이 없어졌어요. 다른 창으로 바꾸지 않습니다.");
  workWindow=handle;Native.GetWindowThreadProcessId(handle,out workProcess);workProfile=s.Profile;workPortalTabKey=null;launch.Reset();
  var c=Locate(s.Profile);if(c==null)throw new SafeFailure("검사 대상 창의 포털 HTTPS 문서를 확인하지 못했어요.");Validate(c,c.Path,false);
 }
 internal static object ProbeBackground(Settings s){
  IntPtr original=Native.GetForegroundWindow();
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),h=>Native.SetForegroundWindow(h)&&Native.GetForegroundWindow()==h);
  try{
   var c=EnsurePortal(s.Profile,false);Validate(c,c.Path,false);
   if(c.Path==MainPath)return new {alreadyLoggedIn=true,passwordRead=false,secretWritten=false,submitted=false};
   var login=Id(c.Doc,"btnLgn");
   if(Dialog(c)==null){object invoke;if(login==null||!login.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new SafeFailure("로그인 버튼의 직접 실행 기능이 없어 백그라운드 검사를 중단했어요. 좌표 클릭으로 바꾸지 않았습니다.");((InvokePattern)invoke).Invoke();focusReturn.TryReturn();}
   c=Wait(()=>{var x=Locate(s.Profile);if(x==null||x.Path!=LoginPath||Dialog(x)==null)return null;try{return Certificates(x).Count>0?x:null;}catch(SafeFailure){return null;}},15000,"인증서 목록 행의 준비 상태를 확인하지 못했어요. 반복 클릭하지 않았습니다.");
   Validate(c,LoginPath,false);var password=Password(c);object value;
   bool supported=password.TryGetCurrentPattern(ValuePattern.Pattern,out value);
   bool readOnly=supported&&((ValuePattern)value).Current.IsReadOnly;
   bool certificateMatched=false,certificateSelected=false,rowSelectable=false;
   if(s.CertUser!=""){try{var row=CertificateRow(c,s);certificateMatched=true;certificateSelected=(row.Current.Name??"").StartsWith("선택됨");object selection;rowSelectable=row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection);}catch(SafeFailure){}}
   var visibleCertificates=new List<CertificateInfo>();try{visibleCertificates=Certificates(c);}catch(SafeFailure){}
   var certificateList=Id(Dialog(c),"kc_cert_list");
   var certificateNodes=certificateList==null?new object[0]:certificateList.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Take(48).Where(e=>!e.Current.IsPassword).Select(e=>(object)new {type=e.Current.ControlType.ProgrammaticName,name=e.Current.Name}).ToArray();
   var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();object confirmation;
   bool directSubmit=buttons.Count==1&&buttons[0].TryGetCurrentPattern(InvokePattern.Pattern,out confirmation);
   focusReturn.TryReturn();
   return new {alreadyLoggedIn=false,hwnd=c.Hwnd.ToInt64(),processId=workProcess,background=Native.GetForegroundWindow()!=c.Hwnd,passwordProtected=password.Current.IsPassword,valuePatternSupported=supported,readOnly=readOnly,certificateMatched=certificateMatched,certificateSelected=certificateSelected,rowSelectionSupported=rowSelectable,directSubmitSupported=directSubmit,certificates=visibleCertificates,certificateNodes=certificateNodes,passwordRead=false,secretWritten=false,submitted=false};
  }finally{focusReturn=null;}
 }
 internal static object BackgroundOnce(Settings s,long hwnd){
  BindProbeWindow(s,hwnd);var c=Locate(s.Profile);Validate(c,LoginPath,false);
  if(s.PendingAttempt)throw new SafeFailure("이전 입력 결과가 미확인이라 백그라운드 시험을 시작하지 않았어요.");
  if(!Vault.Exists(s.Target))throw new SafeFailure("현재 Windows 사용자에게 등록된 암호가 없어요. 시험하지 않습니다.");
  DateTime expiry;if(!DateTime.TryParse(s.CertExpiry,out expiry)||expiry.Date<DateTime.Today)throw new SafeFailure("등록 인증서 유효기간을 확인하지 못했어요.");
  ConfirmSelectedCertificate(c,s);var password=Password(c);object pattern;
  if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)||((ValuePattern)pattern).Current.IsReadOnly)throw new SafeFailure("암호칸의 직접 입력 기능이 없어요. 전면 키 입력으로 바꾸지 않습니다.");
  var value=(ValuePattern)pattern;
  var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();object submitPattern;
  if(buttons.Count!=1||!buttons[0].TryGetCurrentPattern(InvokePattern.Pattern,out submitPattern))throw new SafeFailure("직접 실행할 확인 버튼이 없어 제출하지 않았어요.");
  Action guard=()=>{
   Validate(c,LoginPath,false);ConfirmSelectedCertificate(c,s);
   if(Native.GetForegroundWindow()==c.Hwnd)throw new SafeFailure("업무 창이 전면으로 바뀌어 백그라운드 시험을 중단했어요. 반복 입력하지 않습니다.");
   if(!Automation.Compare(password,Password(c))||value.Current.IsReadOnly)throw new SafeFailure("보호된 입력 요소가 바뀌어 중단했어요.");
  };
  guard();Input(()=>{s.PendingAttempt=true;s.Save();},()=>{AppState.Record("BACKGROUND_INPUT_STARTED");Vault.SetDirect(s.Target,secret=>value.SetValue(secret),guard);guard();});
  AppState.Record("BACKGROUND_INPUT_SET");Report("백그라운드 직접 입력 완료. 전면화·키보드 입력 없음.");
  // No retries, no input-method fallback, and no read-back of the password value.
  SubmitOnce(()=>((InvokePattern)submitPattern).Invoke());AppState.Record("BACKGROUND_SUBMITTED_ONCE");Report("확인 버튼 직접 실행 1회. 응답 확인 중.");
  bool backgroundAfterSubmit=Native.GetForegroundWindow()!=c.Hwnd;
  c=Wait(()=>{var x=Locate(s.Profile);return x!=null&&x.Path==MainPath&&Id(x.Doc,"btn-logout")!=null?x:null;},45000,"직접 입력 후 로그인 성공을 확인하지 못했어요. 재입력·재제출하지 않았습니다.");
  Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();AppState.Record("BACKGROUND_LOGIN_SUCCESS");
  return new {loginSuccess=true,backgroundInputVerified=true,backgroundAfterSubmit=backgroundAfterSubmit,backgroundAtCompletion=Native.GetForegroundWindow()!=c.Hwnd,keyboardInputUsed=false,focusRequested=false,passwordReadBack=false,submittedCount=1,startupRegisteredByTest=false};
 }
 static bool RestoreWorkWindow(IntPtr hwnd){
  Native.SetForegroundWindow(hwnd);if(Native.GetForegroundWindow()==hwnd)return true;
  try{AutomationElement.FromHandle(hwnd).SetFocus();}catch(InvalidOperationException){}catch(ElementNotAvailableException){}
  return Native.GetForegroundWindow()==hwnd;
 }
 internal static bool Run(Settings s,bool test,bool direct=true){
  IntPtr original=Native.GetForegroundWindow();if(original==HelperWindow)original=ReturnWindowHint;
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),RestoreWorkWindow);
  focusReturn.Own(HelperWindow);
  try{return RunCore(s,test,direct);}finally{try{if(focusReturn!=null)focusReturn.TryReturn();}finally{focusReturn=null;}}
 }
 internal static void OpenExistingSystems(Settings s,long hwnd){
  BindProbeWindow(s,hwnd);var c=Locate(s.Profile);Validate(c,MainPath,false);
  IntPtr original=Native.GetForegroundWindow();
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),RestoreWorkWindow);
  focusReturn.Own(c.Hwnd);
  try{
   if(s.OpenNeis)OpenSystem(s,"나이스","gwe.neis.go.kr","/jsp/main.jsp");
   if(s.OpenKedu)OpenSystem(s,"K-에듀파인","klef.gwe.go.kr","/keris_ui/main.do");
  }finally{try{if(focusReturn!=null)focusReturn.TryReturn();}finally{focusReturn=null;}}
 }
 static void AuthenticateDirect(Settings s,PortalContext c){
  Validate(c,LoginPath,false);var row=CertificateRow(c,s);
  var password=Password(c);object pattern;
  if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)||((ValuePattern)pattern).Current.IsReadOnly)throw new SafeFailure("암호칸 직접 입력을 사용할 수 없습니다. 다른 방식으로 자동 재시도하지 않습니다.");
  var value=(ValuePattern)pattern;
  var buttons=All(Dialog(c),ControlType.Button).Where(b=>b.Current.Name=="확인"&&!b.Current.IsOffscreen&&b.Current.IsEnabled).ToList();object confirmation;
  if(buttons.Count!=1||!buttons[0].TryGetCurrentPattern(InvokePattern.Pattern,out confirmation))throw new SafeFailure("확인 버튼의 직접 실행 기능을 확인하지 못했습니다. 암호를 입력하지 않습니다.");
  Report("암호 입력·확인 제출 동안 업무포털이 잠시 전면에 나타납니다.");
  Native.SetForegroundWindow(c.Hwnd); // Best effort only; input remains bound to the validated element.
  if(!(row.Current.Name??"").StartsWith("선택됨")){
   object selection;if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))((SelectionItemPattern)selection).Select();
   else if(row.TryGetCurrentPattern(InvokePattern.Pattern,out selection))((InvokePattern)selection).Invoke();
   else throw new SafeFailure("등록 인증서 직접 선택이 지원되지 않습니다. 암호를 입력하지 않습니다.");
  }
  Action guard=()=>{CheckCancel();Validate(c,LoginPath,false);ConfirmSelectedCertificate(c,s);if(!Automation.Compare(password,Password(c))||value.Current.IsReadOnly||!password.Current.IsEnabled)throw new SafeFailure("암호 입력 대상 또는 인증서가 바뀌었습니다. 재입력하지 않습니다.");};
  try{
   guard();Input(()=>{s.PendingAttempt=true;s.Save();},()=>{AppState.Record("DIRECT_FOREGROUND_INPUT_STARTED");Vault.SetDirect(s.Target,secret=>value.SetValue(secret),guard);guard();});
   SubmitOnce(()=>{AppState.Record("DIRECT_FOREGROUND_SUBMIT_ONCE");((InvokePattern)confirmation).Invoke();});
  }finally{if(focusReturn!=null)focusReturn.TryReturn();}
  Report("로그인 응답 확인 중 · 자동으로 다시 제출하지 않습니다.");
  c=Wait(()=>{var x=Locate(s.Profile);return x!=null&&x.Path==MainPath&&Id(x.Doc,"btn-logout")!=null?x:null;},45000,"로그인 성공을 확인하지 못했습니다. 재입력·재제출하지 않습니다. 오류 상태를 확인해 주세요.");
  Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();AppState.Record("DIRECT_FOREGROUND_LOGIN_SUCCESS");Report("업무포털 로그인 성공 · 입력 구간 종료");
 }
 static bool RunCore(Settings s,bool test,bool direct){
  var c=EnsurePortal(s.Profile,false);Validate(c,c.Path,false);
  Milestone("EDGE_READY");
  if(c.Path==MainPath){if(test)throw new SafeFailure("이미 로그인되어 있어 암호 자동 입력을 검증할 수 없습니다. 직접 로그아웃한 뒤 시험해 주세요.");Report("기존 로그인 세션 확인");}
  else {
   if(s.PendingAttempt)throw new SafeFailure("이전 입력 결과가 확인되지 않았습니다. 자동 재시도하지 않습니다. 화면을 확인한 뒤 앱에서 오류 상태를 초기화해 주세요.");
   if(!Vault.Exists(s.Target))throw new SafeFailure("인증서 암호를 먼저 등록해 주세요.");
   DateTime expiry;if(!DateTime.TryParse(s.CertExpiry,out expiry)||expiry.Date<DateTime.Today)throw new SafeFailure("등록한 인증서 유효기간을 확인해 주세요.");
   c=Prepare(s.Profile,false);
   c=WaitRegisteredCertificate(s,c);
   Milestone("CERTIFICATE_READY");
   if(direct)AuthenticateDirect(s,c);
   else {
   Report("암호 입력·제출 동안 업무 창을 잠시 앞으로 가져옵니다.");c=EnsurePortal(s.Profile,true,false);Validate(c,LoginPath,true);
   var row=CertificateRow(c,s);Native.Click(row,c.Hwnd);Thread.Sleep(250);
   c=Wait(()=>Locate(s.Profile),5000,"업무포털 대상이 바뀌었습니다.");Validate(c,LoginPath,true);ConfirmSelectedCertificate(c,s);
   var password=Password(c);password.SetFocus();Thread.Sleep(150);
   Action guard=()=>{CheckCancel();if(Native.GetForegroundWindow()!=c.Hwnd)throw new SafeFailure("암호 입력 중 활성 창이 바뀌었습니다. 즉시 중단했습니다.");var focused=AutomationElement.FocusedElement;if(!Automation.Compare(focused,password)||!focused.Current.IsPassword)throw new SafeFailure("암호 입력 대상의 포커스가 바뀌었습니다. 즉시 중단했습니다.");};
   Validate(c,LoginPath,true);guard();Input(()=>{s.PendingAttempt=true;s.Save();},()=>{Report("대상·인증서 확인 완료. 저장 암호를 로컬에서 입력 중");Vault.TypeInto(s.Target,guard);guard();});Validate(c,LoginPath,true);ConfirmSelectedCertificate(c,s);
   var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();
   if(buttons.Count!=1)throw new SafeFailure("확인 버튼을 하나로 찾지 못했습니다. 제출하지 않습니다.");
   SubmitOnce(()=>{Report("로그인 1회 제출. 응답 확인 중");Native.Click(buttons[0],c.Hwnd);});
   if(focusReturn!=null)focusReturn.TryReturn();
   c=Wait(()=>{var x=Locate(s.Profile);return x!=null&&x.Path==MainPath&&Id(x.Doc,"btn-logout")!=null?x:null;},45000,"로그인 성공을 확인하지 못했습니다. 입력 불허·암호 오류·응답 지연 등은 화면에서 구분해 주세요. 재제출하지 않았습니다.");
   Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();Report("암호 자동 입력 및 업무포털 로그인 성공");
   }
  }
  Milestone("LOGIN_COMPLETE");
  if(s.OpenNeis)OpenSystem(s,"나이스","gwe.neis.go.kr","/jsp/main.jsp");
  if(s.OpenKedu)OpenSystem(s,"K-에듀파인","klef.gwe.go.kr","/keris_ui/main.do");
  Report("완료: 업무포털과 선택한 시스템 초기 화면 확인");return true;
 }
 static void OpenSystem(Settings s,string name,string host,string path){
  var c=EnsurePortal(s.Profile,false,false);Validate(c,MainPath,false);
  string prefix=name=="나이스"?"4세대 나이스 시스템":"K-에듀파인";
  var previous=new HashSet<string>();
  var previousWindows=new HashSet<IntPtr>();
  foreach(var window in Windows()){previousWindows.Add(window.Item1);foreach(var tab in All(window.Item2,ControlType.TabItem))if(tab.Current.Name.StartsWith(prefix))previous.Add(String.Join("-",tab.GetRuntimeId()));}
  var links=All(c.Doc,ControlType.Hyperlink).Where(e=>e.Current.Name==name&&!e.Current.IsOffscreen).Where(e=>{Uri u;return Uri.TryCreate(e.Current.AutomationId,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host==host;}).ToList();
  if(links.Count!=1)throw new SafeFailure(name+" 공식 메뉴를 하나로 찾지 못했습니다.");
  Report(name+" 공식 SSO 메뉴 열기");Native.Click(links[0],c.Hwnd);
  if(focusReturn!=null)focusReturn.TryReturn();
  var target=Wait(()=>{
   var candidates=new List<BrowserTab>();
   foreach(var w in Windows()){
    if(w.Item1!=workWindow)continue; // Keep all system tabs inside the new work window.
    foreach(var tab in All(w.Item2,ControlType.TabItem))if(tab.Current.Name.StartsWith(prefix)&&!previous.Contains(String.Join("-",tab.GetRuntimeId())))candidates.Add(new BrowserTab{Hwnd=w.Item1,Window=w.Item2,Tab=tab});
   }
   if(candidates.Count>1)throw new SafeFailure(name+"의 새 탭이 여러 개라 대상을 정하지 못했어요. 메뉴를 반복 클릭하지 않습니다.");
   return candidates.Count==1?candidates[0]:null;
  },45000,name+"의 새 탭을 업무 전용 창 안에서 확인하지 못했어요. 기존 작업 창의 탭으로 대신 진행하지 않습니다.");
  if(focusReturn!=null)focusReturn.Own(target.Hwnd);
  // Select once outside the observation loop; do not call SetForegroundWindow.
  Activate(target.Hwnd,target.Tab,false);if(focusReturn!=null)focusReturn.TryReturn();
  uint targetProcess;Native.GetWindowThreadProcessId(target.Hwnd,out targetProcess);
  Wait(()=>{
   uint currentProcess;Native.GetWindowThreadProcessId(target.Hwnd,out currentProcess);if(!Native.IsWindow(target.Hwnd)||currentProcess!=targetProcess)throw new SafeFailure(name+" 확인 창이 닫혔어요. 다른 창으로 대체하지 않습니다.");
   foreach(var d in All(target.Window,ControlType.Document)){Uri u;if(!Uri.TryCreate(DocUrl(d),UriKind.Absolute,out u)||u.Scheme!="https"||u.Port!=443||u.Host!=host||u.AbsolutePath!=path||d.Current.IsOffscreen)continue;
     bool authenticated=All(d,ControlType.Button).Any(b=>b.Current.Name=="로그아웃"||(b.Current.AutomationId??"").EndsWith(".btnLogOut"));if(authenticated)return d;
   }return null;
  },45000,name+" 초기 화면과 로그인 상태를 확인하지 못했습니다. 메뉴를 반복 클릭하지 않습니다.");
  Report(name+" 초기 화면 확인 완료");
 }
}
