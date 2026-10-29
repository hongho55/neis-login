using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

// Message-only window: hotkeys work without creating a visible main form.
internal sealed class StopHotkeyWindow:NativeWindow,IDisposable {
 readonly Action stop;StopHotkeyRegistration registration;bool disposed;
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
 internal bool Registered {get{return registration!=null&&registration.Active;}}
 internal StopHotkeyWindow(Action onStop){
  stop=onStop;CreateHandle(new CreateParams{Caption="TeacherWorkStart.Hotkey",Parent=new IntPtr(-3)});
  IntPtr hwnd=Handle;registration=new StopHotkeyRegistration(()=>RegisterHotKey(hwnd,1,StopHotkeyRegistration.Modifiers,StopHotkeyRegistration.Key),()=>UnregisterHotKey(hwnd,1));registration.Register();
 }
 protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam==(IntPtr)1&&!disposed)stop();base.WndProc(ref m);}
 public void Dispose(){if(disposed)return;disposed=true;if(registration!=null)registration.Dispose();if(Handle!=IntPtr.Zero)DestroyHandle();}
}
