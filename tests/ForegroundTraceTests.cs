using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
internal static class ForegroundTraceTests {
 internal static void Run(Action<bool,string> check){
  IntPtr target=new IntPtr(-101),other=new IntPtr(-102),current=other;int calls=0;var events=new List<TraceEvent>();var records=new List<ForegroundTraceData>();Action<TraceEvent,ForegroundTraceData> write=(code,data)=>{events.Add(code);records.Add(data);};
  bool success=ForegroundTrace.Request(target,FocusPoint.AUTHENTICATE_REQUEST,h=>{calls++;current=h;return true;},()=>current,write);
  check(success&&calls==1&&events.SequenceEqual(new[]{TraceEvent.FOREGROUND_REQUEST,TraceEvent.FOREGROUND_RESULT})&&records[1].target_foreground,"focus/request records before result and actual foreground while calling activation once");
  current=target;events.Clear();records.Clear();bool denied=ForegroundTrace.Request(target,FocusPoint.AUTHENTICATE_REQUEST,h=>{calls++;return false;},()=>current,write);
  check(!denied&&records[1].api_result==false&&records[1].target_foreground,"focus/false API result is distinguishable from target already foreground");
  current=other;events.Clear();records.Clear();bool claimed=ForegroundTrace.Request(target,FocusPoint.AUTHENTICATE_REQUEST,h=>true,()=>current,write);
  check(claimed&&records[1].api_result==true&&!records[1].target_foreground,"focus/true API return does not fabricate actual foreground success");
  int before=calls;bool unaffected=ForegroundTrace.Request(target,FocusPoint.CLICK_REQUEST,h=>{calls++;return false;},()=>current,(code,data)=>{throw new IOException("INJECTED");});
  check(!unaffected&&calls==before+1,"focus/log sink failure does not retry or change activation result");
  var outcomes=new List<ForegroundReturnOutcome>();var restore=new ForegroundReturn(other,()=>current,h=>true,h=>{calls++;current=h;return true;});restore.Own(target);current=target;
  check(restore.TryReturn(outcomes.Add)&&outcomes.Last()==ForegroundReturnOutcome.RESTORED&&current==other,"focus/restore success reason preserves original activation");
  check(!restore.TryReturn(outcomes.Add)&&outcomes.Last()==ForegroundReturnOutcome.ALREADY_ORIGINAL,"focus/already-original skip distinguished from failed restore");
  current=new IntPtr(-103);int unchanged=calls;
  check(!restore.TryReturn(outcomes.Add)&&calls==unchanged&&outcomes.Last()==ForegroundReturnOutcome.USER_WINDOW_SELECTED,"focus/user-selected window preserved and recorded as skip");
  current=target;check(restore.TryReturn(o=>{throw new IOException("INJECTED");})&&current==other,"focus/observer failure cannot alter restore behavior");
  var samples=new List<ForegroundTraceData>();current=other;var poll=new ForegroundWaitTrace(()=>current,samples.Add);poll.Sample(target,FocusPoint.NEIS_WAIT,true,true,null);poll.Sample(target,FocusPoint.NEIS_WAIT,true,true,null);current=target;poll.Sample(target,FocusPoint.NEIS_WAIT,true,false,false);poll.Sample(target,FocusPoint.NEIS_WAIT,true,false,true);
  check(samples.Count==3&&samples[0].document_offscreen==true&&samples[2].logout_marker==true&&samples[1].target_foreground,"focus/wait records changed foreground visibility and logout state without duplicate samples");
  var json=new JavaScriptSerializer();var fields=json.Deserialize<Dictionary<string,object>>(json.Serialize(samples[0]));var allowed=new HashSet<string>{"point","return_outcome","target_hwnd","before_hwnd","after_hwnd","target_pid","before_pid","after_pid","api_result","document_found","document_offscreen","logout_marker","target_foreground","target_visible","target_minimized"};
  check(fields.Count==allowed.Count&&fields.Keys.All(allowed.Contains),"focus/schema contains IDs enum points and booleans only without titles URLs or input text");
 }
}
