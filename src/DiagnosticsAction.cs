using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

// Entry points for "create a diagnostics file": tray menu item and --diagnostics-bundle [path.zip].
internal static class DiagnosticsAction {
 const string Title="진단 파일";

 internal static string DefaultPath(DateTime now){
  string folder=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
  if(String.IsNullOrEmpty(folder)||!Directory.Exists(folder))folder=Settings.Root;
  return Path.Combine(folder,"TeacherWorkStart-진단-"+now.ToString("yyyyMMdd-HHmmss")+".zip");
 }

 // Strings that identify this person or machine and therefore must never appear in a shared file.
 internal static string[] Identities(){
  return new[]{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),WindowsIdentity.GetCurrent().User.Value,Environment.UserName.Length>=4?Environment.UserName:null,Environment.MachineName.Length>=4?Environment.MachineName:null};
 }

 static string WindowsBuild(){
  try{
   using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){
    if(key==null)return "unknown";
    return Convert.ToString(key.GetValue("DisplayVersion")??"")+" build "+Convert.ToString(key.GetValue("CurrentBuild")??"?");
   }
  }catch(Exception ex){return "unavailable:"+ex.GetType().Name;} // best-effort fact; must not block the file
 }

 static string EdgeVersion(){
  string exe=Portal.FindEdgeExecutable();
  if(exe==null)return "not-found";
  try{return FileVersionInfo.GetVersionInfo(exe).FileVersion;}catch(Exception ex){return "unavailable:"+ex.GetType().Name;}
 }

 // Booleans only: enough to see why an automatic run would be blocked, nothing that identifies a person.
 static object State(){
  try{
   var s=Settings.Load();
   return new{registered=s.CertUser!=""&&Vault.Exists(s.Target),verified=s.Verified,pendingAttempt=s.PendingAttempt,storageConfigured=s.CertStorageConfigured,startAtLogon=Autostart.IsRegistered,fastStartup=Autostart.Controller.IsFast,legacyStartup=StartupRegistration.LegacyPresent};
  }catch(Exception ex){return new{unavailable=ex.GetType().Name};}
 }

 internal static object EnvironmentFacts(){return EnvironmentFacts(State);}

 // The state reader is injectable so tests never touch the real settings, credential store or scheduler.
 internal static object EnvironmentFacts(Func<object> state){
  var config=PortalConfig.Current;var defaults=new PortalConfig();
  return new{
   app=new{version=Assembly.GetExecutingAssembly().GetName().Version.ToString()},
   os=new{windows=WindowsBuild(),is64Bit=Environment.Is64BitOperatingSystem,uiCulture=CultureInfo.CurrentUICulture.Name},
   runtime=Environment.Version.ToString(),
   edge=EdgeVersion(),
   config=new{
    fileExists=File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,PortalConfig.FileName)),
    problems=config.Problems.ToArray(),
    hostIsDefault=config.Host==defaults.Host,
    pathsAreDefault=config.LoginPath==defaults.LoginPath&&config.MainPath==defaults.MainPath,
    browserProcessIsDefault=config.BrowserProcess==defaults.BrowserProcess,
    timingsAreDefault=config.PollIntervalMs==defaults.PollIntervalMs&&config.TabSettleMs==defaults.TabSettleMs
   },
   state=state()
  };
 }

 internal static string Create(DateTime now,string path){
  string target=path??DefaultPath(now);
  string folder=Path.GetDirectoryName(Path.GetFullPath(target));
  if(!Directory.Exists(folder))Directory.CreateDirectory(folder);
  DiagnosticsBundle.Create(Settings.Root,target,EnvironmentFacts(),Identities(),now,DiagnosticsBundle.DefaultMaxRuns);
  return target;
 }

 internal static void ShowFromMenu(){
  try{
   string path=Create(DateTime.Now,null);
   MessageBox.Show("진단 파일을 만들었습니다.\n\n"+Path.GetFileName(path)+"\n\n문제가 생겼을 때 이 파일을 전달해 주세요. 실행 기록과 단계별 시간만 들어 있고 비밀번호·인증서 정보·설정은 들어 있지 않습니다.",Title);
   try{Process.Start("explorer.exe","/select,\""+path+"\"");}catch(Exception){} // convenience only
  }catch(IOException ex){Fail(ex);}
  catch(UnauthorizedAccessException ex){Fail(ex);}
 }

 static void Fail(Exception ex){MessageBox.Show("진단 파일을 만들지 못했습니다. 오류 유형: "+ex.GetType().Name,Title);}

 internal static int RunFromCommandLine(string[] args){
  int at=Array.IndexOf(args,"--diagnostics-bundle");
  string path=at+1<args.Length&&!args[at+1].StartsWith("--")?args[at+1]:null;
  if(path!=null&&(!path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||File.Exists(path))){
   Report("출력 경로는 아직 없는 .zip 파일이어야 합니다.");return 2;
  }
  try{Report("진단 파일: "+Create(DateTime.Now,path));return 0;}
  catch(IOException ex){Report("진단 파일을 만들지 못했습니다. 오류 유형: "+ex.GetType().Name);return 1;}
  catch(UnauthorizedAccessException ex){Report("진단 파일을 만들지 못했습니다. 오류 유형: "+ex.GetType().Name);return 1;}
 }

 static void Report(string message){
  #if CONSOLE
  Console.WriteLine(message);
  #else
  MessageBox.Show(message,Title);
  #endif
 }
}
