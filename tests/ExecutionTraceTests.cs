using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal static class ExecutionTraceTests {
 internal static void Run(Action<bool,string> check){
  var lines=new List<string>();int writeFailures=0;
  string mode=ExecutionTrace.Mode(new[]{"--auto","TEST_ARGUMENT_MUST_NOT_BE_RECORDED"});
  var trace=new ExecutionTrace(mode,"0.4.6.0",123,1,lines.Add,()=>writeFailures++);
  trace.Record(TraceEvent.PROCESS_START);trace.Record(TraceEvent.ERROR,TraceStage.PORTAL_DOCUMENT,new InvalidOperationException("TEST_ERROR_MESSAGE_MUST_NOT_BE_RECORDED"));trace.Record(TraceEvent.PROCESS_EXIT);
  var serializer=new JavaScriptSerializer();var start=serializer.Deserialize<Dictionary<string,object>>(lines[0]);var error=serializer.Deserialize<Dictionary<string,object>>(lines[1]);
  check((string)start["code"]=="PROCESS_START"&&(string)start["mode"]=="auto"&&(string)start["version"]=="0.4.6.0","trace/process entry records safe mode version and process metadata");
  check((string)error["stage"]=="PORTAL_DOCUMENT"&&(string)error["error_type"]=="InvalidOperationException"&&!String.Join("",lines).Contains("MUST_NOT_BE_RECORDED"),"trace/raw arguments and exception messages excluded");
  var permitted=new HashSet<string>{"time","run","pid","session","version","mode","code","stage","elapsed_ms","limit_ms","error_type","hresult","focus","prelogin"};
  check(start.Keys.All(permitted.Contains)&&start.Count==permitted.Count,"trace/schema excludes URLs settings certificates and credential fields");
  check(lines.Select(x=>serializer.Deserialize<Dictionary<string,object>>(x)["run"]).Distinct().Count()==1&&lines.Count==3&&writeFailures==0,"trace/one run identifier correlates events with timestamps and elapsed time");
  check(ExecutionTrace.Mode(new[]{"--logging-self-check","--auto"})=="logging_check"&&ExecutionTrace.Mode(new[]{"TEST_PRIVATE_ARGUMENT"})=="other","trace/unknown arguments never become mode text");
  var failing=new ExecutionTrace("manual","test",0,0,s=>{throw new IOException("INJECTED");},()=>writeFailures++);failing.Record(TraceEvent.PROCESS_START);
  check(writeFailures==1,"trace/I-O failure signals existing log notice without throwing into workflow");
  var previous=RunTrace.Current;RunTrace.Current=trace;
  try{
   Portal.BeginOperation();int observed=0;string ready=Portal.Wait(()=>{observed++;return "READY";},1000,"UNUSED",TraceStage.PORTAL_DOCUMENT);
   check(ready=="READY"&&observed==1&&lines.Last().Contains("WAIT_READY"),"trace/observed readiness returns immediately without changing wait policy");
   bool timeout=false;try{Portal.Wait<string>(()=>null,0,"TEST_TIMEOUT_MESSAGE_MUST_NOT_BE_RECORDED",TraceStage.CERTIFICATE_DIALOG);}catch(SafeFailure){timeout=true;}
   check(timeout&&lines.Last().Contains("WAIT_TIMEOUT")&&lines.Last().Contains("CERTIFICATE_DIALOG")&&lines.Last().Contains("\"limit_ms\":0"),"trace/timeout records exact stage and existing limit");
   bool failed=false;try{Portal.Wait<string>(()=>{throw new InvalidOperationException("TEST_CALLBACK_MUST_NOT_BE_RECORDED");},1000,"UNUSED",TraceStage.PORTAL_TAB);}catch(InvalidOperationException){failed=true;}
   check(failed&&lines.Last().Contains("WAIT_FAILED")&&lines.Last().Contains("InvalidOperationException")&&!String.Join("",lines).Contains("MUST_NOT_BE_RECORDED"),"trace/observation failure keeps original exception while logging only type");
   Portal.RequestStop();bool stopped=false;try{Portal.Wait(()=>"READY",1000,"UNUSED",TraceStage.EDGE_NEW_WINDOW);}catch(SafeFailure){stopped=true;}
   check(stopped&&lines.Last().Contains("WAIT_FAILED"),"trace/existing cancellation guard still stops before observation");
  }finally{RunTrace.Current=previous;Portal.BeginOperation();}
 }
}
