using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Web.Script.Serialization;

// Uses synthetic traces in a temp folder only; never reads the real settings, logs or credentials.
internal static class DiagnosticsBundleTests {
 static string Line(string code,string stage,long elapsed,string mode,string error){
  return new JavaScriptSerializer().Serialize(new{time="2026-11-20T09:00:00.0000000+09:00",run="r",pid=1,session=1,version="0.4.16.0",mode=mode,code=code,stage=stage,elapsed_ms=elapsed,error_type=error});
 }
 static string[] Success(long certWait){
  return new[]{Line("PROCESS_START","NONE",0,"auto",null),Line("EDGE_LAUNCH_REQUEST","EDGE_LAUNCH",50,"auto",null),Line("WAIT_BEGIN","CERTIFICATE_BUTTON_READY",100,"auto",null),Line("WAIT_READY","CERTIFICATE_BUTTON_READY",100+certWait,"auto",null),Line("SYSTEM_MENU_REQUESTS_COMPLETE","NEIS_MENU",5000,"auto",null)};
 }
 static string[] Timeout(){
  return new[]{Line("PROCESS_START","NONE",0,"auto",null),Line("CERTIFICATE_OPEN_REQUEST","CERTIFICATE_DIALOG",40,"auto",null),Line("WAIT_BEGIN","CERTIFICATE_DIALOG",50,"auto",null),Line("WAIT_TIMEOUT","CERTIFICATE_DIALOG",60050,"auto",null),Line("ERROR","CERTIFICATE_DIALOG",60060,"auto","SafeFailure")};
 }

 internal static void Run(Action<bool,string> check,string root){
  var ok=TraceSummary.Digest("a.jsonl",Success(2700));
  check(ok.Attempt&&ok.Outcome==RunOutcome.Success&&ok.StageWaitMs["CERTIFICATE_BUTTON_READY"]==2700&&ok.DurationMs==5000&&ok.Mode=="auto"&&ok.Version=="0.4.16.0","diag/successful run is an attempt with its measured stage wait and duration");
  var lingering=TraceSummary.Digest("a-long.jsonl",Success(2700).Concat(new[]{Line("FOREGROUND_SAMPLE","NONE",18962071,"auto",null)}));
  check(lingering.Outcome==RunOutcome.Success&&lingering.DurationMs==5000,"diag/duration is time to the outcome, not how long the helper window stayed open afterwards");
  var lateFailure=TraceSummary.Digest("b-long.jsonl",Timeout().Concat(new[]{Line("FOREGROUND_SAMPLE","NONE",9000000,"auto",null)}));
  check(lateFailure.DurationMs==60050,"diag/failed run duration is the time of the first failure event");
  var bad=TraceSummary.Digest("b.jsonl",Timeout());
  check(bad.Outcome==RunOutcome.Failed&&bad.FailureStage=="CERTIFICATE_DIALOG"&&bad.FailureType=="TIMEOUT","diag/first failure event decides stage and type (timeout, not the later generic error)");
  var errored=TraceSummary.Digest("c.jsonl",new[]{Line("EXISTING_SESSION","NONE",10,"manual",null),Line("ERROR","PORTAL_DOCUMENT",900,"manual","SafeFailure")});
  check(errored.Outcome==RunOutcome.Failed&&errored.FailureType=="SafeFailure","diag/error type is reported without any message text");
  var stopped=TraceSummary.Digest("d.jsonl",new[]{Line("AUTHENTICATION_BEGIN","AUTHENTICATION",10,"auto",null),Line("CANCEL_REQUESTED","AUTHENTICATION",20,"auto",null)});
  check(stopped.Outcome==RunOutcome.Stopped&&stopped.Attempt,"diag/user stop is neither success nor failure");
  var open=TraceSummary.Digest("e.jsonl",new[]{Line("EDGE_LAUNCH_REQUEST","EDGE_LAUNCH",10,"auto",null)});
  check(open.Outcome==RunOutcome.Incomplete&&open.Attempt,"diag/run with no outcome event is incomplete");
  var settings=TraceSummary.Digest("f.jsonl",new[]{Line("PROCESS_START","NONE",0,"manual",null),Line("MAIN_WINDOW_OPEN","NONE",200,"manual",null)});
  check(!settings.Attempt,"diag/just opening the window is not counted as a login attempt");
  var messy=TraceSummary.Digest("g.jsonl",new[]{"not json","",Line("PROCESS_START","NONE",0,"auto",null),"{broken","[1,2]"});
  check(messy.SkippedLines==3&&messy.Mode=="auto","diag/malformed or non-object lines are skipped and counted");

  var runs=new List<RunDigest>{ok,TraceSummary.Digest("a2.jsonl",Success(2900)),TraceSummary.Digest("a3.jsonl",Success(100)),bad,settings};
  string report=TraceSummary.Report(runs,new DateTime(2026,11,23,10,0,0));
  check(report.Contains("로그인 시도 4회")&&report.Contains("성공 3 · 실패 1")&&report.Contains("성공률 75%"),"diag/report counts attempts and success rate");
  check(report.Contains("CERTIFICATE_DIALOG / TIMEOUT : 1회"),"diag/report lists the failing stage and type");
  check(report.Contains("CERTIFICATE_BUTTON_READY : 2700 / 2900 / 3"),"diag/report gives median, max and count of successful waits");
  check(TraceSummary.Report(new List<RunDigest>(),DateTime.Now).Contains("로그인 시도 0회"),"diag/empty history still produces a report");

  string identity=@"C:\Users\fake-person";
  check(DiagnosticsBundle.ContainsIdentity("path "+identity.Replace("\\","\\\\")+"\\\\x",new[]{identity})&&DiagnosticsBundle.ContainsIdentity(identity.ToUpperInvariant(),new[]{identity})&&!DiagnosticsBundle.ContainsIdentity("clean text",new[]{identity,null,"ab"}),"diag/identity guard matches raw, JSON-escaped and differently cased paths but ignores empty or tiny values");

  string home=Path.Combine(root,"diag-home");string logs=Path.Combine(home,"logs");Directory.CreateDirectory(logs);
  File.WriteAllLines(Path.Combine(logs,"startup-20261120T000000000-1-aaaaaaaa.jsonl"),Success(2700));
  File.WriteAllLines(Path.Combine(logs,"startup-20261121T000000000-2-bbbbbbbb.jsonl"),Timeout());
  File.WriteAllLines(Path.Combine(logs,"startup-20261122T000000000-3-cccccccc.jsonl"),new[]{Line("PROCESS_START","NONE",0,"auto",null),new JavaScriptSerializer().Serialize(new{code="LEAK",path=identity})});
  File.WriteAllText(Path.Combine(logs,"other.txt"),"SECRET_OTHER_FILE");
  File.WriteAllText(Path.Combine(home,"settings.json"),"{\"CertUser\":\"SECRET_CERT_HOLDER\",\"Profile\":\"Default\"}");
  File.WriteAllText(Path.Combine(home,"credentials.txt"),"SECRET_PASSWORD");
  File.WriteAllText(Path.Combine(home,"events.log"),"2026-11-20T09:00:00 RUN_STARTED\n");
  File.WriteAllText(Path.Combine(home,"status.json"),"{\"code\":\"RUN_SUCCESS\"}");
  string zip=Path.Combine(root,"diag-out.zip");
  string summary=DiagnosticsBundle.Create(home,zip,new{app=new{version="0.4.16.0"},edge="1.2.3"},new[]{identity},new DateTime(2026,11,23),30);
  var names=new List<string>();var all=new System.Text.StringBuilder();
  using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries){names.Add(entry.FullName);using(var reader=new StreamReader(entry.Open()))all.Append(reader.ReadToEnd());}
  check(names.OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"environment.json","events.log","logs/startup-20261120T000000000-1-aaaaaaaa.jsonl","logs/startup-20261121T000000000-2-bbbbbbbb.jsonl","status.json","summary.txt"}),"diag/bundle contains only the allow-listed files");
  check(!all.ToString().Contains("SECRET_")&&!all.ToString().Contains("fake-person"),"diag/settings, credentials, unrelated files and personal paths never reach the bundle");
  check(summary.Contains("startup-20261122T000000000-3-cccccccc.jsonl (개인 경로가 들어 있어 제외)")&&summary.Contains("로그인 시도 2회"),"diag/leaky trace is excluded, named in the summary, and not counted");
  check(!File.Exists(zip+".tmp"),"diag/no partial temp file is left behind");
  bool refused=false;try{DiagnosticsBundle.Create(home,zip,new{},new string[0],DateTime.Now,30);}catch(IOException){refused=true;}
  check(refused&&!File.Exists(zip+".tmp"),"diag/existing output is never overwritten");

  string zip2=Path.Combine(root,"diag-out2.zip");
  DiagnosticsBundle.Create(Path.Combine(root,"diag-missing-home"),zip2,new{leak=identity},new[]{identity},DateTime.Now,30);
  using(var archive=ZipFile.OpenRead(zip2)){
   string env;using(var reader=new StreamReader(archive.GetEntry("environment.json").Open()))env=reader.ReadToEnd();
   check(archive.Entries.Count==2&&!env.Contains("fake-person")&&env.Contains("excluded"),"diag/missing logs still yield a bundle and environment facts with identity are withheld");
  }

  string notZip=Path.Combine(root,"diag-not-a-zip.txt");
  check(DiagnosticsAction.RunFromCommandLine(new[]{"--diagnostics-bundle",notZip})==2&&!File.Exists(notZip),"diag/command line rejects a non-zip output path without writing");
  check(DiagnosticsAction.RunFromCommandLine(new[]{"--diagnostics-bundle",zip})==2,"diag/command line rejects an existing output file");
  var facts=new JavaScriptSerializer().Serialize(DiagnosticsAction.EnvironmentFacts(()=>new{faked=true}));
  check(facts.Contains("\"edge\"")&&facts.Contains("\"config\"")&&facts.Contains("faked")&&!DiagnosticsBundle.ContainsIdentity(facts,DiagnosticsAction.Identities()),"diag/real environment facts are produced and contain no local identity");
 }
}
