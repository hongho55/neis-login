using System;
using System.IO;
using System.Xml;
using System.Security;
using System.Security.Principal;
using System.Runtime.InteropServices;

internal sealed class LogonTaskRecord {internal string Xml;internal bool Enabled;internal int RunLevel,LogonType=3;}
internal interface ILogonTaskStore {
 LogonTaskRecord Read();void CreateDisabled(string xml);void Update(string xml);void Enable(bool enabled);void Remove();
}

// Uses the Windows scheduler COM API in this user token. No shell, password or elevation.
internal sealed class WindowsLogonTaskStore:ILogonTaskStore {
 readonly string name;internal WindowsLogonTaskStore(string taskName){name=taskName;}
 static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
 T WithFolder<T>(Func<dynamic,T> action){
  object service=null,folder=null;
  try{service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));((dynamic)service).Connect();folder=((dynamic)service).GetFolder("\\");return action(folder);}
  finally{Release(folder);Release(service);}
 }
 public LogonTaskRecord Read(){return WithFolder<LogonTaskRecord>(folder=>{
  object task=null,definition=null,principal=null;try{task=folder.GetTask(name);definition=((dynamic)task).Definition;principal=((dynamic)definition).Principal;return new LogonTaskRecord{Xml=(string)((dynamic)task).Xml,Enabled=(bool)((dynamic)task).Enabled,RunLevel=(int)((dynamic)principal).RunLevel,LogonType=(int)((dynamic)principal).LogonType};}
  catch(FileNotFoundException){return null;}
  catch(COMException e){if(e.ErrorCode==unchecked((int)0x80070002))return null;throw;}finally{Release(principal);Release(definition);Release(task);}
 });}
 public void CreateDisabled(string xml){WithFolder<int>(folder=>{object task=null;try{task=folder.RegisterTask(name,xml,2,WindowsIdentity.GetCurrent().User.Value,null,3,null);return 0;}finally{Release(task);}});}
 public void Update(string xml){WithFolder<int>(folder=>{object task=null;try{task=folder.RegisterTask(name,xml,4,WindowsIdentity.GetCurrent().User.Value,null,3,null);return 0;}finally{Release(task);}});}
 public void Enable(bool enabled){WithFolder<int>(folder=>{object task=null;try{task=folder.GetTask(name);((dynamic)task).Enabled=enabled;return 0;}finally{Release(task);}});}
 public void Remove(){WithFolder<int>(folder=>{folder.DeleteTask(name,0);return 0;});}
}

internal static class LogonTaskPolicy {
 internal const string Marker="TeacherWorkStart.FastStartup.v1";
 internal const string Namespace="http://schemas.microsoft.com/windows/2004/02/mit/task";
 internal const string Delay="PT10S";
 internal static string Build(string sid,string executable){
  string user=SecurityElement.Escape(sid),exe=SecurityElement.Escape(executable),dir=SecurityElement.Escape(Path.GetDirectoryName(executable));
  return "<Task version=\"1.2\" xmlns=\""+Namespace+"\"><RegistrationInfo><Description>업무 시작 도우미 · 현재 사용자 로그온 · --auto</Description><Source>"+Marker+"</Source></RegistrationInfo>"+
   "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+user+"</UserId><Delay>"+Delay+"</Delay></LogonTrigger></Triggers>"+
   "<Principals><Principal id=\"CurrentUser\"><UserId>"+user+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>"+
   "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"+
   "<AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>"+
   "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>false</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>"+
   "<Actions Context=\"CurrentUser\"><Exec><Command>"+exe+"</Command><Arguments>--auto</Arguments><WorkingDirectory>"+dir+"</WorkingDirectory></Exec></Actions></Task>";
 }
 static XmlDocument Parse(string xml){var doc=new XmlDocument{XmlResolver=null};using(var input=new StringReader(xml))using(var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc.Load(reader);return doc;}
 static XmlNamespaceManager Names(XmlDocument doc){var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("t",Namespace);return ns;}
 static string Value(XmlDocument doc,XmlNamespaceManager ns,string path){string query="/t:Task";foreach(string part in path.Split('/'))query+="/"+(part.StartsWith("t:")?part:"t:"+part);var node=doc.SelectSingleNode(query,ns);return node==null?null:node.InnerText;}
 // RegisteredTask.Xml omits defaults. Read their defined meaning, and verify
 // effective RunLevel/LogonType separately from the live COM principal.
 static string Default(string path){
  switch(path){
   case "Triggers/t:LogonTrigger/t:Enabled":case "Settings/t:AllowStartOnDemand":return "true";
   case "Principals/t:Principal/t:RunLevel":return "LeastPrivilege";
   case "Settings/t:MultipleInstancesPolicy":return "IgnoreNew";
   case "Settings/t:StartWhenAvailable":case "Settings/t:RunOnlyIfNetworkAvailable":case "Settings/t:RunOnlyIfIdle":case "Settings/t:WakeToRun":case "Settings/t:Hidden":return "false";
   default:return null;
  }
 }
 internal static bool SameUser(string value,string sid){
  if(String.IsNullOrWhiteSpace(value))return false;if(value.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))return String.Equals(value,sid,StringComparison.OrdinalIgnoreCase);
  try{return ((SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier))).Value==sid;}
  catch(IdentityNotMappedException){return false;}catch(ArgumentException){return false;}catch(SecurityException){return false;}
 }
 internal static bool Owned(string xml,string sid,string executable){
  try{var doc=Parse(xml);var ns=Names(doc);return Value(doc,ns,"RegistrationInfo/Source")==Marker&&
   doc.SelectNodes("/t:Task/t:Principals/*",ns).Count==1&&SameUser(Value(doc,ns,"Principals/t:Principal/t:UserId"),sid)&&
   doc.SelectNodes("/t:Task/t:Actions/*",ns).Count==1&&String.Equals(Value(doc,ns,"Actions/t:Exec/t:Command"),executable,StringComparison.OrdinalIgnoreCase)&&Value(doc,ns,"Actions/t:Exec/t:Arguments")=="--auto";
  }catch(XmlException){return false;}
 }
 internal static string WithDelay(string xml){
  var doc=Parse(xml);var ns=Names(doc);var trigger=doc.SelectSingleNode("/t:Task/t:Triggers/t:LogonTrigger",ns);var delay=trigger.SelectSingleNode("t:Delay",ns);
  if(delay==null){delay=doc.CreateElement("Delay",Namespace);trigger.AppendChild(delay);}delay.InnerText=Delay;return doc.OuterXml;
 }
 internal static bool Valid(string xml,string sid,string executable,bool allowImmediate=false){
  if(!Owned(xml,sid,executable))return false;
  try{
  var doc=Parse(xml);var ns=Names(doc);Func<string,string> value=p=>Value(doc,ns,p)??Default(p);
  string delay=value("Triggers/t:LogonTrigger/t:Delay");
  TimeSpan duration=delay==null?TimeSpan.Zero:XmlConvert.ToTimeSpan(delay);
  return doc.SelectNodes("/t:Task/t:Triggers/*",ns).Count==1&&SameUser(value("Triggers/t:LogonTrigger/t:UserId"),sid)&&value("Triggers/t:LogonTrigger/t:Enabled")=="true"&&(duration==TimeSpan.FromSeconds(10)||(allowImmediate&&duration==TimeSpan.Zero))&&
   value("Principals/t:Principal/t:LogonType")=="InteractiveToken"&&value("Principals/t:Principal/t:RunLevel")=="LeastPrivilege"&&
   value("Settings/t:MultipleInstancesPolicy")=="IgnoreNew"&&value("Settings/t:DisallowStartIfOnBatteries")=="false"&&value("Settings/t:StopIfGoingOnBatteries")=="false"&&
   value("Settings/t:StartWhenAvailable")=="false"&&value("Settings/t:RunOnlyIfNetworkAvailable")=="false"&&value("Settings/t:RunOnlyIfIdle")=="false"&&value("Settings/t:WakeToRun")=="false"&&
   value("Settings/t:AllowHardTerminate")=="false"&&value("Settings/t:AllowStartOnDemand")=="true"&&value("Settings/t:Hidden")=="false"&&value("Settings/t:ExecutionTimeLimit")=="PT0S"&&
   doc.SelectNodes("/t:Task/t:Settings/t:RestartOnFailure",ns).Count==0&&doc.SelectNodes("/t:Task/t:Triggers/t:LogonTrigger/t:Repetition|/t:Task/t:Triggers/t:LogonTrigger/t:StartBoundary|/t:Task/t:Triggers/t:LogonTrigger/t:EndBoundary",ns).Count==0&&String.Equals(value("Actions/t:Exec/t:WorkingDirectory"),Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase);
  }catch(FormatException){return false;}
 }
}

internal sealed class StartupBackup {
 readonly string active,backup,executable;
 internal StartupBackup(string source,string saved,string exe){active=source;backup=saved;executable=exe;}
 internal bool Exists {get{return File.Exists(active);}}
 void Check(string path){if(!File.Exists(path))return;if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||!StartupRegistration.IsRegisteredAt(path,executable))throw new SafeFailure("자동 시작 바로가기의 대상이 달라 변경하지 않았습니다.");}
 static bool Same(string a,string b){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(a)))==BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(b)));}
 internal void Preflight(){Check(active);Check(backup);if(Exists&&File.Exists(backup)&&!Same(active,backup))throw new SafeFailure("이전 백업과 현재 바로가기가 다릅니다. 두 파일을 보존하고 전환을 중단했습니다.");}
 internal bool Suspend(){
  Preflight();if(!Exists)return false;Directory.CreateDirectory(Path.GetDirectoryName(backup));
  if(!File.Exists(backup))File.Copy(active,backup,false);
  Check(backup);if(!Same(active,backup))throw new SafeFailure("자동 시작 백업을 확인하지 못해 기존 등록을 유지했습니다.");
  StartupRegistration.RemoveAt(active,executable);return true;
 }
 internal void Restore(){
  Check(active);Check(backup);if(Exists)return;
  if(File.Exists(backup)){Directory.CreateDirectory(Path.GetDirectoryName(active));InstallFiles.CopyAtomic(backup,active);Check(active);if(!Same(active,backup))throw new SafeFailure("기존 자동 시작 복원 결과를 확인하지 못했습니다.");}
  else StartupRegistration.EnableAt(active,executable);
 }
 internal void Remove(){StartupRegistration.RemoveAt(active,executable);}
}

internal sealed class FastStartupController {
 readonly ILogonTaskStore tasks;readonly StartupBackup startup;readonly string sid,exe;readonly Func<bool> legacy;readonly Action removeLegacy;
 internal FastStartupController(ILogonTaskStore store,StartupBackup source,string user,string executable,Func<bool> hasLegacy,Action clearLegacy=null){tasks=store;startup=source;sid=user;exe=executable;legacy=hasLegacy;removeLegacy=clearLegacy??StartupRegistration.RemoveLegacy;}
 LogonTaskRecord Checked(){var task=tasks.Read();if(task!=null&&!LogonTaskPolicy.Owned(task.Xml,sid,exe))throw new SafeFailure("같은 이름의 다른 예약 작업이 있어 변경하지 않았습니다.");return task;}
 bool Valid(LogonTaskRecord task,bool allowImmediate=false){return task!=null&&task.RunLevel==0&&task.LogonType==3&&LogonTaskPolicy.Valid(task.Xml,sid,exe,allowImmediate);}
 internal bool IsFast {get{var task=Checked();return Valid(task,true)&&task.Enabled;}}
 internal bool NeedsDelayUpdate {get{var task=Checked();return Valid(task,true)&&!Valid(task);}}
 internal bool HasTask {get{return tasks.Read()!=null;}}
 void UpdateDelay(LogonTaskRecord original){
  try{
   tasks.Update(LogonTaskPolicy.WithDelay(original.Xml));var updated=Checked();
   if(!Valid(updated)||updated.Enabled!=original.Enabled)throw new SafeFailure("예약 작업의 10초 지연 결과를 확인하지 못했습니다.");
  }catch(Exception){
   try{tasks.Update(original.Xml);var restored=Checked();if(!Valid(restored,true)||Valid(restored)||restored.Enabled!=original.Enabled)throw new InvalidOperationException();}
   catch(Exception){throw new SafeFailure("10초 지연 적용과 원래 예약 작업 복원을 확인하지 못했습니다. 추가 등록하지 않았으니 예약 작업 상태를 확인해 주세요.");}
   throw new SafeFailure("10초 지연을 적용하지 못해 원래 예약 작업으로 복원했습니다. 권한을 올리거나 자동 재시도하지 않습니다.");
  }
 }
 internal void Enable(){
  if(legacy())throw new SafeFailure("이전 Run 등록이 남아 있어 전환하지 않았습니다. 기존 자동 시작 등록 상태를 먼저 확인해 주세요.");
  startup.Preflight();var original=Checked();if(original!=null&&!Valid(original,true))throw new SafeFailure("기존 예약 작업 설정이 바뀌어 덮어쓰지 않았습니다. 등록 상태를 확인해 주세요.");
  if(original!=null&&!Valid(original)){UpdateDelay(original);original=Checked();}
  if(original!=null&&original.Enabled&&!startup.Exists)return;
  bool created=original==null,suspended=false;
  try{
   if(created)tasks.CreateDisabled(LogonTaskPolicy.Build(sid,exe));
   var staged=Checked();if(!Valid(staged)||(created&&staged.Enabled))throw new SafeFailure("예약 작업의 사용자·권한·실행 설정을 확인하지 못했습니다. 단계: "+(staged==null?"작업 조회 실패":("설정 검증="+Valid(staged)+", 활성="+staged.Enabled)));
   suspended=startup.Suspend();tasks.Enable(true);
   if(!IsFast||startup.Exists)throw new SafeFailure("자동 시작 전환 결과를 확인하지 못했습니다.");
  }catch(Exception failure){
   try{var task=Checked();if(task!=null){tasks.Enable(false);task=Checked();if(task!=null&&task.Enabled)throw new InvalidOperationException();if(created)tasks.Remove();}if(suspended)startup.Restore();}
   catch(Exception){throw new SafeFailure("전환과 자동 복구를 완료하지 못했습니다. 이중 실행을 피하려고 추가 등록하지 않았습니다. 예약 작업과 시작프로그램 상태를 확인해 주세요.");}
   string detail=failure is SafeFailure?failure.Message:(failure.GetType().Name+" / 0x"+failure.HResult.ToString("X8"));
   throw new SafeFailure("빠른 자동 시작을 등록하지 못했습니다. 기존 Startup 등록은 유지·복원했으며 권한을 올리거나 자동 재시도하지 않습니다.\n원인: "+detail);
  }
 }
 internal void Remove(){var task=Checked();if(task!=null){tasks.Enable(false);var disabled=Checked();if(disabled!=null&&disabled.Enabled)throw new SafeFailure("예약 작업을 끄지 못했습니다.");tasks.Remove();}startup.Remove();removeLegacy();}
}

internal static class Autostart {
 internal static string TaskName {get{return "TeacherWorkStart-Logon-"+WindowsIdentity.GetCurrent().User.Value;}}
 internal static string BackupPath {get{return Path.Combine(Settings.Root,"startup-backup","업무 시작 도우미.lnk");}}
 internal static FastStartupController Controller {get{string sid=WindowsIdentity.GetCurrent().User.Value;return new FastStartupController(new WindowsLogonTaskStore(TaskName),new StartupBackup(StartupRegistration.ShortcutPath,BackupPath,StartupRegistration.Executable),sid,StartupRegistration.Executable,()=>StartupRegistration.LegacyPresent);}}
 internal static bool IsRegistered {get{return Controller.IsFast;}}
}
