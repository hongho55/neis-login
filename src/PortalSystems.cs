using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Automation;

// Opening NEIS / K-EduFine through the official portal SSO menu.
internal static partial class Portal {
 internal static void OpenExistingSystems(Settings s,long hwnd){
  BindProbeWindow(s,hwnd);var c=Locate(s.Profile);Validate(c,MainPath,false);
  IntPtr original=Native.GetForegroundWindow();
  focusReturn=new ForegroundReturn(original,Native.GetForegroundWindow,h=>Native.IsWindow(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h),RestoreWorkWindow,true);
  focusReturn.Own(c.Hwnd);
  try{
   SendSystemRequests(s);
  }finally{try{if(focusReturn!=null)ReturnFocus(FocusPoint.RUN_FINALLY_RETURN);}finally{focusReturn=null;}}
 }
 static void SendSystemRequests(Settings s){
  int count=SystemLaunchRequests.Send(s.OpenNeis,s.OpenKedu,(name,host)=>OpenSystem(s,name,host));
  RunTrace.Record(TraceEvent.SYSTEM_MENU_REQUESTS_COMPLETE);
  Report(count==0?"업무포털 로그인 완료.":"완료: 업무포털 로그인 · 선택한 시스템 열기 요청을 보냈어요.");
 }
 static void OpenSystem(Settings s,string name,string host){
  EnsurePortal(s.Profile,false,false);
  var menu=Wait(()=>{
   var current=Locate(s.Profile);if(current==null||current.Path!=MainPath)return null;Validate(current,MainPath,false);if(Id(current.Doc,"btn-logout")==null)return null;
    var links=All(current.Doc,ControlType.Hyperlink).Where(e=>e.Current.Name==name&&!e.Current.IsOffscreen&&e.Current.IsEnabled).Where(e=>{
    Uri u;
    return Uri.TryCreate(e.Current.AutomationId,UriKind.Absolute,out u)&&u.Scheme=="https"&&u.Host==host&&u.Port==443;
   }).ToList();
   if(links.Count>1)throw new SafeFailure(name+" 공식 메뉴가 여러 개여서 실행하지 않습니다.");if(links.Count==0)return null;
   return new SystemMenuContext{Portal=current,Link=links[0]};
  },30000,name+" 공식 메뉴 준비를 확인하지 못했습니다. 새로고침하거나 메뉴를 반복 실행하지 않습니다.",name=="나이스"?TraceStage.NEIS_MENU:TraceStage.KEDU_MENU);
  var c=menu.Portal;Validate(c,MainPath,false);
  Report(name+" 공식 SSO 메뉴 열기");
  Native.Click(menu.Link,c.Hwnd,workProcess);
  RunTrace.Record(name=="나이스"?TraceEvent.NEIS_MENU_DISPATCHED:TraceEvent.KEDU_MENU_DISPATCHED,name=="나이스"?TraceStage.NEIS_MENU:TraceStage.KEDU_MENU);
  if(focusReturn!=null)ReturnFocus(FocusPoint.SYSTEM_MENU_RETURN);
  Report(name+" 열기 요청을 보냈어요.");
 }
}
