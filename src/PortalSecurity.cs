using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

internal static partial class Portal {
 static readonly Stopwatch securityClock=Stopwatch.StartNew();
 static SecurityLoadingGate securityGate;
 static SecurityLoadingGate SecurityGate {get{return securityGate??(securityGate=new SecurityLoadingGate(()=>securityClock.ElapsedMilliseconds,Thread.Sleep,CheckCancel,s=>Report(s)));}}
 static long SecurityPaused {get{return securityGate==null?0:securityGate.PausedMilliseconds;}}
 static SecurityLoadingSample ObserveSecurity(PortalContext c){
  try{if(c!=null)Validate(c,LoginPath,false);return SecurityLoadingDetector.Observe(c);}
  catch(ElementNotAvailableException){return new SecurityLoadingSample(SecurityLoadingState.Unknown,null);}
  catch(System.Runtime.InteropServices.COMException){return new SecurityLoadingSample(SecurityLoadingState.Unknown,null);}
 }
 // Used only before credentials. Reacquire the context after every wait, preserving bound window.
 internal static PortalContext WaitSecurity(string profile){
  SecurityGate.WaitWhenBlocked(()=>ObserveSecurity(Locate(profile)));
  var fresh=Locate(profile);if(fresh==null)throw new SafeFailure("보안 대기 후 업무포털 문서가 바뀌었습니다.");
  Validate(fresh,LoginPath,false);SecurityLoadingGate.RequireClear(ObserveSecurity(fresh));return fresh;
 }
 static PortalContext FreshSecurity(PortalContext expected){
  CheckCancel();var fresh=Locate(workProfile);
  if(fresh==null||fresh.Hwnd!=expected.Hwnd||!Automation.Compare(fresh.Doc,expected.Doc))throw new SafeFailure("보안 확인 중 업무포털 문서가 바뀌었습니다. 입력·제출하지 않습니다.");
  Validate(fresh,LoginPath,false);SecurityLoadingGate.RequireClear(ObserveSecurity(fresh));return fresh;
 }
 static bool SecurityReady(PortalContext c){
  if(ObserveSecurity(c).State==SecurityLoadingState.Clear)return true;
  WaitSecurity(workProfile);return false;
 }
 internal static void GuardPortalAction(IntPtr hwnd){
  if(hwnd!=workWindow)return;
  var current=Locate(workProfile);
  if(current==null)throw new SafeFailure("업무포털 작업 대상을 확인하지 못했습니다.");
  if(current.Path==LoginPath)FreshSecurity(current);
 }
 internal static void FinalAuthenticationGuard(PortalContext c,Settings settings,AutomationElement password){
  var fresh=FreshSecurity(c);ConfirmSelectedCertificate(fresh,settings);
  if(!Automation.Compare(password,Password(fresh)))throw new SafeFailure("암호 입력 요소가 바뀌었습니다. 재입력·재제출하지 않습니다.");
 }
 internal static AutomationElement Confirmation(PortalContext c){
  var dialog=Dialog(c);if(dialog==null)throw new SafeFailure("인증서창이 닫혔거나 바뀌었습니다. 재입력·재제출하지 않습니다.");
  var buttons=All(dialog,ControlType.Button).FindAll(b=>b.Current.Name=="확인"&&!b.Current.IsOffscreen&&b.Current.IsEnabled);
  if(buttons.Count!=1)throw new SafeFailure("현재 확인 버튼을 하나로 검증하지 못했습니다. 제출하지 않습니다.");return buttons[0];
 }
}
