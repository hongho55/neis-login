using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

// Diagnostic probes that never read the password and background-input trials.
internal static partial class Portal {
 internal static void BindProbeWindow(Settings s,long hwnd){
  IntPtr handle=new IntPtr(hwnd);var target=Windows().SingleOrDefault(w=>w.Item1==handle);
  if(target==null)throw new SafeFailure("검사에서 지정한 Edge 창이 없어졌어요. 다른 창으로 바꾸지 않습니다.");
  workWindow=handle;Native.GetWindowThreadProcessId(handle,out workProcess);workProfile=s.Profile;workPortalTabKey=null;launch.Reset();
  var c=Locate(s.Profile);if(c==null)throw new SafeFailure("검사 대상 창의 포털 HTTPS 문서를 확인하지 못했어요.");Validate(c,c.Path,false);
 }
 internal static object ProbeBackground(Settings s){
  IntPtr original=Native.GetForegroundWindow();
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),h=>ForegroundTrace.Request(h,FocusPoint.RESTORE_REQUEST)&&Native.GetForegroundWindow()==h);
  try{
   var c=EnsurePortal(s.Profile,false);Validate(c,c.Path,false);
   if(c.Path==MainPath)return new {alreadyLoggedIn=true,passwordRead=false,secretWritten=false,submitted=false};
   c=WaitSecurity(s.Profile);
   var login=Id(c.Doc,"btnLgn");
    if(Dialog(c)==null){
    object invoke;
    if(login==null||!login.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new SafeFailure("로그인 버튼의 직접 실행 기능이 없어 백그라운드 검사를 중단했어요. 좌표 클릭으로 바꾸지 않았습니다.");
    FreshSecurity(c);((InvokePattern)invoke).Invoke();
    ReturnFocus(FocusPoint.DIAGNOSTIC_RETURN);
   }
    c=Wait(()=>{
    var x=Locate(s.Profile);
    if(x==null||x.Path!=LoginPath||Dialog(x)==null)return null;
   if(!SecurityReady(x))return null;
     try{
     return Certificates(x).Count>0?x:null;
     }catch(SafeFailure){
     return null;
    }
   },15000,"인증서 목록 행의 준비 상태를 확인하지 못했어요. 반복 클릭하지 않았습니다.");
   Validate(c,LoginPath,false);var password=Password(c);object value;
   bool supported=password.TryGetCurrentPattern(ValuePattern.Pattern,out value);
   bool readOnly=supported&&((ValuePattern)value).Current.IsReadOnly;
   bool certificateMatched=false,certificateSelected=false,rowSelectable=false;
    if(s.CertUser!=""){
     try{
     var row=CertificateRow(c,s);
     certificateMatched=true;
     certificateSelected=(row.Current.Name??"").StartsWith("선택됨");
     object selection;
     rowSelectable=row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection);
     }catch(SafeFailure){
    }
   }
   var visibleCertificates=new List<CertificateInfo>();try{visibleCertificates=Certificates(c);}catch(SafeFailure){}
   var certificateList=Id(Dialog(c),"kc_cert_list");
   var certificateNodes=certificateList==null?new object[0]:certificateList.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Take(48).Where(e=>!e.Current.IsPassword).Select(e=>(object)new {type=e.Current.ControlType.ProgrammaticName,name=e.Current.Name}).ToArray();
   var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();object confirmation;
   bool directSubmit=buttons.Count==1&&buttons[0].TryGetCurrentPattern(InvokePattern.Pattern,out confirmation);
   ReturnFocus(FocusPoint.DIAGNOSTIC_RETURN);
    return new {
    alreadyLoggedIn=false,
    hwnd=c.Hwnd.ToInt64(),
    processId=workProcess,
    background=Native.GetForegroundWindow()!=c.Hwnd,
    passwordProtected=password.Current.IsPassword,
    valuePatternSupported=supported,
    readOnly=readOnly,
    certificateMatched=certificateMatched,
    certificateSelected=certificateSelected,
    rowSelectionSupported=rowSelectable,
    directSubmitSupported=directSubmit,
    certificates=visibleCertificates,
    certificateNodes=certificateNodes,
    passwordRead=false,
    secretWritten=false,
    submitted=false
   };
  }finally{focusReturn=null;}
 }
 internal static object BackgroundOnce(Settings s,long hwnd){
  if(s.CertStorageConfigured||s.RegistrationPending)throw new SafeFailure("저장 위치를 등록한 인증서는 정상 업무 시작으로 진행해 주세요. 이 별도 진단은 암호를 입력하지 않습니다.");
  BindProbeWindow(s,hwnd);var c=WaitSecurity(s.Profile);Validate(c,LoginPath,false);
  if(s.PendingAttempt)throw new SafeFailure("이전 입력 결과가 미확인이라 백그라운드 시험을 시작하지 않았어요.");
  if(!Vault.Exists(s.Target))throw new SafeFailure("현재 Windows 사용자에게 등록된 암호가 없어요. 시험하지 않습니다.");
  DateTime expiry;if(!DateTime.TryParse(s.CertExpiry,out expiry)||expiry.Date<DateTime.Today)throw new SafeFailure("등록 인증서 유효기간을 확인하지 못했어요.");
  ConfirmSelectedCertificate(c,s);var password=Password(c);object pattern;
  if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)||((ValuePattern)pattern).Current.IsReadOnly)throw new SafeFailure("암호칸의 직접 입력 기능이 없어요. 전면 키 입력으로 바꾸지 않습니다.");
  var value=(ValuePattern)pattern;
  var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();object submitPattern;
  if(buttons.Count!=1||!buttons[0].TryGetCurrentPattern(InvokePattern.Pattern,out submitPattern))throw new SafeFailure("직접 실행할 확인 버튼이 없어 제출하지 않았어요.");
  Action guard=()=>{
   FinalAuthenticationGuard(c,s,password);
   Validate(c,LoginPath,false);ConfirmSelectedCertificate(c,s);
   if(Native.GetForegroundWindow()==c.Hwnd)throw new SafeFailure("업무 창이 전면으로 바뀌어 백그라운드 시험을 중단했어요. 반복 입력하지 않습니다.");
   if(!Automation.Compare(password,Password(c))||value.Current.IsReadOnly)throw new SafeFailure("보호된 입력 요소가 바뀌어 중단했어요.");
  };
  guard();Input(()=>{s.PendingAttempt=true;s.Save();},()=>{AppState.Record("BACKGROUND_INPUT_STARTED");Vault.SetDirect(s.Target,secret=>value.SetValue(secret),guard);guard();});
  AppState.Record("BACKGROUND_INPUT_SET");Report("백그라운드 직접 입력 완료. 전면화·키보드 입력 없음.");
  // No retries, no input-method fallback, and no read-back of the password value.
  guard();SubmitOnce(()=>{guard();var current=Confirmation(FreshSecurity(c));object invoke;if(!current.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new SafeFailure("확인 버튼 실행 기능이 바뀌었습니다.");CheckCancel();((InvokePattern)invoke).Invoke();});AppState.Record("BACKGROUND_SUBMITTED_ONCE");Report("확인 버튼 직접 실행 1회. 응답 확인 중.");
  bool backgroundAfterSubmit=Native.GetForegroundWindow()!=c.Hwnd;
  c=Wait(()=>{var x=Locate(s.Profile);return x!=null&&x.Path==MainPath&&Id(x.Doc,"btn-logout")!=null?x:null;},45000,"직접 입력 후 로그인 성공을 확인하지 못했어요. 재입력·재제출하지 않았습니다.");
  Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();AppState.Record("BACKGROUND_LOGIN_SUCCESS");
   return new {
   loginSuccess=true,
   backgroundInputVerified=true,
   backgroundAfterSubmit=backgroundAfterSubmit,
   backgroundAtCompletion=Native.GetForegroundWindow()!=c.Hwnd,
   keyboardInputUsed=false,
   focusRequested=false,
   passwordReadBack=false,
   submittedCount=1,
   startupRegisteredByTest=false
  };
 }
}
