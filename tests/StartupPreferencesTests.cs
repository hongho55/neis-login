using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static class StartupPreferencesTests {
 internal static int TrayChild(){
  bool before=false,visible=false,menuOpened=false,controls=false,timedOut=false;NotifyIcon watched=null;int disposed=0;AutoRunContext context=null;
  var services=new AutoRunServices{TrayVisible=true,Record=s=>{},LogFailed=()=>false,Fatal=s=>{},Validate=s=>{var icon=(NotifyIcon)typeof(AutoRunContext).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context);before=icon!=null&&icon.Visible&&!context.HasMainWindow;return "TEST_SETUP_REQUIRED";},Run=s=>{throw new Exception("preflight denied");},CreateMain=(s,message,open)=>{
   var form=new MainWindow(new Settings{AutoRunAtLogon=false},()=>{}){Opacity=0,ShowInTaskbar=false};
   form.Shown+=(o,e)=>form.BeginInvoke((Action)(()=>{
    watched=(NotifyIcon)typeof(MainWindow).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);visible=watched!=null&&watched.Visible;watched.Disposed+=(a,b)=>disposed++;
    var menu=watched.ContextMenuStrip;controls=menu.Items.OfType<ToolStripMenuItem>().Any(x=>x.Text=="설정 열기")&&menu.Items.OfType<ToolStripMenuItem>().Any(x=>x.Text=="중단"&&!x.Enabled);
    typeof(NotifyIcon).GetMethod("OnMouseClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(watched,new object[]{new MouseEventArgs(MouseButtons.Left,1,0,0,0)});menuOpened=menu.Visible;menu.Close();form.Close();
   }));return form;
  }};
  using(context=new AutoRunContext(new Settings(),services))using(var timeout=new Timer{Interval=5000}){timeout.Tick+=(o,e)=>{timedOut=true;context.ExitThread();};timeout.Start();Application.Run(context);}
  Console.WriteLine(new JavaScriptSerializer().Serialize(new{before=before,visible=visible,controls=controls,menu=menuOpened,disposed=disposed==1&&watched!=null&&!watched.Visible,released=context.ResourcesReleased}));return timedOut?1:0;
 }
 internal static void Run(Action<bool,string> check,string root){
  var serializer=new JavaScriptSerializer();var old=serializer.Deserialize<Settings>("{\"OpenNeis\":false,\"PendingAttempt\":true}");
  check(old.AutoRunAtLogon&&!old.OpenNeis&&old.PendingAttempt,"startup/old settings retain automatic behavior without rewriting existing values");
  string file=Path.Combine(root,"startup-preference.json");StartupPreferences.Save(old,false,()=>old.SaveTo(file));var loaded=serializer.Deserialize<Settings>(File.ReadAllText(file));
  check(!loaded.AutoRunAtLogon&&!loaded.OpenNeis&&loaded.PendingAttempt,"startup/program-only choice survives save reload while preserving other preferences");
  bool failed=false;try{StartupPreferences.Save(old,true,()=>{throw new IOException("INJECTED");});}catch(IOException){failed=true;}
  check(failed&&!old.AutoRunAtLogon,"startup/preference save failure restores prior behavior");
  using(var form=new MainWindow(new Settings(),()=>{throw new IOException("INJECTED");})){
   var mode=(ComboBox)typeof(MainWindow).GetField("startupMode",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   mode.SelectedIndex=1;var status=(Label)typeof(MainWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
   check(mode.SelectedIndex==0&&status.Text.Contains("저장하지 못"),"startup/real selector rolls back and reports failed save");
  }
  var info=new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--startup-tray-child"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,RedirectStandardOutput=true};
  using(var child=System.Diagnostics.Process.Start(info)){
   if(!child.WaitForExit(10000)){child.Kill();throw new Exception("Tray child timed out");}if(child.ExitCode!=0)throw new Exception("Tray child failed");
   var result=serializer.Deserialize<System.Collections.Generic.Dictionary<string,bool>>(child.StandardOutput.ReadToEnd());
   check(result["before"]&&result["released"],"startup/fresh process shows real tray before preflight without main form");
   check(result["visible"]&&result["controls"],"startup/main form owns real visible tray with status stop and settings");
   check(result["menu"],"startup/left-click tray event opens actual context menu");
   check(result["disposed"],"startup/main form exit removes tray icon exactly once");
  }
 }
}
