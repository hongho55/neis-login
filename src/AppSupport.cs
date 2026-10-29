using System;
using System.IO;
using System.Security;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class DiagnosticLog {
 static bool TryIO(Action write){try{write();return true;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(SecurityException){return false;}}
 internal static bool Record(string root,string code){
  bool status=TryIO(()=>{Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"status.json"),new JavaScriptSerializer().Serialize(new {time=DateTimeOffset.Now.ToString("o"),code=code}),new UTF8Encoding(false));});
  bool events=TryIO(()=>{Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"events.log"),DateTimeOffset.Now.ToString("o")+" "+code+Environment.NewLine,new UTF8Encoding(false));});
  return status&&events;
 }
}

internal static class SelectionPreferences {
 internal static void Save(Settings settings,bool neis,bool kedu,Action persist){
  bool oldNeis=settings.OpenNeis,oldKedu=settings.OpenKedu;settings.OpenNeis=neis;settings.OpenKedu=kedu;
  try{persist();}catch{settings.OpenNeis=oldNeis;settings.OpenKedu=oldKedu;throw;}
 }
}

internal sealed class StopHotkeyRegistration:IDisposable {
 internal const uint Modifiers=0x4003,Key=0x79; // Ctrl+Alt+F10, no-repeat. F12 is reserved.
 readonly Func<bool> register;readonly Action unregister;internal bool Active {get;private set;}
 internal StopHotkeyRegistration(Func<bool> add,Action remove){register=add;unregister=remove;}
 internal bool Register(){if(!Active)Active=register();return Active;}
 public void Dispose(){if(Active){Active=false;unregister();}}
}

internal sealed class InstanceLease:IDisposable {
 readonly Mutex mutex;internal bool Owned {get;private set;}
 internal InstanceLease(string name){mutex=new Mutex(false,name);try{Owned=mutex.WaitOne(0);}catch(AbandonedMutexException){Owned=true;}}
 internal void Release(){if(Owned){Owned=false;mutex.ReleaseMutex();}}
 public void Dispose(){Release();mutex.Dispose();}
}

internal static class InstallFiles {
 internal static bool CanWriteShortcut(string path,string target){
  if(!File.Exists(path))return true;
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return false;
  var previous=ShortcutFile.Read(path);
  return String.Equals(Path.GetFullPath(previous.Target),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase)&&previous.Arguments=="";
 }
 internal static void CopyAtomic(string source,string target){
  string staged=target+".update-"+Guid.NewGuid().ToString("N");
  try{File.Copy(source,staged,false);if(File.Exists(target))File.Replace(staged,target,null);else File.Move(staged,target);}
  finally{if(File.Exists(staged))File.Delete(staged);}
 }
}
