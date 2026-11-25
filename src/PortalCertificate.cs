using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

// Certificate dialog discovery, row matching and password field lookup.
internal static partial class Portal {
 internal static AutomationElement Dialog(PortalContext c){
  var explicitDialog=Id(c.Doc,"kcasedialog");if(explicitDialog!=null)return explicitDialog;
  var list=Id(c.Doc,"kc_cert_list");if(list==null||list.Current.IsOffscreen)return null;
  // Chromium omits the generic DIV id in UIA. The observed parent is the web modal Window.
  var parent=TreeWalker.ControlViewWalker.GetParent(list);
  return parent!=null&&parent.Current.ControlType==ControlType.Window?parent:null;
 }
 internal static PortalContext Prepare(string profile,bool allowCreate=true,Settings recoverySettings=null,bool waitForRows=true){
  Report("인증서창을 준비하고 있어요.");var c=EnsurePortal(profile,false,allowCreate);
  Validate(c,c.Path,false);if(c.Path==MainPath)throw new SafeFailure("현재 로그인되어 있습니다. 실제 암호 시험·등록 전 업무포털에서 직접 로그아웃해 주세요.");
  c=WaitSecurity(profile);
  RunTrace.Record(TraceEvent.CERTIFICATE_OPEN_REQUEST,TraceStage.CERTIFICATE_DIALOG);
  var dialog=Dialog(c);
  if(dialog==null||dialog.Current.IsOffscreen){
   var stable=new StableElementObservation(CertificateStartup.ButtonStableMs);var clock=Stopwatch.StartNew();var recovery=new PreloginRecoveryGate();var observation=new PreloginStateTrace();
   c=Wait(()=>{
    var x=Locate(profile);
     if(x==null||x.Path!=LoginPath){
     observation.Observe(null);
     stable.Observe(null,false,clock.ElapsedMilliseconds);
     recovery.TryClaim(new PreloginRecoveryFacts(),clock.ElapsedMilliseconds);
     return null;
    }
    Validate(x,LoginPath,false);
    if(!SecurityReady(x)){stable.Observe(null,false,clock.ElapsedMilliseconds);recovery.TryClaim(new PreloginRecoveryFacts(),clock.ElapsedMilliseconds);return null;}
    var shown=Dialog(x);if(shown!=null&&!shown.Current.IsOffscreen)return x;
    if(recoverySettings!=null&&TryRecoverBlank(recoverySettings,x,recovery,clock.ElapsedMilliseconds,observation)){stable.Observe(null,false,clock.ElapsedMilliseconds);return null;}
    var button=Id(x.Doc,"btnLgn");bool ready=button!=null&&button.Current.IsEnabled&&!button.Current.IsOffscreen;
    string identity=ready?String.Join("-",x.Doc.GetRuntimeId())+"/"+String.Join("-",button.GetRuntimeId()):null;
    return stable.Observe(identity,ready,clock.ElapsedMilliseconds)?x:null;
   },CertificateStartup.ButtonReadyTimeoutMs,"로그인 화면과 인증서 버튼이 안정된 상태를 확인하지 못했습니다. 암호를 입력하거나 버튼을 반복 실행하지 않았습니다.",TraceStage.CERTIFICATE_BUTTON_READY);
   c=FreshSecurity(c);dialog=Dialog(c);
    if(dialog==null||dialog.Current.IsOffscreen){
    var login=Id(c.Doc,"btnLgn");
    if(login==null||!login.Current.IsEnabled||login.Current.IsOffscreen)throw new SafeFailure("인증서 로그인 버튼 상태가 바뀌어 중단했습니다. 암호를 입력하지 않았습니다.");
    Native.Click(login,c.Hwnd,workProcess);
    RunTrace.Record(TraceEvent.CERTIFICATE_BUTTON_DISPATCHED,TraceStage.CERTIFICATE_DIALOG);
   }
  }
  if(focusReturn!=null)ReturnFocus(FocusPoint.CERTIFICATE_RETURN);
  string requestedDocument=String.Join("-",c.Doc.GetRuntimeId());bool missingRecorded=false,replacedRecorded=false;
  return Wait(()=>{
   var x=Locate(profile);if(x==null||x.Path!=LoginPath){if(!missingRecorded){RunTrace.Record(TraceEvent.CERTIFICATE_DOCUMENT_MISSING,TraceStage.CERTIFICATE_DIALOG);missingRecorded=true;}return null;}
   if(!replacedRecorded&&String.Join("-",x.Doc.GetRuntimeId())!=requestedDocument){RunTrace.Record(TraceEvent.CERTIFICATE_DOCUMENT_REPLACED,TraceStage.CERTIFICATE_DIALOG);replacedRecorded=true;}
   Validate(x,LoginPath,false);
   if(!SecurityReady(x))return null;
   var d=Dialog(x);
   if(d==null||d.Current.IsOffscreen)return null;
   var list=Id(d,"kc_cert_list");
   return list!=null&&(!waitForRows||ReadCertificatesFromList(list).Count>0)?x:null;
  },CertificateStartup.DialogReadyTimeoutMs,"인증서 목록을 60초 동안 확인하지 못했습니다. 인증서 위치와 목록 표시를 확인해 주세요. 암호 입력·새로고침·반복 클릭은 하지 않았습니다.",TraceStage.CERTIFICATE_DIALOG);
 }
 internal static List<CertificateInfo> Certificates(PortalContext c,bool includeOffscreen=false){
  Validate(c,LoginPath,false);var list=Id(Dialog(c),"kc_cert_list");if(list==null)throw new SafeFailure("인증서 목록을 찾지 못했습니다.");
  var result=ReadCertificatesFromList(list,includeOffscreen);
  if(result.Count==0)throw new SafeFailure("인증서 목록의 실제 행이 아직 준비되지 않았습니다. 암호를 입력하지 않습니다.");
  return result;
 }
 static List<CertificateInfo> ReadCertificatesFromList(AutomationElement list,bool includeOffscreen=false){
  var result=new List<CertificateInfo>();
  foreach(var row in All(list,ControlType.DataItem)){
   if(!includeOffscreen&&row.Current.IsOffscreen)continue;
   var certificate=CertificateRowData.Parse(All(row,ControlType.DataItem).Select(cell=>cell.Current.Name).ToArray());
   if(certificate!=null)result.Add(certificate);
  }
  return result.GroupBy(x=>x.ToString()).Select(g=>g.First()).ToList();
 }
 static List<AutomationElement> MatchingCertificateRows(AutomationElement list,Settings s){
   return All(list,ControlType.DataItem).Where(r=>{
   var n=All(r,ControlType.DataItem).Select(x=>(x.Current.Name??"").Trim()).ToList();
   return n.Contains(s.CertUser)&&n.Contains(s.CertExpiry)&&n.Contains(s.CertIssuer);
  }).GroupBy(r=>CertificateRowData.IdentityKey(All(r,ControlType.DataItem).Select(x=>(x.Current.Name??"").Trim()).ToArray()))
    .Select(g=>g.OrderByDescending(r=>(r.Current.Name??"").StartsWith("선택됨")).First()).ToList();
 }
 static PortalContext WaitRegisteredCertificate(Settings s,PortalContext initial){
  Report("인증서 목록을 불러오는 중 · 등록한 인증서 확인 대기");
  return Wait(()=>{
   var c=Locate(s.Profile);if(c==null){if(BoundWindow()==null)throw new SafeFailure("확인 중인 업무 창이 닫혔거나 바뀌었습니다. 다른 창에 입력하지 않습니다.");return null;}
   if(c.Hwnd!=initial.Hwnd)throw new SafeFailure("확인 중인 업무 창이 바뀌었습니다. 다른 창에 입력하지 않습니다.");
   Validate(c,LoginPath,false);if(!SecurityReady(c))return null;var dialog=Dialog(c);if(dialog==null||dialog.Current.IsOffscreen)return null;var list=Id(dialog,"kc_cert_list");if(list==null)return null;
   var matches=MatchingCertificateRows(list,s);
   if(matches.Count>1)throw new SafeFailure("등록 정보가 서로 다른 인증서로 여러 개 표시됩니다. 임의 선택하지 않고 중단합니다.");
   return matches.Count==1?c:null;
  },15000,"목록에서 등록한 이름·만료일·발급자가 일치하는 인증서를 확인하지 못했습니다. 인증서 위치와 표시된 목록을 확인해 주세요. 암호는 입력하지 않았습니다.",TraceStage.REGISTERED_CERTIFICATE);
 }
 internal static AutomationElement CertificateRow(PortalContext c,Settings s){
  var list=Id(Dialog(c),"kc_cert_list");if(list==null)throw new SafeFailure("인증서 목록이 없어졌습니다.");
  var matches=MatchingCertificateRows(list,s);
  if(matches.Count==0)throw new SafeFailure(ReadCertificatesFromList(list).Count==0?"인증서 목록 행이 아직 준비되지 않았습니다. 암호를 입력하지 않습니다.":"현재 목록에 등록 정보가 일치하는 인증서가 보이지 않습니다. 인증서 위치를 확인해 주세요.");
  if(matches.Count>1)throw new SafeFailure("등록 정보가 서로 다른 인증서로 여러 개 표시됩니다. 임의 선택하지 않습니다.");return matches[0];
 }
 internal static void ConfirmSelectedCertificate(PortalContext c,Settings s){
  var row=CertificateRow(c,s);
  if(!(row.Current.Name??"").StartsWith("선택됨"))throw new SafeFailure("등록한 인증서가 선택된 상태인지 확인하지 못했습니다. 암호 입력·제출을 중단합니다.");
 }
  internal static AutomationElement Password(PortalContext c){
  var d=Dialog(c);
  if(d==null)throw new SafeFailure("인증서창이 없어졌습니다.");
  var fields=All(d,ControlType.Edit).Where(e=>e.Current.IsPassword&&!e.Current.IsOffscreen&&e.Current.IsEnabled).ToList();
  if(fields.Count!=1)throw new SafeFailure("암호 입력칸을 하나로 식별하지 못했습니다. 입력 차단으로 단정하지 않고 중단합니다.");
  return fields[0];
 }
}
