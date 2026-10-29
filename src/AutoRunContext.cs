using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

internal static class AutoRunEligibility {
 internal static string Check(Settings settings,Func<bool> credentialExists){
  if(settings.PendingAttempt)return "이전 입력 결과가 미확인이라 자동 실행하지 않았습니다. 화면을 확인하고 오류 상태를 직접 초기화해 주세요.";
  if(!settings.Verified||String.IsNullOrWhiteSpace(settings.CertUser)||String.IsNullOrWhiteSpace(settings.CertExpiry)||String.IsNullOrWhiteSpace(settings.CertIssuer))return "처음 설정을 완료한 뒤 업무 시작을 눌러 주세요. 자동 로그인은 시작하지 않았습니다.";
  DateTime expiry;if(!DateTime.TryParse(settings.CertExpiry,out expiry)||expiry.Date<DateTime.Today)return "등록 인증서의 유효기간을 확인해 주세요. 자동 로그인은 시작하지 않았습니다.";
  try{WindowFlow.LaunchArguments(settings.Profile,Portal.Origin+Portal.LoginPath);}catch(SafeFailure){return "Edge 프로필 설정을 확인해 주세요. 자동 로그인은 시작하지 않았습니다.";}
  return credentialExists()?null:"이 Windows 사용자에게 등록된 암호를 확인하지 못했습니다. 로컬 등록 안내를 확인해 주세요.";
 }
}

internal sealed class AutoRunServices {
 internal Func<Settings,string> Validate=s=>AutoRunEligibility.Check(s,()=>Vault.Exists(s.Target));
 internal Func<Settings,bool> Run=s=>Portal.Run(s,false);
 internal Action<string> Record=AppState.Record;
 internal Func<bool> LogFailed=()=>AppState.HasLogFailure;
 internal bool TrayVisible=true;
 internal Action<string> Fatal=message=>MessageBox.Show(AppState.WithLogNotice(message),"업무 시작 도우미 · 중단");
 internal Func<Settings,string,bool,Form> CreateMain=(s,message,openSettings)=>{
  var form=new MainWindow(s);form.ShowNotice(message);if(openSettings)form.Shown+=(o,e)=>form.OpenPreferences();return form;
 };
}

internal sealed class AutoRunTiming {
 readonly Stopwatch clock=Stopwatch.StartNew();readonly Action<string> record;readonly HashSet<string> marked=new HashSet<string>();
 internal AutoRunTiming(Action<string> write){record=write;}
 internal void Mark(string stage){
  if(stage!="HELPER_START"&&stage!="EDGE_READY"&&stage!="CERTIFICATE_READY"&&stage!="LOGIN_COMPLETE")throw new ArgumentException("Unknown timing stage");
  lock(marked){if(marked.Add(stage))record("TIMING_"+stage+" elapsed_ms="+clock.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));}
 }
}

internal sealed class AutoRunContext:ApplicationContext {
 readonly Settings settings;readonly AutoRunServices services;readonly Control dispatcher=new Control{Visible=false};
 readonly AutoRunTiming timing;NotifyIcon tray;Icon trayIcon;ContextMenuStrip menu;ToolStripMenuItem progress,stopItem,settingsItem;StopHotkeyWindow hotkey;
 bool running,settingsRequested,disposed,started,resourcesReleased;string lastProgress="자동 실행 준비 중";
 internal bool Running {get{return running;}}
 internal bool ResourcesReleased {get{return resourcesReleased;}}
 internal bool HasMainWindow {get{return MainForm!=null;}}
 internal AutoRunContext(Settings s):this(s,new AutoRunServices()){}
 internal AutoRunContext(Settings s,AutoRunServices implementation){
  settings=s;services=implementation;timing=new AutoRunTiming(services.Record);
  IntPtr hiddenDispatcher=dispatcher.Handle; // A hidden marshal control, never a main form.
  Application.Idle+=StartOnce;
 }
 void StartOnce(object sender,EventArgs e){
  Application.Idle-=StartOnce;if(started||disposed)return;started=true;Portal.HelperWindow=IntPtr.Zero;
  try{
   timing.Mark("HELPER_START");
   menu=new ContextMenuStrip();progress=new ToolStripMenuItem(lastProgress){Enabled=false};stopItem=new ToolStripMenuItem("중단",null,(o,a)=>RequestStop());settingsItem=new ToolStripMenuItem("설정 열기",null,(o,a)=>RequestSettings());
   menu.Items.AddRange(new ToolStripItem[]{progress,new ToolStripSeparator(),stopItem,settingsItem});
   trayIcon=AppVisuals.CreateIcon();tray=new NotifyIcon{Icon=trayIcon,Text="업무 시작 도우미 · 자동 실행 준비",ContextMenuStrip=menu,Visible=services.TrayVisible};
   AppVisuals.OpenMenuOnClick(tray,menu);tray.DoubleClick+=(o,a)=>RequestSettings();hotkey=new StopHotkeyWindow(RequestStop);
   RunTrace.Record(TraceEvent.TRAY_READY);
   if(!settings.AutoRunAtLogon){RunTrace.Record(TraceEvent.AUTO_MANUAL_WAIT);Present("프로그램만 열었습니다. 업무 시작을 직접 눌러 주세요.",false);return;}
   RunTrace.Record(TraceEvent.AUTO_PREFLIGHT_BEGIN);string reason=services.Validate(settings);
   if(reason!=null){RunTrace.Record(TraceEvent.AUTO_PREFLIGHT_REJECTED);Present(reason,false);return;}
   RunTrace.Record(TraceEvent.AUTO_PREFLIGHT_ACCEPTED);
   Portal.Report=message=>Post(()=>UpdateProgress(message));Portal.Milestone=timing.Mark;
   Portal.BeginOperation();running=true;RunTrace.Record(TraceEvent.AUTO_WORK_START);UpdateProgress("업무 시작 중");
   if(services.TrayVisible)try{tray.BalloonTipTitle="업무 시작 도우미";tray.BalloonTipText="자동 작업을 시작합니다. 시계 옆 책 아이콘을 누르면 상태·중단·설정을 볼 수 있습니다.";tray.ShowBalloonTip(3500);}catch(System.ComponentModel.Win32Exception){}
   ThreadPool.QueueUserWorkItem(_=>{
    bool success=false;Exception failure=null;try{success=services.Run(settings);}catch(Exception ex){failure=ex;}
    Post(()=>Complete(success,failure));
   });
  }catch(Exception ex){RunTrace.Record(TraceEvent.ERROR,failure:ex);Present(Describe(ex),false);}
 }
 void Post(Action action){if(disposed)return;try{dispatcher.BeginInvoke((Action)(()=>{if(!disposed)action();}));}catch(InvalidOperationException){if(!disposed)throw;}}
 void UpdateProgress(string text){
  if(resourcesReleased||tray==null)return;lastProgress=text;
  string hint=hotkey!=null&&hotkey.Registered?"":" · 단축키 사용 불가: 트레이 중단 사용";
  progress.Text=text+hint;string tooltip=(hotkey!=null&&hotkey.Registered?"":"단축키 사용 불가 · ")+"업무 도우미 · "+text;tray.Text=tooltip.Length>63?tooltip.Substring(0,63):tooltip;
 }
 internal void RequestStop(){
  if(!running||disposed)return;RunTrace.Record(TraceEvent.CANCEL_REQUESTED);bool submitted=Portal.RequestStop();
  UpdateProgress(submitted?"제출 시작 후 중단 요청 · 이미 시작한 제출은 취소할 수 없음":"중단 요청 · 새 제출은 시작하지 않음");
 }
 internal void RequestSettings(){
  if(disposed||resourcesReleased)return;RunTrace.Record(TraceEvent.SETTINGS_REQUESTED);settingsRequested=true;if(settingsItem!=null)settingsItem.Enabled=false;RequestStop();
  UpdateProgress("작업 정리 후 설정 화면을 엽니다 · 자동 재시도 없음");
 }
 void Complete(bool success,Exception failure){
  running=false;RunTrace.Record(success&&failure==null&&!Portal.Cancelled?TraceEvent.AUTO_WORK_SUCCESS:TraceEvent.AUTO_WORK_STOPPED,failure:failure);if(disposed)return;
  if(settingsRequested){services.Record("AUTO_SETTINGS_REQUESTED");Present("자동 작업을 정리했습니다. 이미 시작한 제출은 취소할 수 없습니다. 필요한 설정을 확인해 주세요.",true);return;}
  if(failure!=null||!success||Portal.Cancelled){services.Record("AUTO_STOPPED");Present(failure==null?"자동 작업을 중단했습니다. 자동으로 다시 제출하지 않습니다.":Describe(failure),false);return;}
  services.Record("AUTO_SUCCESS");
  if(services.LogFailed()){Present("업무 시작은 완료됐지만 진단 기록 저장을 확인해 주세요. 로그인은 다시 실행하지 않습니다.",false);return;}
  ReleaseAutomation();ExitThread();
 }
 static string Describe(Exception error){return error is SafeFailure?error.Message:"자동 실행을 중단했습니다. 오류 유형: "+error.GetType().Name+" · 자동으로 재시도하지 않습니다.";}
 void Present(string message,bool openSettings){
  running=false;RunTrace.Record(TraceEvent.AUTO_GUIDANCE_OPEN);ReleaseAutomation();services.Record("AUTO_GUIDANCE_OPENED");
  try{MainForm=services.CreateMain(settings,message,openSettings);if(MainForm==null)throw new InvalidOperationException("No guidance form");MainForm.Show();if(!Native.IsWindowVisible(MainForm.Handle))Native.ShowWindow(MainForm.Handle,4);}
  catch(Exception ex){services.Fatal("안내 화면을 열지 못했습니다. 오류 유형: "+ex.GetType().Name+" · 자동 로그인은 다시 실행하지 않습니다.");ExitThread();}
 }
 void ReleaseAutomation(){
  if(resourcesReleased)return;resourcesReleased=true;Portal.Report=s=>{};Portal.Milestone=s=>{};
  if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}if(trayIcon!=null){trayIcon.Dispose();trayIcon=null;}if(menu!=null){menu.Dispose();menu=null;}if(hotkey!=null){hotkey.Dispose();hotkey=null;}
 }
 protected override void ExitThreadCore(){ReleaseAutomation();base.ExitThreadCore();}
 protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;Application.Idle-=StartOnce;ReleaseAutomation();dispatcher.Dispose();}base.Dispose(disposing);}
}
