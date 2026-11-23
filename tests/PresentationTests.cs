using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// Only fake settings/handles and hidden controls. Never opens a browser, reads
// credentials, saves user settings, or changes the real startup registration.
internal static class PresentationTests {
 static void Click(Button button){typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});}
 internal static void Run(Action<bool,string> check){
  check(WorkProgressWindow.CenterWithin(new Rectangle(100,200,1200,800),new Size(440,190))==new Point(480,505),"presentation/notice is centered in the selected working area");
  check(WorkProgressWindow.CenterWithin(new Rectangle(-1920,0,1920,1080),new Size(440,190))==new Point(-1180,445),"presentation/notice centering supports monitors with a negative screen origin");
  IntPtr original=new IntPtr(-301),work=new IntPtr(-302),current=work;int activations=0,observations=0;
  var kept=new ForegroundReturn(original,()=>current,h=>true,h=>{activations++;current=h;return true;},true);kept.Own(work);
  var reasons=new List<ForegroundReturnOutcome>();
  check(kept.KeepWorkForeground&&!kept.TryReturn(reasons.Add)&&current==work&&activations==0&&reasons.Single()==ForegroundReturnOutcome.WORK_WINDOW_KEPT,"presentation/foreground run suppresses the original-window return with an explicit reason");
  current=new IntPtr(-303);
  check(!kept.TryReturn(reasons.Add)&&current==new IntPtr(-303)&&activations==0,"presentation/foreground policy never fights a user-selected unrelated window during return");
  check(!kept.TryReturn(r=>{observations++;throw new IOException("INJECTED");})&&observations==1&&activations==0,"presentation/foreground return observer failure cannot activate another window");

  var settings=new Settings{AutoRunAtLogon=false,PendingAttempt=true,OpenNeis=false,OpenKedu=true};int saves=0,descriptions=0;bool failSave=false;
  var registrations=new List<bool>();
  using(var panel=new StartupOptions(settings,()=>{saves++;if(failSave)throw new IOException("INJECTED");},registrations.Add,()=>{descriptions++;return "TEST_STARTUP_STATE";})){
   var modes=panel.Controls.OfType<RadioButton>().ToArray();
   check(saves==0&&registrations.Count==0&&descriptions==1&&modes[1].Checked&&!modes[0].Checked&&!settings.AutoRunAtLogon,"presentation/opening startup settings preserves the saved mode without registration or writes");
   modes[0].Checked=true;
   check(saves==1&&settings.AutoRunAtLogon&&registrations.Count==0&&settings.PendingAttempt&&!settings.OpenNeis&&settings.OpenKedu,"presentation/selecting automatic work saves only the preference and preserves unresolved attempt and system choices");
   failSave=true;modes[1].Checked=true;
   check(saves==2&&settings.AutoRunAtLogon&&modes[0].Checked&&!modes[1].Checked&&registrations.Count==0&&settings.PendingAttempt&&!settings.OpenNeis&&settings.OpenKedu,"presentation/failed mode persistence rolls back without resetting safety or opening systems");
   check(panel.Controls.OfType<Label>().Any(l=>l.Text.Contains("저장하지 못")),"presentation/startup selector exposes persistence failure rather than claiming success");
   Click(panel.Controls.OfType<Button>().Single(b=>b.Text=="자동 시작 켜기"));
   Click(panel.Controls.OfType<Button>().Single(b=>b.Text=="자동 시작 끄기"));
   check(registrations.SequenceEqual(new[]{true,false})&&saves==2,"presentation/only explicit startup buttons reach the registration callback");
  }
  using(var panel=new StartupOptions(new Settings(),()=>{},enabled=>{throw new SafeFailure("TEST_REGISTRATION_REFUSED");},()=>"TEST_STARTUP_STATE")){
   Click(panel.Controls.OfType<Button>().Single(b=>b.Text=="자동 시작 켜기"));
   check(panel.Controls.OfType<Label>().Any(l=>l.Text=="TEST_REGISTRATION_REFUSED"),"presentation/startup registration refusal remains visible without fallback registration");
  }

  int stops=0;
  using(var form=new WorkProgressWindow(()=>stops++)){
   form.Opacity=0;form.Location=new Point(-32000,-32000);form.ShowInTaskbar=false;
   bool withoutActivation=(bool)typeof(WorkProgressWindow).GetProperty("ShowWithoutActivation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form,null);
   var parameters=(CreateParams)typeof(WorkProgressWindow).GetProperty("CreateParams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form,null);
   check(withoutActivation&&(parameters.ExStyle&0x08000000)!=0&&!form.ShowInTaskbar,"presentation/progress notice declares non-activating behavior and no taskbar entry");
   IntPtr hiddenHandle=form.Handle;
   check(hiddenHandle!=IntPtr.Zero&&!form.Visible,"presentation/progress lifecycle fixture remains hidden without taking focus");
   IntPtr foreground=Native.GetForegroundWindow();form.Show();
   check(form.Visible&&Native.GetForegroundWindow()==foreground,"presentation/showing the real offscreen notice preserves the actual Windows foreground handle");
   form.UpdateProgress("TEST_SYSTEM_WAIT");
   check(form.Controls.OfType<Label>().Any(l=>l.Text=="TEST_SYSTEM_WAIT")&&stops==0,"presentation/progress updates do not trigger submission or stop actions");
   check(Native.GetForegroundWindow()==foreground,"presentation/updating the notice does not activate it");
   form.SetPointerNoticeVisible(false);check(!form.Visible,"presentation/coordinate fallback can hide only the notice before resolving a click point");
   form.SetPointerNoticeVisible(true);check(form.Visible&&Native.GetForegroundWindow()==foreground,"presentation/notice returns after pointer access without activating");
   Click(form.Controls.OfType<Button>().Single(b=>b.Text=="중단"));
   check(stops==1&&!form.IsDisposed,"presentation/progress stop button requests cancellation while leaving lifecycle to the worker");
   form.Close();
   check(stops==2&&!form.IsDisposed&&form.IsHandleCreated,"presentation/progress close requests cancellation and cannot hide an unfinished worker");
   form.Finish();
   check(stops==2&&form.IsDisposed&&!form.IsHandleCreated,"presentation/completion disposes the progress window without requesting a further stop");
   form.SetPointerNoticeVisible(true);check(form.IsDisposed&&!form.IsHandleCreated,"presentation/late pointer cleanup cannot recreate a finished notice");
  }
 }
}
