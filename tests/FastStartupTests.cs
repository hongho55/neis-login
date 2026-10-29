using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class FastStartupTests {
 const string Sid="S-1-5-21-100-200-300-1001";
 sealed class Store:ILogonTaskStore {
  internal LogonTaskRecord Task;internal bool RejectCreate,RejectEnable,IgnoreDisable;internal Action Created=()=>{};internal readonly List<string> Calls=new List<string>();
  public LogonTaskRecord Read(){return Task;}
  public void CreateDisabled(string xml){Calls.Add("CREATE_DISABLED");if(RejectCreate)throw new UnauthorizedAccessException();Task=new LogonTaskRecord{Xml=xml,Enabled=false};Created();}
  public void Enable(bool on){Calls.Add(on?"ENABLE":"DISABLE");if(on&&RejectEnable)throw new UnauthorizedAccessException();if(on||!IgnoreDisable)Task.Enabled=on;}
  public void Remove(){Calls.Add("REMOVE");Task=null;}
 }
 static bool Refuses(Action operation){try{operation();return false;}catch(SafeFailure){return true;}}
 static StartupBackup Source(string root,string exe){return new StartupBackup(Path.Combine(root,"startup.lnk"),Path.Combine(root,"backup","startup.lnk"),exe);}
 static FastStartupController Controller(Store store,StartupBackup source,string exe,Func<bool> legacy=null){return new FastStartupController(store,source,Sid,exe,legacy??(()=>false),()=>{});}
 internal static void Run(Action<bool,string> check,string testRoot){
  string root=Path.Combine(testRoot,"fast-startup");Directory.CreateDirectory(root);string exe=Path.Combine(root,"teacher & test.exe");File.WriteAllText(exe,"TEST_NOT_EXECUTABLE");
  string xml=LogonTaskPolicy.Build(Sid,exe);
  string compact=xml;foreach(string tag in new[]{"<Enabled>true</Enabled>","<Delay>PT0S</Delay>","<RunLevel>LeastPrivilege</RunLevel>","<StartWhenAvailable>false</StartWhenAvailable>","<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>","<RunOnlyIfIdle>false</RunOnlyIfIdle>","<WakeToRun>false</WakeToRun>","<AllowStartOnDemand>true</AllowStartOnDemand>","<Hidden>false</Hidden>"})compact=compact.Replace(tag,"");
  check(LogonTaskPolicy.Valid(compact,Sid,exe),"fast/Windows registered XML may omit schema defaults without changing meaning");
  using(var current=System.Security.Principal.WindowsIdentity.GetCurrent()){
   string localXml=LogonTaskPolicy.Build(current.User.Value,exe).Replace("<UserId>"+current.User.Value+"</UserId>","<UserId>"+System.Security.SecurityElement.Escape(current.Name)+"</UserId>");
   check(LogonTaskPolicy.Valid(localXml,current.User.Value,exe)&&!LogonTaskPolicy.SameUser(current.Name,Sid),"fast/Windows account-name normalization resolves to exact intended SID");
  }
  check(new WindowsLogonTaskStore("TeacherWorkStart-Regression-Absent-"+Guid.NewGuid().ToString("N")).Read()==null,"fast/real scheduler missing-task read returns absent without writes");
  check(LogonTaskPolicy.Owned(xml,Sid,exe)&&LogonTaskPolicy.Valid(xml,Sid,exe),"fast/policy XML escapes executable and accepts only intended configuration");
  check(!LogonTaskPolicy.Owned(xml,"OTHER",exe)&&!LogonTaskPolicy.Owned(xml,Sid,exe+".other"),"fast/other user and executable are not owned");
  check(!LogonTaskPolicy.Owned(xml.Replace("--auto","--other"),Sid,exe)&&!LogonTaskPolicy.Owned("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///not-read'>]><x>&e;</x>",Sid,exe),"fast/other arguments and DTD rejected");
  foreach(string[] mutation in new[]{new[]{"LeastPrivilege","HighestAvailable"},new[]{"InteractiveToken","Password"},new[]{"IgnoreNew","Parallel"},new[]{"<Delay>PT0S</Delay>","<Delay>PT30S</Delay>"},new[]{"<Delay>PT0S</Delay>","<Delay>BAD</Delay>"},new[]{"<RunOnlyIfIdle>false","<RunOnlyIfIdle>true"},new[]{"<StartWhenAvailable>false","<StartWhenAvailable>true"},new[]{"<DisallowStartIfOnBatteries>false","<DisallowStartIfOnBatteries>true"},new[]{"<RunOnlyIfNetworkAvailable>false","<RunOnlyIfNetworkAvailable>true"},new[]{"</Settings>","<RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>"},new[]{"</LogonTrigger>","<Repetition><Interval>PT1M</Interval></Repetition></LogonTrigger>"},new[]{"</Triggers>","<BootTrigger><Enabled>true</Enabled></BootTrigger></Triggers>"}})
   check(!LogonTaskPolicy.Valid(xml.Replace(mutation[0],mutation[1]),Sid,exe),"fast/reject changed policy "+mutation[0]);

  string active=Path.Combine(root,"startup.lnk"),backup=Path.Combine(root,"backup","startup.lnk");ShortcutFile.Write(active,exe,"--auto","ORIGINAL_TEST");byte[] original=File.ReadAllBytes(active);
  var source=Source(root,exe);var store=new Store();var controller=Controller(store,source,exe);
  store.Created=()=>{if(!File.Exists(active)||store.Task.Enabled)throw new Exception("staging order");};
  controller.Enable();
  check(controller.IsFast&&!File.Exists(active)&&File.Exists(backup)&&Convert.ToBase64String(File.ReadAllBytes(backup))==Convert.ToBase64String(original),"fast/stage disabled task then preserve exact Startup bytes and enable only scheduler");
  int calls=store.Calls.Count;controller.Enable();check(store.Calls.Count==calls,"fast/repeated enable is idempotent");
  controller.Restore();check(store.Task==null&&File.Exists(active)&&Convert.ToBase64String(File.ReadAllBytes(active))==Convert.ToBase64String(original),"fast/restore exact original shortcut after disabling and removing owned task");
  controller.Enable();controller.Remove();check(store.Task==null&&!File.Exists(active)&&File.Exists(backup),"fast/off removes both active modes but retains backup");

  source.Restore();var blocked=new Store{RejectCreate=true};
  check(Refuses(()=>Controller(blocked,source,exe).Enable())&&File.Exists(active)&&blocked.Task==null,"fast/policy denied creation leaves original Startup active");
  var failedEnable=new Store{RejectEnable=true};check(Refuses(()=>Controller(failedEnable,source,exe).Enable())&&File.Exists(active)&&failedEnable.Task==null,"fast/enable failure rolls back task and restores Startup");
  var foreign=new Store{Task=new LogonTaskRecord{Xml=xml.Replace(LogonTaskPolicy.Marker,"OTHER_APP"),Enabled=true}};
  var other=Controller(foreign,source,exe);check(Refuses(()=>other.Enable())&&Refuses(()=>other.Restore())&&Refuses(()=>other.Remove())&&foreign.Calls.Count==0&&File.Exists(active),"fast/unowned task is never overwritten disabled or deleted");
  var altered=new Store{Task=new LogonTaskRecord{Xml=xml.Replace("LeastPrivilege","HighestAvailable"),Enabled=true}};
  check(Refuses(()=>Controller(altered,source,exe).Enable())&&altered.Calls.Count==0&&File.Exists(active),"fast/manually changed task policy not silently overwritten");
  var elevated=new Store{Task=new LogonTaskRecord{Xml=compact,Enabled=true,RunLevel=1}};
  check(!Controller(elevated,source,exe).IsFast&&Refuses(()=>Controller(elevated,source,exe).Enable())&&elevated.Calls.Count==0,"fast/effective COM principal privilege checked even when XML omits RunLevel");
  var legacy=new Store();check(Refuses(()=>Controller(legacy,source,exe,()=>true).Enable())&&Refuses(()=>Controller(legacy,source,exe,()=>true).Restore())&&legacy.Calls.Count==0,"fast/legacy Run conflict does not add duplicate startup");
  ShortcutFile.Write(active,exe,"--other","FOREIGN_TEST");var collision=new Store();
  check(Refuses(()=>Controller(collision,source,exe).Enable())&&collision.Calls.Count==0,"fast/foreign Startup shortcut preserved before scheduler write");
  File.Delete(active);source.Restore();ShortcutFile.Write(active,exe,"--auto","CHANGED_BUT_OWNED_TEST");
  check(Refuses(()=>Controller(new Store(),source,exe).Enable())&&File.Exists(active),"fast/different preexisting backup and shortcut both preserved");
  File.Delete(active);source.Restore();var cannotDisable=new Store();var uncooperative=Controller(cannotDisable,source,exe);uncooperative.Enable();cannotDisable.IgnoreDisable=true;
  check(Refuses(()=>uncooperative.Restore())&&!File.Exists(active)&&cannotDisable.Task.Enabled,"fast/failed scheduler disable never adds Startup duplicate");
  cannotDisable.IgnoreDisable=false;uncooperative.Restore();

  string clean=Path.Combine(root,"new-user");Directory.CreateDirectory(clean);var noPrior=Source(clean,exe);var noTask=new Store();var newUser=Controller(noTask,noPrior,exe);newUser.Enable();newUser.Restore();
  check(StartupRegistration.IsRegisteredAt(Path.Combine(clean,"startup.lnk"),exe)&&noTask.Task==null,"fast/no prior shortcut supports explicit normal-mode restore");
  using(var form=new MainWindow(new Settings(),()=>{})){
   int buttons=0;foreach(Control control in form.Controls)if(control is Button&&(control.Text.Contains("빠른 자동 시작")||control.Text.Contains("기존 방식")||control.Text.Contains("기존 자동 시작")||control.Text=="자동 시작 끄기"))buttons++;
   check(buttons==3,"fast/GUI exposes fast normal-restore and off without modifying registration");
  }
 }
}
