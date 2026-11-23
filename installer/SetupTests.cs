using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TeacherWorkStartSetup {
 internal static class SetupTests {
  static int count;static string root;static readonly byte[] ApplicationBytes=Encoding.UTF8.GetBytes("TEST_ONLY_NOT_EXECUTABLE"),ConfigurationBytes=Encoding.UTF8.GetBytes("<configuration/>");
  static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
  sealed class Platform:ISetupPlatform {
   internal string Root;internal bool Busy,InPackage,CorruptInstall,ChangeSettings,Shortcut=true,RealChild;internal int ExitCode,Installs,Opens;internal string Stage;
   public bool HelperRunning {get{return Busy;}}public bool Packaged {get{return InPackage;}}
   public int InstallOnly(string exe){Installs++;Stage=Path.GetDirectoryName(exe);if(RealChild)return new SetupPlatform().InstallOnly(exe);if(ExitCode!=0)return ExitCode;Directory.CreateDirectory(Root);File.Copy(exe,Path.Combine(Root,"TeacherWorkStart.exe"),true);File.Copy(exe+".config",Path.Combine(Root,"TeacherWorkStart.exe.config"),true);if(CorruptInstall)File.AppendAllText(Path.Combine(Root,"TeacherWorkStart.exe"),"BROKEN");if(ChangeSettings)File.WriteAllText(Path.Combine(Root,"settings.json"),"UNEXPECTED_CHANGE");return 0;}
   public bool ShortcutMatches(string exe){return Shortcut;}public void OpenHelper(string exe){Opens++;}
  }
  static SetupPayload Payload(Func<string,Stream> stream=null){return new SetupPayload("0.4.12.0",SetupPayload.Hash(ApplicationBytes),SetupPayload.Hash(ConfigurationBytes),stream??(name=>new MemoryStream(name=="Application"?ApplicationBytes:ConfigurationBytes)));}
  static SetupCore Core(string name,Platform platform,SetupPayload payload=null,Version existing=null){string path=Path.Combine(root,name);platform.Root=path;return new SetupCore(path,root,payload??Payload(),platform,p=>existing??new Version("0.4.12.0"));}
  static SetupProblem? Refused(Action action){try{action();return null;}catch(SetupFailure ex){return ex.Problem;}}
  static void Pump(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(10);while(!ready()&&DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(5);}if(!ready())throw new Exception("UI completion deadline");}
  static void CoreChecks(){
   var platform=new Platform();var core=Core("fresh",platform);var result=core.Install();
   Check(platform.Installs==1&&platform.Opens==0&&result.ShortcutReady&&result.SettingsPreserved,"fresh install dispatches once and never opens helper automatically");
   Check(File.ReadAllBytes(core.Executable).SequenceEqual(ApplicationBytes)&&File.ReadAllBytes(core.Executable+".config").SequenceEqual(ConfigurationBytes),"both verified payload files arrive at the intended test installation");
   Check(!Directory.Exists(platform.Stage)&&result.TemporaryFilesRemoved,"extracted executable and sidecar are removed after child exits");
   core.Open();Check(platform.Opens==1,"helper opens only after explicit completion action");
   Directory.CreateDirectory(platform.Root);File.WriteAllText(Path.Combine(platform.Root,"settings.json"),"PRIVATE_SETTINGS_TEST_SENTINEL");string before=SetupPayload.FileHash(Path.Combine(platform.Root,"settings.json"));core.Install();
   Check(before==SetupPayload.FileHash(Path.Combine(platform.Root,"settings.json")),"repair installation preserves existing settings bytes");
   var busy=new Platform{Busy=true};Check(Refused(()=>Core("busy",busy).Install())==SetupProblem.Busy&&busy.Installs==0&&!Directory.Exists(busy.Root),"running helper blocks before staging or install dispatch");
   var packaged=new Platform{InPackage=true};Check(Refused(()=>Core("package",packaged).Install())==SetupProblem.UnknownInstallation&&packaged.Installs==0,"packaged invocation does not silently install into redirected app data");
   var newer=new Platform();var downgrade=Core("newer",newer,existing:new Version("0.4.13.0"));Directory.CreateDirectory(newer.Root);File.WriteAllText(downgrade.Executable,"NEWER_KEEP");
   Check(Refused(()=>downgrade.Install())==SetupProblem.NewerVersion&&newer.Installs==0&&File.ReadAllText(downgrade.Executable)=="NEWER_KEEP","newer installed version is preserved without downgrade");
   var unknown=new Platform();var uncertain=new SetupCore(Path.Combine(root,"unknown"),root,Payload(),unknown,p=>null);unknown.Root=uncertain.Root;Directory.CreateDirectory(unknown.Root);File.WriteAllText(uncertain.Executable,"UNKNOWN_KEEP");
   Check(Refused(()=>uncertain.Install())==SetupProblem.UnknownInstallation&&unknown.Installs==0,"unidentified existing executable is never overwritten");
   var corrupt=new Platform();Check(Refused(()=>Core("corrupt",corrupt,Payload(n=>new MemoryStream(new byte[]{1,2,3}))).Install())==SetupProblem.Payload&&corrupt.Installs==0,"corrupt embedded payload never executes");
   var missing=new Platform();Check(Refused(()=>Core("missing",missing,Payload(n=>n=="Application"?new MemoryStream(ApplicationBytes):null)).Install())==SetupProblem.Payload&&missing.Installs==0,"missing sidecar never dispatches partial installation");
   var failed=new Platform{ExitCode=2};Check(Refused(()=>Core("child-error",failed).Install())==SetupProblem.ChildFailed&&failed.Installs==1&&failed.Opens==0&&!Directory.Exists(failed.Stage),"failed child is not retried or treated as success and staging is cleaned");
   var mismatch=new Platform{CorruptInstall=true};Check(Refused(()=>Core("verify",mismatch).Install())==SetupProblem.Verification&&mismatch.Opens==0,"zero child exit alone cannot fabricate successful installation");
   var settings=new Platform{ChangeSettings=true};Check(Refused(()=>Core("changed",settings).Install())==SetupProblem.SettingsChanged&&settings.Opens==0,"unexpected setting changes are surfaced without opening helper");
   var noShortcut=new Platform{Shortcut=false};var without=Core("shortcut",noShortcut).Install();Check(!without.ShortcutReady&&without.SettingsPreserved,"missing or unrelated desktop shortcut does not fabricate shortcut success");
   var stage=new TemporaryPayload(root);string extra=Path.Combine(stage.Folder,"UNRELATED_KEEP");File.WriteAllText(extra,"KEEP");stage.Dispose();Check(!stage.Cleaned&&File.Exists(extra),"cleanup refuses recursive deletion of unexpected files");File.Delete(extra);stage.Dispose();Check(stage.Cleaned,"known empty staging directory can be cleaned normally");
  }
  static void UiChecks(){
   var platform=new Platform();using(var window=new SetupWindow(Core("ui",platform),"0.4.12")){
    window.ShowInTaskbar=false;window.Opacity=0;window.Show();Check(platform.Installs==0&&platform.Opens==0&&!window.Complete,"opening setup window does not install or access helper automatically");
    window.Primary.PerformClick();Pump(()=>!window.Busy);Check(window.Complete&&window.Primary.Text=="도우미 열기"&&platform.Installs==1&&platform.Opens==0,"completed UI offers explicit next step without automatic launch");window.Primary.PerformClick();Check(platform.Opens==1,"completion button opens helper exactly once");
   }
   var failing=new Platform{ExitCode=1};using(var window=new SetupWindow(Core("ui-error",failing),"0.4.12")){
    window.ShowInTaskbar=false;window.Opacity=0;window.Show();window.Primary.PerformClick();Pump(()=>!window.Busy);Check(!window.Complete&&window.Primary.Enabled&&failing.Installs==1&&failing.Opens==0,"UI failure remains retryable by user and never advances to successful next step");window.Close();
   }
  }
  static void RealChild(string executable){
   string target=Path.Combine(root,"real-child"),previous=Environment.GetEnvironmentVariable("TEACHERWORKSTART_SETUP_TEST_ROOT");Directory.CreateDirectory(target);
   try{Environment.SetEnvironmentVariable("TEACHERWORKSTART_SETUP_TEST_ROOT",target);var exe=File.ReadAllBytes(executable);var config=File.ReadAllBytes(executable+".config");var payload=new SetupPayload("0.4.12.0",SetupPayload.Hash(exe),SetupPayload.Hash(config),name=>new MemoryStream(name=="Application"?exe:config));var platform=new Platform{Root=target,RealChild=true};var core=new SetupCore(target,root,payload,platform);var result=core.Install();
    Check(platform.Installs==1&&File.ReadAllText(Path.Combine(target,"arguments.txt"))=="--install-only"&&result.SettingsPreserved,"real Windows child receives only install-only argument and completes verified temporary install");
    Check(!Directory.Exists(platform.Stage)&&platform.Opens==0&&!File.Exists(Path.Combine(target,"settings.json")),"real child test leaves no staging settings or automatic helper launch");
   }finally{Environment.SetEnvironmentVariable("TEACHERWORKSTART_SETUP_TEST_ROOT",previous);}
  }
  [STAThread]static int Main(string[] args){
   string temp=Path.GetFullPath(Path.GetTempPath());root=Path.GetFullPath(Path.Combine(temp,"TeacherWorkStart-Setup-Tests-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
   try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);CoreChecks();UiChecks();RealChild(args[0]);Console.WriteLine("PASS "+count+" installer checks; no user settings, credentials, browser login or startup writes");return 0;}
   catch(Exception ex){Console.WriteLine("FAIL "+ex.GetType().Name+": "+ex.Message);return 1;}
   finally{if(Path.GetDirectoryName(root).TrimEnd(Path.DirectorySeparatorChar)==temp.TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(root).StartsWith("TeacherWorkStart-Setup-Tests-"))Directory.Delete(root,true);}
  }
 }
}
