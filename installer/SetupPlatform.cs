using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;

namespace TeacherWorkStartSetup {
 internal sealed class SetupLease:IDisposable {
  readonly Mutex mutex;internal bool Owned;
  internal SetupLease(string name){mutex=new Mutex(false,name);try{Owned=mutex.WaitOne(0);}catch(AbandonedMutexException){Owned=true;}}
  public void Dispose(){if(Owned){Owned=false;mutex.ReleaseMutex();}mutex.Dispose();}
 }
 internal sealed class SetupPlatform:ISetupPlatform {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetCurrentPackageFullName(ref uint length,StringBuilder name);
  internal static string User {get{return WindowsIdentity.GetCurrent().User.Value;}}
  public bool Packaged {get{uint size=0;return GetCurrentPackageFullName(ref size,null)!=15700;}}
  public bool HelperRunning {get{using(var lease=new SetupLease("Local\\TeacherWorkStart-"+User))return !lease.Owned;}}
  public int InstallOnly(string executable){using(var process=Process.Start(new ProcessStartInfo(executable,"--install-only"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)})){process.WaitForExit();return process.ExitCode;}}
  public void OpenHelper(string executable){Process.Start(new ProcessStartInfo(executable,"--show"){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(executable)});}
  public bool ShortcutMatches(string executable){
   string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"업무 시작 도우미.lnk");if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)return false;
   return DesktopLink.Matches(file,executable);
  }
 }
 // Read only: the existing helper creates its own optional shortcut during install.
 internal static class DesktopLink {
  [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink {}
  [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
  interface IShellLinkW {
   void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder file,int length,IntPtr data,uint flags);
   void GetIDList(out IntPtr list);void SetIDList(IntPtr list);
   void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder text,int length);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string text);
   void GetHotkey(out short hotkey);void SetHotkey(short hotkey);void GetShowCmd(out int show);void SetShowCmd(int show);
   void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int length,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
   void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);void Resolve(IntPtr window,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
  }
  internal static bool Matches(string file,string expected){object link=new ShellLink();try{((IPersistFile)link).Load(file,0);var native=(IShellLinkW)link;var path=new StringBuilder(32768);var args=new StringBuilder(32768);native.GetPath(path,path.Capacity,IntPtr.Zero,4);native.GetArguments(args,args.Capacity);return String.Equals(Path.GetFullPath(path.ToString()),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase)&&args.Length==0;}finally{Marshal.FinalReleaseComObject(link);}}
 }
}
