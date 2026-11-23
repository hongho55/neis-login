using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal enum RunOutcome { Success, Failed, Stopped, Incomplete }

// What one process run did, derived only from its structured trace (codes, stages, timings, error types).
internal sealed class RunDigest {
 internal string File,Mode,Version,Started,FailureStage,FailureType;
 internal bool Attempt;
 internal RunOutcome Outcome=RunOutcome.Incomplete;
 internal long DurationMs;
 internal int SkippedLines;
 internal readonly Dictionary<string,long> StageWaitMs=new Dictionary<string,long>();
}

// Turns the per-run JSONL traces into the answers support needs: how often does it work, where does it
// fail, and how long does each wait take. Pure functions over text so they can be tested without files.
internal static class TraceSummary {
 // A run counts as a login attempt only if it touched the browser/portal flow; opening settings does not.
 static readonly string[] AttemptEvents={"EDGE_LAUNCH_REQUEST","EXISTING_SESSION","CERTIFICATE_OPEN_REQUEST","AUTHENTICATION_BEGIN"};

 static string Text(Dictionary<string,object> e,string key){object v;return e.TryGetValue(key,out v)&&v!=null?v as string:null;}
 static long Number(Dictionary<string,object> e,string key){
  object v;if(!e.TryGetValue(key,out v)||v==null)return 0;
  try{return Convert.ToInt64(v);}catch(FormatException){return 0;}catch(InvalidCastException){return 0;}catch(OverflowException){return 0;}
 }

 internal static RunDigest Digest(string file,IEnumerable<string> lines){
  var digest=new RunDigest{File=file};
  var serializer=new JavaScriptSerializer();
  var waitStart=new Dictionary<string,long>();
  bool failed=false,completed=false,stopped=false;
  long failedAt=0,completedAt=0,lastAt=0;
  foreach(var line in lines){
   if(String.IsNullOrWhiteSpace(line))continue;
   Dictionary<string,object> e=null;
   try{e=serializer.DeserializeObject(line) as Dictionary<string,object>;}
   catch(ArgumentException){}
   catch(InvalidOperationException){}
   if(e==null){digest.SkippedLines++;continue;}
   string code=Text(e,"code")??"",stage=Text(e,"stage")??"NONE";long elapsed=Number(e,"elapsed_ms");
   if(digest.Mode==null){digest.Mode=Text(e,"mode");digest.Version=Text(e,"version");digest.Started=Text(e,"time");}
   if(elapsed>lastAt)lastAt=elapsed;
   if(AttemptEvents.Contains(code))digest.Attempt=true;
   switch(code){
    case "WAIT_BEGIN":waitStart[stage]=elapsed;break;
    case "WAIT_READY":{long begun;if(waitStart.TryGetValue(stage,out begun)&&elapsed>=begun)digest.StageWaitMs[stage]=elapsed-begun;break;}
    case "ERROR":case "WAIT_TIMEOUT":case "WAIT_FAILED":
     if(!failed){failed=true;failedAt=elapsed;digest.FailureStage=stage;digest.FailureType=code=="WAIT_TIMEOUT"?"TIMEOUT":(Text(e,"error_type")??"UNKNOWN");}
     break;
    case "SYSTEM_MENU_REQUESTS_COMPLETE":case "AUTO_WORK_SUCCESS":if(!completed){completed=true;completedAt=elapsed;}break;
    case "AUTO_WORK_STOPPED":case "CANCEL_REQUESTED":stopped=true;break;
   }
  }
  digest.Outcome=failed?RunOutcome.Failed:completed?RunOutcome.Success:stopped?RunOutcome.Stopped:RunOutcome.Incomplete;
  // Time until the outcome, not the process lifetime: the helper window may stay open for hours afterwards.
  digest.DurationMs=failed?failedAt:completed?completedAt:lastAt;
  return digest;
 }

 static long Median(List<long> values){
  var sorted=values.OrderBy(v=>v).ToList();int n=sorted.Count;
  return n%2==1?sorted[n/2]:(sorted[n/2-1]+sorted[n/2])/2;
 }

 static string OutcomeLabel(RunOutcome outcome){
  switch(outcome){
   case RunOutcome.Success:return "성공";
   case RunOutcome.Failed:return "실패";
   case RunOutcome.Stopped:return "중단";
   default:return "미완료";
  }
 }

 internal static string Report(IList<RunDigest> runs,DateTime now){
  var sb=new StringBuilder();
  var attempts=runs.Where(r=>r.Attempt).ToList();
  sb.AppendLine("업무 시작 도우미 진단 요약 · "+now.ToString("yyyy-MM-dd HH:mm"));
  sb.AppendLine("분석한 실행 기록 "+runs.Count+"개 · 그중 로그인 시도 "+attempts.Count+"회");
  if(attempts.Count>0){
   int ok=attempts.Count(r=>r.Outcome==RunOutcome.Success),bad=attempts.Count(r=>r.Outcome==RunOutcome.Failed);
   int stop=attempts.Count(r=>r.Outcome==RunOutcome.Stopped),open=attempts.Count(r=>r.Outcome==RunOutcome.Incomplete);
   sb.AppendLine("성공 "+ok+" · 실패 "+bad+" · 중단 "+stop+" · 미완료 "+open+" (성공률 "+(100*ok/attempts.Count)+"%)");
   var failures=attempts.Where(r=>r.Outcome==RunOutcome.Failed).GroupBy(r=>r.FailureStage+" / "+r.FailureType).OrderByDescending(g=>g.Count()).ToList();
   if(failures.Count>0){
    sb.AppendLine();sb.AppendLine("실패가 난 단계 (단계 / 오류 유형)");
    foreach(var g in failures)sb.AppendLine("  "+g.Key+" : "+g.Count()+"회");
   }
   var waits=new Dictionary<string,List<long>>();
   foreach(var r in attempts.Where(r=>r.Outcome==RunOutcome.Success))
    foreach(var pair in r.StageWaitMs){List<long> list;if(!waits.TryGetValue(pair.Key,out list))waits[pair.Key]=list=new List<long>();list.Add(pair.Value);}
   if(waits.Count>0){
    sb.AppendLine();sb.AppendLine("성공한 실행의 단계별 대기 시간(밀리초): 중앙값 / 최대 / 횟수");
    foreach(var pair in waits.OrderBy(p=>p.Key,StringComparer.Ordinal))sb.AppendLine("  "+pair.Key+" : "+Median(pair.Value)+" / "+pair.Value.Max()+" / "+pair.Value.Count);
   }
   sb.AppendLine();sb.AppendLine("최근 로그인 시도 (최신순, 최대 10건)");
   foreach(var r in attempts.OrderByDescending(r=>r.File,StringComparer.Ordinal).Take(10)){
    string started=(r.Started??"").Replace('T',' ');if(started.Length>19)started=started.Substring(0,19);
    sb.AppendLine("  "+started+" · "+(r.Mode??"?")+" · "+OutcomeLabel(r.Outcome)+" · 결과까지 "+r.DurationMs+"ms"+(r.Outcome==RunOutcome.Failed?" · "+r.FailureStage+"/"+r.FailureType:"")+(r.Version!=null?" · v"+r.Version:""));
   }
  }
  int skipped=runs.Sum(r=>r.SkippedLines);
  if(skipped>0){sb.AppendLine();sb.AppendLine("읽을 수 없는 로그 줄 "+skipped+"개는 건너뛰었습니다.");}
  return sb.ToString();
 }
}

// Builds one shareable zip. Allow-list only: structured run traces, the status/event code files, a
// computed summary and non-identifying environment facts. Settings, credentials and certificate
// details are never read. Every text is also scanned for local identity strings before it is added.
internal static class DiagnosticsBundle {
 internal const int DefaultMaxRuns=30;
 const long MaxFileBytes=2*1024*1024;

 internal static bool ContainsIdentity(string text,IEnumerable<string> identities){
  foreach(var identity in identities){
   if(String.IsNullOrEmpty(identity)||identity.Length<4)continue;
   // Traces are JSON, which doubles backslashes, so check both spellings of a path.
   if(text.IndexOf(identity,StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf(identity.Replace("\\","\\\\"),StringComparison.OrdinalIgnoreCase)>=0)return true;
  }
  return false;
 }

 static string ReadShared(string path){
  using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
  using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();
 }

 static void AddText(ZipArchive zip,string name,string text){
  var entry=zip.CreateEntry(name,CompressionLevel.Optimal);
  using(var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false)))writer.Write(text);
 }

 // Returns the summary text that was written into the bundle.
 internal static string Create(string root,string zipPath,object environment,IEnumerable<string> identities,DateTime now,int maxRuns){
  identities=identities.ToList();
  var included=new List<KeyValuePair<string,string>>();
  var excluded=new List<string>();
  var digests=new List<RunDigest>();
  string logs=Path.Combine(root,"logs");
  var traces=Directory.Exists(logs)?Directory.GetFiles(logs,"startup-*.jsonl").OrderByDescending(f=>Path.GetFileName(f),StringComparer.Ordinal).Take(maxRuns).ToList():new List<string>();
  foreach(var path in traces){
   string name=Path.GetFileName(path);
   try{
    if(new FileInfo(path).Length>MaxFileBytes){excluded.Add(name+" (너무 커서 제외)");continue;}
    string text=ReadShared(path);
    if(ContainsIdentity(text,identities)){excluded.Add(name+" (개인 경로가 들어 있어 제외)");continue;}
    included.Add(new KeyValuePair<string,string>("logs/"+name,text));
    digests.Add(TraceSummary.Digest(name,text.Split('\n').Select(l=>l.TrimEnd('\r'))));
   }catch(IOException ex){excluded.Add(name+" (읽을 수 없음: "+ex.GetType().Name+")");}
   catch(UnauthorizedAccessException ex){excluded.Add(name+" (읽을 수 없음: "+ex.GetType().Name+")");}
  }
  // Fixed names only: codes and timestamps, written by DiagnosticLog.
  foreach(var name in new[]{"events.log","status.json"}){
   string path=Path.Combine(root,name);if(!File.Exists(path))continue;
   try{
    if(new FileInfo(path).Length>MaxFileBytes){excluded.Add(name+" (너무 커서 제외)");continue;}
    string text=ReadShared(path);
    if(ContainsIdentity(text,identities)){excluded.Add(name+" (개인 경로가 들어 있어 제외)");continue;}
    included.Add(new KeyValuePair<string,string>(name,text));
   }catch(IOException ex){excluded.Add(name+" (읽을 수 없음: "+ex.GetType().Name+")");}
   catch(UnauthorizedAccessException ex){excluded.Add(name+" (읽을 수 없음: "+ex.GetType().Name+")");}
  }
  var summary=new StringBuilder(TraceSummary.Report(digests,now));
  summary.AppendLine();
  summary.AppendLine("이 파일에 들어 있는 것: 실행 기록(단계·시간·오류 유형), 이벤트 코드, 환경 정보.");
  summary.AppendLine("들어 있지 않은 것: 비밀번호, 인증서 이름·위치, 설정 파일, 화면 내용, 주소.");
  if(excluded.Count>0){summary.AppendLine();summary.AppendLine("제외된 파일");foreach(var item in excluded)summary.AppendLine("  "+item);}
  string environmentJson=new JavaScriptSerializer().Serialize(environment);
  string summaryText=summary.ToString();
  if(ContainsIdentity(summaryText,identities))summaryText="[개인 경로가 들어 있어 요약을 제외했습니다]";
  if(ContainsIdentity(environmentJson,identities))environmentJson="{\"excluded\":\"개인 경로가 들어 있어 제외\"}";

  string temp=zipPath+".tmp";
  if(File.Exists(temp))File.Delete(temp);
  try{
   using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create)){
    AddText(zip,"summary.txt",summaryText);
    AddText(zip,"environment.json",environmentJson);
    foreach(var item in included)AddText(zip,item.Key,item.Value);
   }
   if(File.Exists(zipPath))throw new IOException("출력 파일이 이미 있습니다.");
   File.Move(temp,zipPath);
  }finally{if(File.Exists(temp))File.Delete(temp);}
  return summaryText;
 }
}
