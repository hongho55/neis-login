using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

// One-shot recovery for a blank login page before any credential is entered.
internal static partial class Portal {
 static AutomationElement ReloadButton(PortalContext c){
  var matches=All(c.Window,ControlType.ToolBar).SelectMany(t=>All(t,ControlType.Button)).Where(b=>b.Current.AutomationId=="view_1003"&&b.Current.Name=="새로 고침"&&!b.Current.IsOffscreen&&b.Current.IsEnabled).GroupBy(b=>String.Join("-",b.GetRuntimeId())).Select(g=>g.First()).ToList();
  object pattern;return matches.Count==1&&matches[0].TryGetCurrentPattern(InvokePattern.Pattern,out pattern)?matches[0]:null;
 }
 static PreloginRecoveryFacts RecoveryFacts(Settings settings,PortalContext c){
  var dialog=Dialog(c);bool certificate=dialog!=null&&!dialog.Current.IsOffscreen;
  bool password=All(c.Doc,ControlType.Edit).Any(e=>!e.Current.IsOffscreen&&e.Current.IsPassword);
  bool content=All(c.Doc,ControlType.Button).Any(e=>!e.Current.IsOffscreen)||All(c.Doc,ControlType.Hyperlink).Any(e=>!e.Current.IsOffscreen)||All(c.Doc,ControlType.Edit).Any(e=>!e.Current.IsOffscreen)||All(c.Doc,ControlType.Text).Any(e=>!e.Current.IsOffscreen&&!String.IsNullOrWhiteSpace(e.Current.Name));
  var selected=All(c.Window,ControlType.TabItem).Where(t=>{object p;return t.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p)&&((SelectionItemPattern)p).Current.IsSelected;}).ToList();
  bool ownTab=selected.Count==1&&String.Join("-",selected[0].GetRuntimeId())==workPortalTabKey;
   return new PreloginRecoveryFacts{
   TargetVerified=c.Hwnd==workWindow&&c.Path==LoginPath&&ownTab&&!Native.IsIconic(c.Hwnd),
   SecurityState=ObserveSecurity(c).State,
   Blank=!content,
   CertificateVisible=certificate,
   PasswordVisible=password,
   BlockingDialog=All(c.Window,ControlType.Window).Any(w=>!w.Current.IsOffscreen),
   Pending=settings.PendingAttempt,
   Cancelled=Cancelled,
   ReloadSupported=ReloadButton(c)!=null
  };
 }
 static bool TryRecoverBlank(Settings settings,PortalContext c,PreloginRecoveryGate gate,long now,PreloginStateTrace observation){
  CheckCancel();Validate(c,LoginPath,false);
  if(ObserveSecurity(c).State!=SecurityLoadingState.Clear){gate.TryClaim(new PreloginRecoveryFacts(),now);return false;}
  var facts=RecoveryFacts(settings,c);
  observation.Observe(facts);
  if(!gate.TryClaim(facts,now))return false;
  var fresh=Locate(settings.Profile);if(fresh==null||fresh.Path!=LoginPath||!Automation.Compare(fresh.Doc,c.Doc))return false;
  Validate(fresh,LoginPath,false);if(!RecoveryFacts(settings,fresh).Safe)return false;
  var reload=ReloadButton(fresh);
  object pattern;
  if(reload==null||!reload.TryGetCurrentPattern(InvokePattern.Pattern,out pattern))throw new SafeFailure("새로고침 직접 실행 기능이 없어 중단했습니다. 키 입력으로 대신 실행하지 않습니다.");
  FreshSecurity(fresh);CheckCancel();if(settings.PendingAttempt)throw new SafeFailure("입력 결과 미확인 상태에서는 새로고침하지 않습니다.");
  RunTrace.Record(TraceEvent.PRELOGIN_REFRESH_REQUESTED,TraceStage.PRELOGIN_REFRESH);Report("인증 전 빈 로그인 화면 확인 · 새로고침 1회 후 준비를 기다립니다.");
  try{CheckCancel();((InvokePattern)pattern).Invoke();}
  catch(Exception ex){RunTrace.Record(TraceEvent.ERROR,TraceStage.PRELOGIN_REFRESH,ex);throw new SafeFailure("새로고침 실행 결과가 미확인이라 중단했습니다. 반복 새로고침이나 암호 입력은 하지 않습니다.");}
  RunTrace.Record(TraceEvent.PRELOGIN_REFRESH_DISPATCHED,TraceStage.PRELOGIN_REFRESH);return true;
 }
}
