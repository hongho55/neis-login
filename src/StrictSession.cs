using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Web.Script.Serialization;

// This path deliberately cannot call the legacy activation/input/fallback path.
// The command channel accepts fixed operation names only; never passwords.
internal sealed class StrictSession:IDisposable {
 readonly Settings settings;readonly IntPtr hwnd;readonly string reportPath;readonly FocusAudit audit;
 PortalContext context;bool freshDialog,selected,attempted,login,neis,kedu,blocked;int submissions;
 string step="BASELINE",state="running",selectionMethod="not-tested";
 readonly List<object> steps=new List<object>();
 StrictSession(Settings s,long window,string report){
  settings=s;hwnd=new IntPtr(window);reportPath=Path.GetFullPath(report);
  uint pid;Native.GetWindowThreadProcessId(hwnd,out pid);
  if(Process.GetProcessById((int)pid).SessionId!=Process.GetCurrentProcess().SessionId)throw new SafeFailure("다른 Windows 세션의 창은 시험하지 않습니다.");
  Portal.BindProbeWindow(settings,window);context=Portal.Locate(settings.Profile);Portal.Validate(context,Portal.LoginPath,false);
  audit=new FocusAudit();audit.AddTarget(hwnd);audit.AssertBackground();Save();
 }
 void Save(){var data=new {time=DateTimeOffset.Now.ToString("o"),state=state,step=step,loginSuccess=login,neisSuccess=neis,keduSuccess=kedu,submissions=submissions,selectionMethod=selectionMethod,secretAttemptStarted=attempted,foregroundRequests=0,keyboardOrCoordinateInput=0,focusRestorationUsed=false,startupModified=false,sameWindowsSession=true,steps=steps.ToArray(),focus=audit.Snapshot()};Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,new JavaScriptSerializer().Serialize(data),new System.Text.UTF8Encoding(false));}
 void Stage(string name){step=name;Save();audit.Stage(name);Save();}
 void Done(string method){audit.AssertBackground();steps.Add(new {step=step,method=method,result="completed"});Save();}
 PortalContext Current(){var c=Portal.Locate(settings.Profile);if(c==null||c.Hwnd!=hwnd)throw new SafeFailure("지정한 업무포털 문서가 없어졌어요. 다른 창으로 바꾸지 않습니다.");Portal.Validate(c,c.Path,false);return c;}
 static void Invoke(AutomationElement e){object p;if(e==null||!e.TryGetCurrentPattern(InvokePattern.Pattern,out p))throw new SafeFailure("대상 요소에 InvokePattern이 없습니다. 전면 입력으로 대체하지 않습니다.");((InvokePattern)p).Invoke();}
 AutomationElement Button(PortalContext c,string name){var buttons=Portal.All(Portal.Dialog(c),ControlType.Button).Where(e=>e.Current.Name==name&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();if(buttons.Count!=1)throw new SafeFailure("해당 버튼을 하나로 확인하지 못했어요.");return buttons[0];}
 void Prepare(){
  if(freshDialog||attempted)throw new SafeFailure("같은 시험에서 인증서창을 반복 생성하지 않습니다.");
  context=Current();Portal.Validate(context,Portal.LoginPath,false);
  var existing=Portal.Dialog(context);
  if(existing!=null&&!existing.Current.IsOffscreen){Stage("CLOSE_EXISTING_CERTIFICATE");Invoke(Button(context,"취소"));Portal.Wait(()=>{var c=Current();return Portal.Dialog(c)==null?c:null;},10000,"기존 인증서창이 닫힌 것을 확인하지 못했어요.");Done("InvokePattern.Cancel");}
  Stage("CREATE_CERTIFICATE_DIALOG");Invoke(Portal.Id(context.Doc,"btnLgn"));
  context=Portal.Wait(()=>{var c=Current();if(Portal.Dialog(c)==null)return null;try{return Portal.Certificates(c).Count>0?c:null;}catch(SafeFailure){return null;}},20000,"인증서 목록이 준비되지 않았어요.");
  freshDialog=true;Done("InvokePattern.Login");
 }
 object Inspect(){
  context=Current();var row=Portal.CertificateRow(context,settings);var password=Portal.Password(context);object p;
  bool select=row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p),invoke=row.TryGetCurrentPattern(InvokePattern.Pattern,out p),value=password.TryGetCurrentPattern(ValuePattern.Pattern,out p);
  return new {certificateSelected=(row.Current.Name??"").StartsWith("선택됨"),selectionItemPattern=select,invokePattern=invoke,valuePattern=value,valueReadOnly=value&&((ValuePattern)p).Current.IsReadOnly,rowPatterns=row.GetSupportedPatterns().Select(x=>x.ProgrammaticName).ToArray(),passwordRead=false};
 }
 void BeginDomOpen(){
  if(freshDialog||attempted)throw new SafeFailure("같은 시험에서 인증서창을 반복 생성하지 않습니다.");
  context=Current();Portal.Validate(context,Portal.LoginPath,false);var dialog=Portal.Dialog(context);
  if(dialog!=null&&!dialog.Current.IsOffscreen)throw new SafeFailure("기존 인증서창이 열려 있어 새 생성 시험을 시작하지 않았어요.");
  Stage("CREATE_CERTIFICATE_DOM");
 }
 void VerifyDomOpen(){
  if(step!="CREATE_CERTIFICATE_DOM")throw new SafeFailure("DOM 생성 대기 단계가 아닙니다.");
  context=Portal.Wait(()=>{audit.AssertBackground();var c=Current();if(Portal.Dialog(c)==null)return null;try{return Portal.Certificates(c).Count>0?c:null;}catch(SafeFailure){return null;}},20000,"DOM 조작 후 인증서 목록을 확인하지 못했어요.");
  freshDialog=true;Done("DOM semantic login control + UIA dialog verification");
 }
 object Select(){
  Stage("SELECT_CERTIFICATE");context=Current();var row=Portal.CertificateRow(context,settings);object p;
  if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p)){((SelectionItemPattern)p).Select();selectionMethod="SelectionItemPattern.Select";}
  else if(row.TryGetCurrentPattern(InvokePattern.Pattern,out p)){((InvokePattern)p).Invoke();selectionMethod="InvokePattern.Invoke";}
  else {selectionMethod="UIA-direct-selection-unavailable";Save();return new {directSelectionSupported=false,alreadySelected=(row.Current.Name??"").StartsWith("선택됨"),domSelectionRequired=true};}
  Portal.ConfirmSelectedCertificate(context,settings);selected=true;Done(selectionMethod);return new {directSelectionSupported=true,selected=true};
 }
 void VerifyDom(){Stage("VERIFY_DOM_CERTIFICATE_SELECTION");context=Current();Portal.ConfirmSelectedCertificate(context,settings);selected=true;selectionMethod="DOM semantic selection + UIA verification";Done(selectionMethod);}
 void Login(){
  if(attempted||!freshDialog||!selected)throw new SafeFailure("새 인증서창과 선택 확인이 필요하며 로그인은 한 번만 시도합니다.");
  context=Current();Portal.Validate(context,Portal.LoginPath,false);Portal.ConfirmSelectedCertificate(context,settings);
  DateTime expiry;if(!DateTime.TryParse(settings.CertExpiry,out expiry)||expiry.Date<DateTime.Today)throw new SafeFailure("인증서 유효기간을 확인해 주세요.");
  var password=Portal.Password(context);object p;if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out p)||((ValuePattern)p).Current.IsReadOnly)throw new SafeFailure("암호칸 직접 입력을 사용할 수 없어요.");var value=(ValuePattern)p;
  var confirm=Button(context,"확인");object confirmPattern;if(!confirm.TryGetCurrentPattern(InvokePattern.Pattern,out confirmPattern))throw new SafeFailure("확인 버튼에 InvokePattern이 없습니다.");
  Action guard=()=>{audit.AssertBackground();Portal.Validate(context,Portal.LoginPath,false);Portal.ConfirmSelectedCertificate(context,settings);if(!Automation.Compare(password,Portal.Password(context))||value.Current.IsReadOnly)throw new SafeFailure("보호된 입력 대상이 변경됐어요.");};
  guard();Stage("PASSWORD_DIRECT_SET");attempted=true;settings.PendingAttempt=true;settings.Save();Save();
  Vault.SetDirect(settings.Target,secret=>value.SetValue(secret),guard);Done("ValuePattern.SetValue; no read-back");
  Stage("CONFIRM_INVOKE");guard();submissions=1;Save();((InvokePattern)confirmPattern).Invoke();Done("InvokePattern.Invoke once");
  Stage("LOGIN_RESPONSE");context=Portal.Wait(()=>{audit.AssertBackground();var c=Current();return c.Path==Portal.MainPath&&Portal.Id(c.Doc,"btn-logout")!=null?c:null;},45000,"로그인 성공을 확인하지 못했어요. 재입력·재제출하지 않습니다.");
  Portal.Validate(context,Portal.MainPath,false);login=true;settings.PendingAttempt=false;settings.Verified=true;settings.Save();Done("HTTPS main + logout marker");
 }
 void OpenSystem(string name,string host,string path,string prefix){
  if(!login)throw new SafeFailure("이번 시험에서 로그인 성공을 먼저 확인해야 합니다.");
  Stage("PORTAL_TAB_FOR_"+name);
  var root=AutomationElement.FromHandle(hwnd);var tabs=Portal.All(root,ControlType.TabItem).Where(t=>(t.Current.Name??"").StartsWith("업무포털 메인")).ToList();
  if(tabs.Count!=1)throw new SafeFailure("전용 창의 포털 탭을 하나로 확인하지 못했어요.");
  object selection;if(!tabs[0].TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))throw new SafeFailure("포털 탭 직접 선택 기능이 없습니다.");
  if(!((SelectionItemPattern)selection).Current.IsSelected)((SelectionItemPattern)selection).Select();Done("SelectionItemPattern.Select");
  context=Portal.Wait(()=>{var c=Current();return c.Path==Portal.MainPath?c:null;},10000,"포털 메인 탭을 확인하지 못했어요.");
  var beforeWindows=new HashSet<IntPtr>();var beforeTabs=new HashSet<string>();
  foreach(var w in Portal.Windows()){beforeWindows.Add(w.Item1);foreach(var t in Portal.All(w.Item2,ControlType.TabItem))beforeTabs.Add(String.Join("-",t.GetRuntimeId()));}
  var links=Portal.All(context.Doc,ControlType.Hyperlink).Where(e=>e.Current.Name==name&&!e.Current.IsOffscreen).Where(e=>{Uri u;return Uri.TryCreate(e.Current.AutomationId,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host==host&&u.Port==443;}).ToList();
  if(links.Count!=1)throw new SafeFailure("공식 시스템 메뉴를 하나로 확인하지 못했어요.");
  Stage("SSO_CREATE_"+name);Invoke(links[0]);
  var target=Portal.Wait(()=>{
   audit.AssertBackground();var found=new List<BrowserTab>();
   foreach(var w in Portal.Windows()){if(w.Item1!=hwnd&&beforeWindows.Contains(w.Item1))continue;foreach(var t in Portal.All(w.Item2,ControlType.TabItem))if((t.Current.Name??"").StartsWith(prefix)&&!beforeTabs.Contains(String.Join("-",t.GetRuntimeId())))found.Add(new BrowserTab{Hwnd=w.Item1,Window=w.Item2,Tab=t});}
   if(found.Count>1)throw new SafeFailure("새 시스템 탭이 여러 개라 선택하지 않습니다.");return found.Count==1?found[0]:null;
  },45000,"새 시스템 탭을 확인하지 못했어요. 메뉴를 다시 누르지 않습니다.");
  audit.AddTarget(target.Hwnd);audit.AssertBackground();Done("InvokePattern.SSO");
  Stage("SYSTEM_TAB_"+name);object tabSelection;if(!target.Tab.TryGetCurrentPattern(SelectionItemPattern.Pattern,out tabSelection))throw new SafeFailure("새 탭 직접 선택 기능이 없습니다.");if(!((SelectionItemPattern)tabSelection).Current.IsSelected)((SelectionItemPattern)tabSelection).Select();Done("SelectionItemPattern.Select");
  Stage("SYSTEM_READY_"+name);Portal.Wait(()=>{audit.AssertBackground();foreach(var d in Portal.All(target.Window,ControlType.Document)){Uri u;if(!Uri.TryCreate(Portal.DocUrl(d),UriKind.Absolute,out u)||u.Scheme!="https"||u.Host!=host||u.Port!=443||u.AbsolutePath!=path||d.Current.IsOffscreen)continue;if(Portal.All(d,ControlType.Button).Any(b=>b.Current.Name=="로그아웃"||(b.Current.AutomationId??"").EndsWith(".btnLogOut")))return d;}return null;},45000,"시스템 초기 화면과 로그인 상태를 확인하지 못했어요.");
  if(name=="나이스")neis=true;else kedu=true;Done("HTTPS initial screen + logout marker");
 }
 object Command(string command){
  if(blocked&&command!="status")throw new SafeFailure("중단된 시험에서는 추가 조작하지 않습니다.");
  switch(command){
   case "status":return new {step=step,loginSuccess=login,neisSuccess=neis,keduSuccess=kedu};
   case "prepare":Prepare();return new {certificateDialogCreated=true};
   case "begin-dom-open":BeginDomOpen();return new {readyForDomLoginControl=true};
   case "verify-dom-open":VerifyDomOpen();return new {certificateDialogCreated=true};
   case "begin-dom-select":Stage("SELECT_CERTIFICATE_DOM");return new {readyForDomCertificateSelection=true};
   case "inspect":return Inspect();
   case "select":return Select();
   case "verify-dom":VerifyDom();return new {certificateMatchedAndSelected=true};
   case "login":Login();return new {loginSuccess=login};
   case "neis":OpenSystem("나이스","gwe.neis.go.kr","/jsp/main.jsp","4세대 나이스 시스템");return new {neisSuccess=neis};
   case "kedu":OpenSystem("K-에듀파인","klef.gwe.go.kr","/keris_ui/main.do","K-에듀파인");return new {keduSuccess=kedu};
   case "finish":Stage("FINAL");state=login&&neis&&kedu?"success":"incomplete";Save();return new {completeBackgroundSuccess=state=="success"};
   default:throw new SafeFailure("알 수 없는 검사 명령입니다.");
  }
 }
 internal static int Execute(Settings s,long hwnd,string report){
  using(var session=new StrictSession(s,hwnd,report)){
   Console.WriteLine("STRICT_SESSION_READY");Console.Out.Flush();string line;
   while((line=Console.ReadLine())!=null){if(line=="quit")break;try{Console.WriteLine(new JavaScriptSerializer().Serialize(session.Command(line)));}catch(SafeFailure e){session.blocked=true;session.state="stopped";session.steps.Add(new {step=session.step,error=e.Message});session.Save();Console.WriteLine("STOP: "+e.Message);}catch(Exception e){session.blocked=true;session.state="stopped";session.steps.Add(new {step=session.step,errorType=e.GetType().Name});session.Save();Console.WriteLine("STOP: "+e.GetType().Name);}Console.Out.Flush();}
   if(session.state=="running")session.state="incomplete";session.Save();return session.state=="success"?0:3;
  }
 }
 public void Dispose(){audit.Dispose();}
}
