using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

// Run entry points and the direct/keyboard authentication steps.
internal static partial class Portal {
 static bool RestoreWorkWindow(IntPtr hwnd){
  ForegroundTrace.Request(hwnd,FocusPoint.RESTORE_REQUEST);if(Native.GetForegroundWindow()==hwnd)return true;
  IntPtr before=Native.GetForegroundWindow();
   try{
   AutomationElement.FromHandle(hwnd).SetFocus();
   }catch(InvalidOperationException){
   }catch(ElementNotAvailableException){
   }finally{
   ForegroundTrace.UiaResult(hwnd,before);
  }
  return Native.GetForegroundWindow()==hwnd;
 }
 internal static bool Run(Settings s,bool test,bool direct=true){
  IntPtr original=Native.GetForegroundWindow();if(original==HelperWindow)original=ReturnWindowHint;
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),RestoreWorkWindow,true);
  focusReturn.Own(HelperWindow);
  try{return RunCore(s,test,direct);}finally{try{if(focusReturn!=null)ReturnFocus(FocusPoint.RUN_FINALLY_RETURN);}finally{focusReturn=null;}}
 }
 static void AuthenticateDirect(Settings s,PortalContext c,PreparedCertificateStorage storage=null){
  Validate(c,LoginPath,false);bool foreground=Native.EnsureForeground(c.Hwnd,FocusPoint.AUTHENTICATE_REQUEST,workProcess);
  c=WaitSecurity(s.Profile);
  Validate(c,LoginPath,false);var row=CertificateRow(c,s);
  var password=Password(c);object pattern;
  if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)||((ValuePattern)pattern).Current.IsReadOnly)throw new SafeFailure("암호칸 직접 입력을 사용할 수 없습니다. 다른 방식으로 자동 재시도하지 않습니다.");
  var value=(ValuePattern)pattern;
  Report("등록한 인증서로 로그인 중입니다. 잠시만 기다려 주세요.");
  if(!foreground)ForegroundTrace.Sample(c.Hwnd,FocusPoint.DIRECT_CONTROL_FALLBACK);
  if(!(row.Current.Name??"").StartsWith("선택됨")){
   FreshSecurity(c);
   object selection;if(row.TryGetCurrentPattern(SelectionItemPattern.Pattern,out selection))((SelectionItemPattern)selection).Select();
   else if(row.TryGetCurrentPattern(InvokePattern.Pattern,out selection))((InvokePattern)selection).Invoke();
   else throw new SafeFailure("등록 인증서 직접 선택이 지원되지 않습니다. 암호를 입력하지 않습니다.");
  }
  c=WaitSecurity(s.Profile);
  // Selection can rebuild the dialog. Acquire all input/submission elements again before input.
  password=Password(c);
  if(!password.TryGetCurrentPattern(ValuePattern.Pattern,out pattern)||((ValuePattern)pattern).Current.IsReadOnly)throw new SafeFailure("선택 후 암호 입력칸을 검증하지 못했습니다.");
  value=(ValuePattern)pattern;
  object confirmation;if(!Confirmation(c).TryGetCurrentPattern(InvokePattern.Pattern,out confirmation))throw new SafeFailure("확인 버튼의 직접 실행 기능을 확인하지 못했습니다. 암호를 입력하지 않습니다.");
   Action guard=()=>{
   CheckCancel();
   FinalAuthenticationGuard(c,s,password);
   if(storage!=null)ValidatePreparedStorage(storage);
   Validate(c,LoginPath,false);
   ConfirmSelectedCertificate(c,s);
   if(!Automation.Compare(password,Password(c))||value.Current.IsReadOnly||!password.Current.IsEnabled)throw new SafeFailure("암호 입력 대상 또는 인증서가 바뀌었습니다. 재입력하지 않습니다.");
  };
  try{
   guard();Input(()=>{s.PendingAttempt=true;s.Save();},()=>{AppState.Record("DIRECT_ELEMENT_INPUT_STARTED");Vault.SetDirect(s.Target,secret=>value.SetValue(secret),guard);guard();});
   guard();
   SubmitOnce(()=>{guard();var current=Confirmation(FreshSecurity(c));object invoke;if(!current.TryGetCurrentPattern(InvokePattern.Pattern,out invoke))throw new SafeFailure("제출 직전 확인 버튼이 바뀌었습니다.");AppState.Record("DIRECT_ELEMENT_SUBMIT_ONCE");CheckCancel();((InvokePattern)invoke).Invoke();});
  }finally{if(focusReturn!=null)ReturnFocus(FocusPoint.AUTH_INPUT_FINALLY_RETURN);}
  Report("로그인 응답 확인 중 · 자동으로 다시 제출하지 않습니다.");
  var loginFocus=new ForegroundWaitTrace();
   c=Wait(()=>{
   var x=Locate(s.Profile);
   bool found=x!=null&&x.Path==MainPath;
   bool marker=found&&Id(x.Doc,"btn-logout")!=null;
   loginFocus.Sample(c.Hwnd,FocusPoint.LOGIN_WAIT,found,found?(bool?)false:null,marker);
   return marker?x:null;
  },45000,"로그인 성공을 확인하지 못했습니다. 재입력·재제출하지 않습니다. 오류 상태를 확인해 주세요.",TraceStage.LOGIN_RESPONSE);
  Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();AppState.Record("DIRECT_ELEMENT_LOGIN_SUCCESS");Report("업무포털 로그인 완료 · 선택한 시스템을 엽니다.");
 }
 static bool RunCore(Settings s,bool test,bool direct){
  var c=EnsurePortal(s.Profile,false);Validate(c,c.Path,false);
  Milestone("EDGE_READY");
  if(c.Path==MainPath){if(test)throw new SafeFailure("이미 로그인되어 있어 암호 자동 입력을 검증할 수 없습니다. 직접 로그아웃한 뒤 시험해 주세요.");RunTrace.Record(TraceEvent.EXISTING_SESSION);Report("기존 로그인 세션 확인");}
  else {
   if(s.PendingAttempt)throw new SafeFailure("이전 입력 결과가 확인되지 않았습니다. 자동 재시도하지 않습니다. 화면을 확인한 뒤 앱에서 오류 상태를 초기화해 주세요.");
   if(s.RegistrationPending)throw new SafeFailure("인증서·저장 위치 등록이 완료되지 않았습니다. 등록을 다시 마쳐 주세요.");
   if(!Vault.Exists(s.Target))throw new SafeFailure("인증서 암호를 먼저 등록해 주세요.");
   DateTime expiry;if(!DateTime.TryParse(s.CertExpiry,out expiry)||expiry.Date<DateTime.Today)throw new SafeFailure("등록한 인증서 유효기간을 확인해 주세요.");
   var location=CertificateStorage.From(s);PreparedCertificateStorage storage=null;
   c=Prepare(s.Profile,false,s,!s.CertStorageConfigured);
   if(s.CertStorageConfigured){storage=SelectCertificateStorage(c,location);c=storage.Context;}
   c=WaitRegisteredCertificate(s,c);
   Milestone("CERTIFICATE_READY");
   RunTrace.Record(TraceEvent.AUTHENTICATION_BEGIN,TraceStage.AUTHENTICATION);
   if(direct)AuthenticateDirect(s,c,storage);
   else {
   Report("암호 입력·제출 동안 업무 창을 잠시 앞으로 가져옵니다.");c=EnsurePortal(s.Profile,true,false);Validate(c,LoginPath,true);
   c=WaitSecurity(s.Profile);
   var row=CertificateRow(c,s);Native.Click(row,c.Hwnd,workProcess);Thread.Sleep(250);
   c=Wait(()=>Locate(s.Profile),5000,"업무포털 대상이 바뀌었습니다.");Validate(c,LoginPath,true);ConfirmSelectedCertificate(c,s);
   c=WaitSecurity(s.Profile);var password=Password(c);FreshSecurity(c);password.SetFocus();Thread.Sleep(150);
    Action guard=()=>{
    CheckCancel();
    FinalAuthenticationGuard(c,s,password);
    if(storage!=null)ValidatePreparedStorage(storage);
    if(Native.GetForegroundWindow()!=c.Hwnd)throw new SafeFailure("암호 입력 중 활성 창이 바뀌었습니다. 즉시 중단했습니다.");
    var focused=AutomationElement.FocusedElement;
    if(!Automation.Compare(focused,password)||!focused.Current.IsPassword)throw new SafeFailure("암호 입력 대상의 포커스가 바뀌었습니다. 즉시 중단했습니다.");
   };
   Validate(c,LoginPath,true);
   guard();
    Input(()=>{
    s.PendingAttempt=true;
    s.Save();
    },()=>{
    Report("대상·인증서 확인 완료. 저장 암호를 로컬에서 입력 중");
    Vault.TypeInto(s.Target,guard);
    guard();
   });
   Validate(c,LoginPath,true);
   ConfirmSelectedCertificate(c,s);
   var buttons=All(Dialog(c),ControlType.Button).Where(e=>e.Current.Name=="확인"&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();
   if(buttons.Count!=1)throw new SafeFailure("확인 버튼을 하나로 찾지 못했습니다. 제출하지 않습니다.");
   guard();
   SubmitOnce(()=>{guard();var current=Confirmation(FreshSecurity(c));Report("로그인 1회 제출. 응답 확인 중");Native.Click(current,c.Hwnd,workProcess);});
   if(focusReturn!=null)ReturnFocus(FocusPoint.AUTH_INPUT_FINALLY_RETURN);
   c=Wait(()=>{var x=Locate(s.Profile);return x!=null&&x.Path==MainPath&&Id(x.Doc,"btn-logout")!=null?x:null;},45000,"로그인 성공을 확인하지 못했습니다. 입력 불허·암호 오류·응답 지연 등은 화면에서 구분해 주세요. 재제출하지 않았습니다.");
   Validate(c,MainPath,false);s.PendingAttempt=false;s.Verified=true;s.Save();Report("암호 자동 입력 및 업무포털 로그인 성공");
   }
  }
  Milestone("LOGIN_COMPLETE");
  SendSystemRequests(s);return true;
 }
}
