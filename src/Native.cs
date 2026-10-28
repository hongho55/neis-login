using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Automation;

internal static class Native {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetCurrentPackageFullName(ref uint length,StringBuilder name);
 internal static bool IsPackaged {get{uint n=0;return GetCurrentPackageFullName(ref n,null)!=15700;}}
 internal delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc p, IntPtr a);
 [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] internal static extern uint SendInput(uint n, INPUT[] p, int size);
 [StructLayout(LayoutKind.Sequential)] internal struct INPUT { internal uint type; internal INPUTUNION u; }
 [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION {
  [FieldOffset(0)] internal MOUSEINPUT mouse;
  [FieldOffset(0)] internal KEYBDINPUT key;
 }
 [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { internal int dx,dy; internal uint mouseData,flags,time; internal UIntPtr extra; }
 [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { internal ushort vk,scan; internal uint flags,time; internal UIntPtr extra; }
 internal static void Key(ushort vk) {
  INPUT a=new INPUT {type=1,u=new INPUTUNION {key=new KEYBDINPUT {vk=vk}}};
  INPUT b=a;b.u.key.flags=2;
  if(SendInput(2,new[]{a,b},Marshal.SizeOf(typeof(INPUT)))!=2)throw new SafeFailure("키 입력이 전달되지 않았습니다. 반복하지 않고 중단합니다.");
 }
 internal static void ClearFocused() {
  INPUT a=new INPUT{type=1,u=new INPUTUNION{key=new KEYBDINPUT{vk=0x11}}};
  INPUT b=new INPUT{type=1,u=new INPUTUNION{key=new KEYBDINPUT{vk=0x41}}};
  INPUT c=b;c.u.key.flags=2;INPUT d=a;d.u.key.flags=2;
  if(SendInput(4,new[]{a,b,c,d},Marshal.SizeOf(typeof(INPUT)))!=4)throw new SafeFailure("입력칸 초기화에 실패했습니다.");
  Key(0x08);
 }
 internal static void UnicodeChar(char ch) {
  INPUT a=new INPUT{type=1,u=new INPUTUNION{key=new KEYBDINPUT{scan=ch,flags=4}}};
  INPUT b=a;b.u.key.flags=6;
  if(SendInput(2,new[]{a,b},Marshal.SizeOf(typeof(INPUT)))!=2)throw new SafeFailure("암호 입력을 전달하지 못했습니다. 재입력하지 않습니다.");
 }
 internal static void Click(AutomationElement e,IntPtr hwnd) {
  object p;
  if(e.TryGetCurrentPattern(InvokePattern.Pattern,out p)) {((InvokePattern)p).Invoke();return;}
  if(e.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p)) {((SelectionItemPattern)p).Select();return;}
  System.Windows.Point point;
  if(!e.TryGetClickablePoint(out point))throw new SafeFailure("대상 요소에 클릭 위치가 없습니다. 요소를 찾은 것과 입력 가능 여부는 별도입니다.");
  SetForegroundWindow(hwnd);
  if(GetForegroundWindow()!=hwnd)throw new SafeFailure("대상 Edge 창을 활성화하지 못했습니다.");
  SetCursorPos((int)point.X,(int)point.Y);
  INPUT a=new INPUT{type=0,u=new INPUTUNION{mouse=new MOUSEINPUT{flags=2}}};INPUT b=a;b.u.mouse.flags=4;
  if(SendInput(2,new[]{a,b},Marshal.SizeOf(typeof(INPUT)))!=2)throw new SafeFailure("클릭을 전달하지 못했습니다.");
 }
}

internal sealed class SafeFailure:Exception { internal SafeFailure(string s):base(s){} }

// No enumeration of unrelated credentials. Only this app's exact, certificate-bound target.
internal static class Vault {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct CREDENTIAL {
  internal uint Flags,Type; internal string TargetName,Comment; internal long LastWritten;
  internal uint CredentialBlobSize; internal IntPtr CredentialBlob; internal uint Persist,AttributeCount;
  internal IntPtr Attributes; internal string TargetAlias,UserName;
 }
 [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Write(ref CREDENTIAL c,uint flags);
 [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Read(string target,uint type,uint flags,out IntPtr ptr);
 [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool Delete(string target,uint type,uint flags);
 [DllImport("advapi32.dll")] static extern void CredFree(IntPtr p);
 internal static bool Equal(SecureString a,SecureString b) {
  IntPtr pa=IntPtr.Zero,pb=IntPtr.Zero;
  try {pa=Marshal.SecureStringToCoTaskMemUnicode(a);pb=Marshal.SecureStringToCoTaskMemUnicode(b);int diff=a.Length^b.Length;
   for(int i=0;i<Math.Min(a.Length,b.Length);i++)diff|=Marshal.ReadInt16(pa,i*2)^Marshal.ReadInt16(pb,i*2);return diff==0;
  }finally{if(pa!=IntPtr.Zero)Marshal.ZeroFreeCoTaskMemUnicode(pa);if(pb!=IntPtr.Zero)Marshal.ZeroFreeCoTaskMemUnicode(pb);}
 }
 internal static void Save(string target,string user,SecureString secret) {
  if(secret.Length==0||secret.Length>256)throw new SafeFailure("암호 길이를 확인해 주세요.");
  IntPtr p=Marshal.SecureStringToCoTaskMemUnicode(secret);
  try {CREDENTIAL c=new CREDENTIAL{Type=1,TargetName=target,UserName=user,Comment="강원 업무 시작 도우미: 현재 Windows 사용자 전용",CredentialBlob=p,CredentialBlobSize=(uint)secret.Length*2,Persist=2};
   if(!Write(ref c,0))throw new SafeFailure("Windows 자격 증명 저장이 허용되지 않거나 실패했습니다. 정책을 변경하지 않고 중단합니다.");
  } finally {Marshal.ZeroFreeCoTaskMemUnicode(p);}
 }
 internal static bool Exists(string target) {IntPtr p;if(!Read(target,1,0,out p))return false;try{return true;}finally{WipeFree(p);}}
 internal static void Remove(string target) {if(!Delete(target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new SafeFailure("이 앱의 저장 암호를 삭제하지 못했습니다.");}
 static void WipeFree(IntPtr p){try{var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));for(int i=0;i<c.CredentialBlobSize;i++)Marshal.WriteByte(c.CredentialBlob,i,0);}finally{CredFree(p);}}
 internal static void TypeInto(string target,Action guard) {
  IntPtr p;if(!Read(target,1,0,out p))throw new SafeFailure("등록한 암호가 없습니다. 인증서 설정을 먼저 해 주세요.");
  try {var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));
   if(c.CredentialBlobSize==0||c.CredentialBlobSize%2!=0||c.CredentialBlobSize>512)throw new SafeFailure("저장 형식이 맞지 않습니다. 암호를 다시 등록해 주세요.");
   guard();Native.ClearFocused();
   for(int i=0;i<c.CredentialBlobSize;i+=2){guard();Native.UnicodeChar((char)Marshal.ReadInt16(c.CredentialBlob,i));System.Threading.Thread.Sleep(20);}
  }finally{WipeFree(p);}
 }
 // Values stay inside this local call; no process arguments, clipboard, logs,
 // settings, or tool/model boundary receive the credential contents.
 internal static void SetDirect(string target,Action<string> setValue,Action guard){
  IntPtr p;if(!Read(target,1,0,out p))throw new SafeFailure("등록한 암호가 없습니다. 인증서 설정을 먼저 해 주세요.");
  string localValue=null;
  try{var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));
   if(c.CredentialBlobSize==0||c.CredentialBlobSize%2!=0||c.CredentialBlobSize>512)throw new SafeFailure("저장 형식을 확인하지 못했어요. 입력하지 않습니다.");
   guard();localValue=Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2);
   try{setValue(localValue);}catch(Exception){throw new SafeFailure("요소 직접 입력이 실패했어요. 키보드 입력으로 바꾸거나 다시 입력하지 않았습니다.");}
   guard();
  }finally{localValue=null;WipeFree(p);}
 }
 internal static void SelfTest(){
  string target="TeacherWorkStart/selftest/"+Guid.NewGuid().ToString("N");byte[] bytes=new byte[24];
  using(var rng=System.Security.Cryptography.RandomNumberGenerator.Create())rng.GetBytes(bytes);
  using(var secret=new SecureString()){
   foreach(byte b in bytes)secret.AppendChar((char)(0x21+(b%90)));Array.Clear(bytes,0,bytes.Length);
   IntPtr p=IntPtr.Zero,expected=IntPtr.Zero;
   try{Save(target,"self-test",secret);if(!Read(target,1,0,out p))throw new SafeFailure("보안 저장소 읽기 시험 실패");var c=(CREDENTIAL)Marshal.PtrToStructure(p,typeof(CREDENTIAL));
    expected=Marshal.SecureStringToCoTaskMemUnicode(secret);int diff=(int)c.CredentialBlobSize-secret.Length*2;
    for(int i=0;i<secret.Length;i++)diff|=Marshal.ReadInt16(expected,i*2)^Marshal.ReadInt16(c.CredentialBlob,i*2);
    if(diff!=0)throw new SafeFailure("보안 저장소 왕복 시험 실패");
    int guarded=0;SetDirect(target,local=>{if(local.Length!=secret.Length)throw new SafeFailure("로컬 직접 입력 시험 실패");for(int i=0;i<local.Length;i++)if(local[i]!=(char)Marshal.ReadInt16(expected,i*2))throw new SafeFailure("로컬 직접 입력 시험 실패");},()=>guarded++);
    if(guarded!=2)throw new SafeFailure("직접 입력 전후 검사 시험 실패");
   }finally{if(expected!=IntPtr.Zero)Marshal.ZeroFreeCoTaskMemUnicode(expected);if(p!=IntPtr.Zero)WipeFree(p);Remove(target);}
  }
 }
}
