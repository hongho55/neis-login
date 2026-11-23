using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;

namespace TeacherWorkStartSetup {
 internal enum SetupProblem { Busy, NewerVersion, UnknownInstallation, Payload, ChildFailed, Verification, SettingsChanged, LinkedPath }
 internal sealed class SetupFailure:Exception {
  internal readonly SetupProblem Problem;
  internal SetupFailure(SetupProblem problem,string message):base(message){Problem=problem;}
 }
 internal interface ISetupPlatform {
  bool HelperRunning {get;}
  bool Packaged {get;}
  int InstallOnly(string executable);
  bool ShortcutMatches(string executable);
  void OpenHelper(string executable);
 }
 internal sealed class SetupPayload {
  internal readonly Version Version;readonly Func<string,Stream> open;readonly string exeHash,configHash;
  internal SetupPayload(string version,string applicationHash,string configurationHash,Func<string,Stream> resources){Version=new Version(version);exeHash=applicationHash;configHash=configurationHash;open=resources;}
  internal static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");}
  internal static string FileHash(string path){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");}
  byte[] Read(string name,string expected){
   using(var input=open(name))using(var buffer=new MemoryStream()){
    if(input==null)throw new SetupFailure(SetupProblem.Payload,"설치 파일에 필요한 구성 요소가 없습니다. 설치 파일을 다시 받아 주세요.");
    input.CopyTo(buffer);var bytes=buffer.ToArray();if(!String.Equals(Hash(bytes),expected,StringComparison.OrdinalIgnoreCase))throw new SetupFailure(SetupProblem.Payload,"설치 파일의 구성 요소가 손상됐습니다. 실행하지 않고 중단했어요.");return bytes;
   }
  }
  internal void Export(string folder){var exe=Read("Application",exeHash);var config=Read("Configuration",configHash);WriteNew(Path.Combine(folder,"TeacherWorkStart.exe"),exe);WriteNew(Path.Combine(folder,"TeacherWorkStart.exe.config"),config);}
  static void WriteNew(string path,byte[] bytes){using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Write(bytes,0,bytes.Length);}
  internal bool Matches(string executable){return File.Exists(executable)&&File.Exists(executable+".config")&&String.Equals(FileHash(executable),exeHash,StringComparison.OrdinalIgnoreCase)&&String.Equals(FileHash(executable+".config"),configHash,StringComparison.OrdinalIgnoreCase);}
 }
 internal sealed class SetupResult {internal bool ShortcutReady,SettingsPreserved,TemporaryFilesRemoved;}
 internal sealed class TemporaryPayload:IDisposable {
  internal readonly string Folder;internal bool Cleaned;
  internal TemporaryPayload(string temp){
   Folder=Path.GetFullPath(Path.Combine(temp,"TeacherWorkStart-Setup-"+Guid.NewGuid().ToString("N")));
   if(!String.Equals(Path.GetDirectoryName(Folder).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected temporary location");
   SetupCore.CheckPath(temp);Directory.CreateDirectory(Folder);SetupCore.CheckPath(Folder);
  }
  public void Dispose(){
   try{SetupCore.CheckPath(Folder);foreach(string name in new[]{"TeacherWorkStart.exe","TeacherWorkStart.exe.config"}){string path=Path.Combine(Folder,name);if(File.Exists(path)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return;File.Delete(path);}}Directory.Delete(Folder,false);Cleaned=true;}catch(IOException){}catch(UnauthorizedAccessException){}catch(SetupFailure){}
  }
 }
 internal sealed class SetupCore {
  internal readonly string Root,Executable;readonly string temp;readonly SetupPayload payload;readonly ISetupPlatform platform;readonly Func<string,Version> installedVersion;
  internal SetupCore(string root,string temporary,SetupPayload package,ISetupPlatform windows,Func<string,Version> version=null){Root=Path.GetFullPath(root);Executable=Path.Combine(Root,"TeacherWorkStart.exe");temp=temporary;payload=package;platform=windows;installedVersion=version??ReadVersion;}
  static Version ReadVersion(string path){Version value;return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion,out value)?value:null;}
  internal static void CheckPath(string path){
   for(string current=Path.GetFullPath(path);!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)){
    if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new SetupFailure(SetupProblem.LinkedPath,"설치 위치에 연결된 폴더가 있어 자동으로 변경하지 않았습니다. 학교 전산 담당자에게 확인해 주세요.");
   }
  }
  static string SettingsStamp(string root){string path=Path.Combine(root,"settings.json");return File.Exists(path)?SetupPayload.FileHash(path):null;}
  internal SetupResult Install(){
   if(platform.Packaged)throw new SetupFailure(SetupProblem.UnknownInstallation,"설치 파일을 파일 탐색기에서 직접 열어 주세요. 현재 앱 안에서는 설치를 시작하지 않았습니다.");
   CheckPath(Root);if(platform.HelperRunning)throw new SetupFailure(SetupProblem.Busy,"업무 시작 도우미가 실행 중입니다. 도우미를 닫은 뒤 설치하기를 다시 눌러 주세요.");
   if(File.Exists(Executable)){
    Version prior=installedVersion(Executable);if(prior==null)throw new SetupFailure(SetupProblem.UnknownInstallation,"기존 실행 파일의 버전을 확인하지 못해 덮어쓰지 않았습니다.");
    if(prior>payload.Version)throw new SetupFailure(SetupProblem.NewerVersion,"더 새로운 버전이 이미 설치돼 있어요. 기존 도우미를 사용해 주세요.");
   }
   string before=SettingsStamp(Root);SetupResult result=null;var extracted=new TemporaryPayload(temp);
   try{
    payload.Export(extracted.Folder);int code=platform.InstallOnly(Path.Combine(extracted.Folder,"TeacherWorkStart.exe"));
    if(code!=0)throw new SetupFailure(SetupProblem.ChildFailed,"설치를 완료하지 못했어요. 실행 중인 도우미와 화면의 안내를 확인한 뒤 다시 눌러 주세요. 자동으로 반복 설치하지 않습니다.");
    if(!payload.Matches(Executable))throw new SetupFailure(SetupProblem.Verification,"설치된 파일을 확인하지 못했어요. 도우미를 자동으로 실행하지 않았습니다.");
    if(!String.Equals(before,SettingsStamp(Root),StringComparison.Ordinal))throw new SetupFailure(SetupProblem.SettingsChanged,"설치 중 기존 설정에 변경이 감지됐어요. 도우미를 자동으로 실행하지 않았습니다. 설정 상태를 확인해 주세요.");
    bool shortcut=false;try{shortcut=platform.ShortcutMatches(Executable);}catch(Exception){}
    result=new SetupResult{ShortcutReady=shortcut,SettingsPreserved=true};
   }finally{extracted.Dispose();}
   result.TemporaryFilesRemoved=extracted.Cleaned;return result;
  }
  internal void Open(){platform.OpenHelper(Executable);}
 }
}
