using System;

internal enum FocusPoint { ACTIVATE_REQUEST, CLICK_REQUEST, AUTHENTICATE_REQUEST, RESTORE_REQUEST, RESTORE_UIA, EDGE_CREATED_RETURN, PORTAL_TAB_RETURN, CERTIFICATE_RETURN, AUTH_INPUT_FINALLY_RETURN, SYSTEM_MENU_RETURN, SYSTEM_TAB_RETURN, RUN_FINALLY_RETURN, DIAGNOSTIC_RETURN, TAB_FOREGROUND, TAB_BACKGROUND, LOGIN_WAIT, NEIS_WAIT, KEDU_WAIT }
internal sealed class ForegroundTraceData {
 public string point,return_outcome;
 public long target_hwnd,before_hwnd,after_hwnd;public uint target_pid,before_pid,after_pid;
 public bool? api_result,document_found,document_offscreen,logout_marker;
 public bool target_foreground,target_visible,target_minimized;
}

// Only observes window IDs, process IDs and boolean state. Never reads titles,
// controls, text, input values, URLs or process command lines.
internal static class ForegroundTrace {
 static uint Pid(IntPtr h){uint pid=0;if(h!=IntPtr.Zero)Native.GetWindowThreadProcessId(h,out pid);return pid;}
 internal static ForegroundTraceData Data(IntPtr target,IntPtr before,IntPtr after,FocusPoint point,bool? api=null){
  return new ForegroundTraceData{point=point.ToString(),target_hwnd=target.ToInt64(),before_hwnd=before.ToInt64(),after_hwnd=after.ToInt64(),target_pid=Pid(target),before_pid=Pid(before),after_pid=Pid(after),api_result=api,target_foreground=target!=IntPtr.Zero&&target==after,target_visible=target!=IntPtr.Zero&&Native.IsWindowVisible(target),target_minimized=target!=IntPtr.Zero&&Native.IsIconic(target)};
 }
 static void Emit(TraceEvent code,IntPtr target,IntPtr before,IntPtr after,FocusPoint point,bool? api,Action<TraceEvent,ForegroundTraceData> record){try{record(code,Data(target,before,after,point,api));}catch(Exception){}}
 internal static bool Request(IntPtr target,FocusPoint point){return Request(target,point,Native.SetForegroundWindow,Native.GetForegroundWindow,(code,data)=>RunTrace.Record(code,focus:data));}
 internal static bool Request(IntPtr target,FocusPoint point,Func<IntPtr,bool> request,Func<IntPtr> current,Action<TraceEvent,ForegroundTraceData> record){
  IntPtr before=current();Emit(TraceEvent.FOREGROUND_REQUEST,target,before,before,point,null,record);
  bool result=request(target);IntPtr after=current();Emit(TraceEvent.FOREGROUND_RESULT,target,before,after,point,result,record);return result;
 }
 internal static void Sample(IntPtr target,FocusPoint point,ForegroundReturnOutcome? outcome=null){try{IntPtr current=Native.GetForegroundWindow();var data=Data(target,current,current,point);data.return_outcome=outcome.HasValue?outcome.Value.ToString():null;RunTrace.Record(outcome.HasValue?TraceEvent.FOCUS_RETURN_DECISION:TraceEvent.FOREGROUND_SAMPLE,focus:data);}catch(Exception){}}
 internal static void UiaResult(IntPtr target,IntPtr before){try{RunTrace.Record(TraceEvent.FOREGROUND_SAMPLE,focus:Data(target,before,Native.GetForegroundWindow(),FocusPoint.RESTORE_UIA));}catch(Exception){}}
}

internal sealed class ForegroundWaitTrace {
 string previous;readonly Func<IntPtr> current;readonly Action<ForegroundTraceData> record;
 internal ForegroundWaitTrace():this(Native.GetForegroundWindow,data=>RunTrace.Record(TraceEvent.FOREGROUND_SAMPLE,focus:data)){}
 internal ForegroundWaitTrace(Func<IntPtr> foreground,Action<ForegroundTraceData> write){current=foreground;record=write;}
 internal void Sample(IntPtr target,FocusPoint point,bool? found=null,bool? offscreen=null,bool? marker=null){try{
  IntPtr foreground=current();var data=ForegroundTrace.Data(target,foreground,foreground,point);data.document_found=found;data.document_offscreen=offscreen;data.logout_marker=marker;
  string key=foreground.ToInt64()+"/"+data.target_visible+"/"+data.target_minimized+"/"+found+"/"+offscreen+"/"+marker;
  if(previous!=key){previous=key;record(data);}
 }catch(Exception){}}
}
