using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Web.Script.Serialization;

internal enum TraceEvent { PROCESS_START, PROCESS_EXIT, INSTANCE_ACQUIRED, DUPLICATE_EXIT, SETTINGS_LOAD_BEGIN, SETTINGS_LOADED, INSTALL_BEGIN, INSTALL_COMPLETE, AUTO_CONTEXT_OPEN, AUTO_MANUAL_WAIT, TRAY_READY, AUTO_PREFLIGHT_BEGIN, AUTO_PREFLIGHT_REJECTED, AUTO_PREFLIGHT_ACCEPTED, AUTO_WORK_START, AUTO_WORK_SUCCESS, AUTO_WORK_STOPPED, AUTO_GUIDANCE_OPEN, MAIN_WINDOW_OPEN, CANCEL_REQUESTED, SETTINGS_REQUESTED, EXISTING_SESSION, EDGE_LAUNCH_REQUEST, CERTIFICATE_OPEN_REQUEST, CERTIFICATE_BUTTON_DISPATCHED, CERTIFICATE_DOCUMENT_MISSING, CERTIFICATE_DOCUMENT_REPLACED, PRELOGIN_REFRESH_REQUESTED, PRELOGIN_REFRESH_DISPATCHED, AUTHENTICATION_BEGIN, NEIS_MENU_DISPATCHED, KEDU_MENU_DISPATCHED, WAIT_BEGIN, WAIT_READY, WAIT_TIMEOUT, WAIT_FAILED, ERROR, LOGGING_CHECK, FOREGROUND_REQUEST, FOREGROUND_RESULT, FOREGROUND_SAMPLE, FOCUS_RETURN_DECISION, PRELOGIN_STATE }
internal enum TraceStage { NONE, EDGE_LAUNCH, EDGE_NEW_WINDOW, PORTAL_TAB, PORTAL_DOCUMENT, CERTIFICATE_BUTTON_READY, PRELOGIN_REFRESH, CERTIFICATE_DIALOG, REGISTERED_CERTIFICATE, AUTHENTICATION, LOGIN_RESPONSE, NEIS_MENU, NEIS_TAB, NEIS_INITIAL_SCREEN, KEDU_MENU, KEDU_TAB, KEDU_INITIAL_SCREEN }

// Structured, per-process diagnostic file. Never accepts UI text, raw arguments,
// exception messages, URLs, settings or credential values.
internal sealed class ExecutionTrace {
 readonly object sync=new object();readonly Stopwatch clock=Stopwatch.StartNew();readonly string run=Guid.NewGuid().ToString("N");
 readonly string mode,version;readonly int pid,session;readonly Action<string> write;readonly Action failed;TraceStage last;
 internal ExecutionTrace(string runMode,string build,int process,int desktopSession,Action<string> sink,Action onFailure){mode=runMode;version=build;pid=process;session=desktopSession;write=sink;failed=onFailure;}
 internal static string Mode(string[] args){
  if(args.Contains("--logging-self-check"))return "logging_check";
  if(args.Contains("--auto"))return "auto";
  if(args.Contains("--install-only"))return "install";
  if(args.Contains("--status"))return "status";
  if(args.Contains("--enable-fast-startup")||args.Contains("--restore-startup")||args.Contains("--uninstall-startup"))return "autostart_configuration";
  if(args.Length==0||args.Contains("--show"))return "manual";
  return "other";
 }
 internal void Record(TraceEvent code,TraceStage? stage=null,Exception failure=null,int? limitMs=null,ForegroundTraceData focus=null,PreloginTraceData prelogin=null){
  lock(sync){try{
   if(stage.HasValue)last=stage.Value;
   write(new JavaScriptSerializer().Serialize(new{time=DateTimeOffset.Now.ToString("o"),run=run,pid=pid,session=session,version=version,mode=mode,code=code.ToString(),stage=last.ToString(),elapsed_ms=clock.ElapsedMilliseconds,limit_ms=limitMs,error_type=failure==null?null:failure.GetType().Name,hresult=failure==null?null:("0x"+failure.HResult.ToString("X8")),focus=focus,prelogin=prelogin}));
  }catch(Exception){try{failed();}catch(Exception){}}}
 }
}

internal static class RunTrace {
 internal static ExecutionTrace Current;internal static string FilePath;
 internal static void Start(string[] args){
  try{
   var process=Process.GetCurrentProcess();string folder=Path.Combine(Settings.Root,"logs");
   FilePath=Path.Combine(folder,"startup-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+process.Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".jsonl");
   string path=FilePath;
   Current=new ExecutionTrace(ExecutionTrace.Mode(args),Assembly.GetExecutingAssembly().GetName().Version.ToString(),process.Id,process.SessionId,line=>{Directory.CreateDirectory(folder);File.AppendAllText(path,line+Environment.NewLine,new UTF8Encoding(false));},AppState.NoteTraceFailure);
   Record(TraceEvent.PROCESS_START);
  }catch(Exception){AppState.NoteTraceFailure();}
 }
 internal static void Record(TraceEvent code,TraceStage? stage=null,Exception failure=null,int? limitMs=null,ForegroundTraceData focus=null,PreloginTraceData prelogin=null){var trace=Current;if(trace!=null)trace.Record(code,stage,failure,limitMs,focus,prelogin);}
}
