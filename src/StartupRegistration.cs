using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

// Registration is performed only by the explicit button handler. No constructor,
// installation, password registration, or successful login creates a startup entry.
internal static class StartupRegistration {
 internal static string Executable {get{return Path.Combine(Settings.Root,"TeacherWorkStart.exe");}}
 internal static string ShortcutPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"업무 시작 도우미.lnk");}}
 internal static bool IsRegistered {get{return IsRegisteredAt(ShortcutPath,Executable);}}
 internal static bool HasEntry {get{return File.Exists(ShortcutPath)||LegacyPresent;}}
 internal static bool LegacyPresent {get{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){return key!=null&&key.GetValue("TeacherWorkStart")!=null;}}}
 internal static bool IsRegisteredAt(string path,string executable){try{if(!File.Exists(path)||!File.Exists(executable))return false;var link=ShortcutFile.Read(path);return Owns(link,executable);}catch{return false;}}
 static bool Owns(ShortcutFile.Info link,string executable){return String.Equals(Path.GetFullPath(link.Target),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase)&&link.Arguments=="--auto";}
 static void CheckExisting(string path,string executable){if(!File.Exists(path))return;if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0||!Owns(ShortcutFile.Read(path),executable))throw new SafeFailure("같은 이름의 다른 바로가기가 있어요. 덮어쓰거나 삭제하지 않았어요.");}
 internal static void Enable(){if(!File.Exists(Executable))throw new SafeFailure("설치된 프로그램을 찾지 못했어요. 설치 위치를 확인해 주세요.");RemoveLegacy();EnableAt(ShortcutPath,Executable);}
 internal static void EnableAt(string path,string executable){
  if(!File.Exists(executable))throw new SafeFailure("자동 시작할 프로그램을 찾지 못했어요.");
  CheckExisting(path,executable);if(IsRegisteredAt(path,executable))return;
  string temp=Path.Combine(Path.GetDirectoryName(executable),"startup-"+Guid.NewGuid().ToString("N")+".lnk");
  try{ShortcutFile.Write(temp,executable,"--auto","Windows 로그인 후 업무 시작 도우미 열기");if(!IsRegisteredAt(temp,executable))throw new SafeFailure("바로가기의 실행 경로를 확인하지 못했어요.");Directory.CreateDirectory(Path.GetDirectoryName(path));File.Move(temp,path);if(!IsRegisteredAt(path,executable))throw new SafeFailure("자동 시작 등록 결과를 확인하지 못했어요.");}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 internal static void Remove(){RemoveAt(ShortcutPath,Executable);RemoveLegacy();if(HasEntry)throw new SafeFailure("자동 시작 등록이 남아 있어요. 표시된 위치를 확인해 주세요.");}
 internal static void RemoveAt(string path,string executable){CheckExisting(path,executable);if(File.Exists(path))File.Delete(path);if(File.Exists(path))throw new SafeFailure("자동 시작 바로가기를 삭제하지 못했어요.");}
 internal static void RemoveLegacy(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key==null)return;string value=key.GetValue("TeacherWorkStart") as string;if(value==null)return;string expected="\""+Executable+"\" --auto";if(!String.Equals(value,expected,StringComparison.OrdinalIgnoreCase))throw new SafeFailure("기존 Run 항목의 경로가 달라 자동으로 삭제하지 않았어요.");key.DeleteValue("TeacherWorkStart",false);if(key.GetValue("TeacherWorkStart")!=null)throw new SafeFailure("기존 Run 항목을 삭제하지 못했어요.");}}
}

internal static class ShortcutFile {
 internal sealed class Info {internal string Target,Arguments;}
 [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink {}
 [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
 interface IShellLinkW {
  void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder file,int length,IntPtr findData,uint flags);
  void GetIDList(out IntPtr list);void SetIDList(IntPtr list);
  void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);
  void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
  void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);
  void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string text);
  void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);
  void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string text);
  void GetHotkey(out short hotkey);void SetHotkey(short hotkey);
  void GetShowCmd(out int show);void SetShowCmd(int show);
  void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int length,out int index);
  void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
  void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);
  void Resolve(IntPtr window,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
 }
 internal static Info Read(string path){object obj=new ShellLink();try{((IPersistFile)obj).Load(path,0);var link=(IShellLinkW)obj;var target=new StringBuilder(32768);var args=new StringBuilder(32768);link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetArguments(args,args.Capacity);return new Info{Target=target.ToString(),Arguments=args.ToString()};}finally{Marshal.FinalReleaseComObject(obj);}}
 internal static void Write(string path,string executable,string args,string description){object obj=new ShellLink();try{var link=(IShellLinkW)obj;link.SetPath(executable);link.SetArguments(args);link.SetWorkingDirectory(Path.GetDirectoryName(executable));link.SetDescription(description);link.SetShowCmd(1);((IPersistFile)obj).Save(path,true);}finally{Marshal.FinalReleaseComObject(obj);}}
}
