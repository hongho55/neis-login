using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Win32;
using System.Runtime.InteropServices;

internal sealed class Settings {
 public string Profile="Default",CertUser="",CertExpiry="",CertIssuer="";
 public bool OpenNeis=true,OpenKedu=true,StartAtLogon=false,Verified=false,PendingAttempt=false;
 public bool AutoRunAtLogon=true;
 public string CertMedia=CertificateStorage.HardDisk,CertDrive="";
 public bool CertStorageConfigured=false,RegistrationPending=false;
 public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TeacherWorkStart");}}
 public static string FilePath {get{return Path.Combine(Root,"settings.json");}}
 [ScriptIgnore] public string Target {get{using(var hash=SHA256.Create()){var b=hash.ComputeHash(Encoding.UTF8.GetBytes(Portal.Origin+"\n"+CertUser+"\n"+CertExpiry+"\n"+CertIssuer));return "TeacherWorkStart/gwe/"+BitConverter.ToString(b).Replace("-","");}}}
 public static Settings Load(){if(!File.Exists(FilePath))return new Settings();try{return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath,Encoding.UTF8));}catch{throw new SafeFailure("설정 파일을 읽지 못했습니다. 자동 로그인을 시작하지 않습니다.");}}
 public void Save(){SaveTo(FilePath);}
 internal void SaveTo(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
}

internal static class AppState {
 static readonly object sync=new object();static bool logFailed;
 internal static bool HasLogFailure {get{lock(sync)return logFailed;}}
 internal static void NoteTraceFailure(){lock(sync)logFailed=true;}
 internal static string WithLogNotice(string message){lock(sync)return message+(logFailed?"\n진단 기록 저장 실패 · 위 결과를 확인해 주세요.":"");}
 internal static void Record(string code){RecordTo(Settings.Root,code);}
 internal static void RecordTo(string root,string code){lock(sync){if(!DiagnosticLog.Record(root,code))logFailed=true;}}
 internal static string Install(){
  string target=Path.Combine(Settings.Root,"TeacherWorkStart.exe");string source=System.Reflection.Assembly.GetExecutingAssembly().Location;
  try{Directory.CreateDirectory(Settings.Root);if(!String.Equals(source,target,StringComparison.OrdinalIgnoreCase)){if(File.Exists(source+".config"))InstallFiles.CopyAtomic(source+".config",target+".config");InstallFiles.CopyAtomic(source,target);}}
  catch(IOException){throw new SafeFailure("실행 파일 설치·업데이트에 실패했습니다. 기존 도우미를 완전히 종료한 뒤 다시 실행해 주세요. 로그인은 시작하지 않았습니다.");}
  catch(UnauthorizedAccessException){throw new SafeFailure("실행 파일 설치 위치에 쓸 수 없습니다. 권한이나 보안정책을 변경하지 않고 중단했습니다.");}
  // Desktop shortcut is optional; never overwrite another target or a reparse point.
  try{string desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"업무 시작 도우미.lnk");
   if(!InstallFiles.CanWriteShortcut(desktop,target))return target;
   ShortcutFile.Write(desktop,target,"","강원 업무 시작 도우미");
  }catch{}return target;
 }
}

internal sealed class RegisterDialog:Form {
 readonly System.Windows.Controls.PasswordBox first=new System.Windows.Controls.PasswordBox(),second=new System.Windows.Controls.PasswordBox();
 readonly ComboBox certificates=new ComboBox();readonly Label error=new Label();readonly Settings settings;
 readonly CertificateStorage location;readonly Action validateLocation;readonly Action<CertificateInfo> validateCertificate;
 internal RegisterDialog(Settings s,System.Collections.Generic.List<CertificateInfo> certs,CertificateStorage storage=null,Action validate=null,Action<CertificateInfo> validateIdentity=null){
  settings=s;location=storage??CertificateStorage.From(s);validateLocation=validate??(()=>{});validateCertificate=validateIdentity??(c=>{});Text="인증서 확인 · 암호 저장";Width=620;Height=435;StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  Font=new System.Drawing.Font("맑은 고딕",10);
  Controls.Add(new Label{Text="인증서를 확인했어요. 본인 이름·만료일·발급자를 확인하세요.\n강원 업무포털에서 사용할 암호를 아래 두 칸에 입력해 주세요.",Left=22,Top=18,Width=560,Height=55});
  Controls.Add(new Label{Text="저장 위치: "+location.Display,Left=22,Top=78,Width=555,Height=26,ForeColor=System.Drawing.Color.FromArgb(31,91,163)});
  certificates.SetBounds(22,110,555,32);certificates.DropDownStyle=ComboBoxStyle.DropDownList;foreach(var c in certs)certificates.Items.Add(c);if(certs.Count==1)certificates.SelectedIndex=0;Controls.Add(certificates);
  Controls.Add(new Label{Text="인증서 암호",Left=22,Top=162,Width=150});Controls.Add(new Label{Text="암호 다시 입력",Left=22,Top=215,Width=150});
  AddSecret(first,175,155);AddSecret(second,175,208);
  Controls.Add(new Label{Text="암호는 이 Windows 계정의 자격 증명 관리자에 저장됩니다.\n다음부터 저장한 위치를 선택하고 인증서를 확인합니다.",Left=22,Top=256,Width=555,Height=48});
  error.SetBounds(22,306,555,30);error.ForeColor=System.Drawing.Color.Firebrick;Controls.Add(error);
  var save=new Button{Text="암호 저장",Left=315,Top=342,Width=140,Height=32};save.Click+=(o,e)=>SaveSecret();Controls.Add(save);
  var cancel=new Button{Text="이전으로",Left=468,Top=342,Width=110,Height=32,DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
 }
 void AddSecret(System.Windows.Controls.PasswordBox p,int x,int y){p.FontSize=19;p.Padding=new System.Windows.Thickness(5);p.ContextMenu=null;
  System.Windows.DataObject.AddPastingHandler(p,(o,e)=>e.CancelCommand());
  p.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Copy,(o,e)=>e.Handled=true,(o,e)=>{e.CanExecute=false;e.Handled=true;}));
  p.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Cut,(o,e)=>e.Handled=true,(o,e)=>{e.CanExecute=false;e.Handled=true;}));
  var h=new ElementHost{Left=x,Top=y,Width=402,Height=37,Child=p};Controls.Add(h);
 }
 void SaveSecret(){try{
  var cert=certificates.SelectedItem as CertificateInfo;if(cert==null)throw new SafeFailure("인증서를 선택해 주세요.");
  OperationSafety.RequireResolved(settings.PendingAttempt);validateLocation();validateCertificate(cert);
  using(var a=first.SecurePassword)using(var b=second.SecurePassword){if(a.Length==0||!Vault.Equal(a,b))throw new SafeFailure("암호 두 칸의 입력이 일치하는지 확인해 주세요.");
   string oldTarget=settings.Target;string oldUser=settings.CertUser;
   var candidate=new Settings{CertUser=cert.User,CertExpiry=cert.Expiry,CertIssuer=cert.Issuer};
   CertificateRegistration.Commit(settings,cert,location,()=>{validateLocation();validateCertificate(cert);},settings.Save,()=>Vault.Save(candidate.Target,cert.User,a));
   if(oldUser!=""&&oldTarget!=settings.Target)try{Vault.Remove(oldTarget);}catch(SafeFailure){AppState.Record("OLD_CREDENTIAL_CLEANUP_INCOMPLETE");}
  }
  first.Clear();second.Clear();AppState.Record("REGISTERED");RunTrace.Record(TraceEvent.CERTIFICATE_REGISTRATION_COMPLETE,TraceStage.REGISTERED_CERTIFICATE);DialogResult=DialogResult.OK;Close();
 }catch(SafeFailure ex){error.Text=ex.Message;}catch(Exception){error.Text="등록 중 오류가 발생했습니다. 저장 여부를 확인해 주세요.";}}
 protected override void OnFormClosed(FormClosedEventArgs e){first.Clear();second.Clear();base.OnFormClosed(e);}
}

internal sealed class MainWindow:Form {
 enum SetupStep { Welcome, Certificate, Registered, Ready }
 readonly Settings settings;readonly Action persistSelection;
 readonly Label status=new Label(),cert=new Label(),guideTitle=new Label(),guideText=new Label(),progress=new Label(),startupNote=new Label();
 readonly CheckBox neis=new CheckBox(),kedu=new CheckBox();
 readonly Button next=new Button(),back=new Button(),setup=new Button(),options=new Button(),stop=new Button();
 NotifyIcon tray;ContextMenuStrip trayMenu;ToolStripMenuItem trayProgress,trayStop,traySettings,trayExit;System.Drawing.Icon trayIcon;
 readonly Panel guide=new Panel();readonly Label hotkeyNote=new Label();bool busy,restoringSelection;string lastStatus="";SetupStep step;StopHotkeyWindow stopHotkey;WorkProgressWindow workProgress;
 internal MainWindow(Settings s,Action saveSelection=null){
  settings=s;persistSelection=saveSelection??s.Save;step=s.CertUser==""||!Vault.Exists(s.Target)?SetupStep.Welcome:(s.Verified?SetupStep.Ready:SetupStep.Registered);
  Text="강원 업무 시작 도우미 · Edge 전용";ClientSize=new System.Drawing.Size(660,645);StartPosition=FormStartPosition.CenterScreen;
  Font=new System.Drawing.Font("맑은 고딕",10);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;BackColor=System.Drawing.Color.White;
  Controls.Add(new Label{Text="업무 시작 도우미",Left=24,Top=21,Width=600,Height=34,Font=new System.Drawing.Font("맑은 고딕",17,System.Drawing.FontStyle.Bold)});
  Controls.Add(new Label{Text="한 번 등록하면, 업무포털 로그인과 시스템 열기를 함께 진행해요.",Left=25,Top=62,Width=600,Height=26,ForeColor=System.Drawing.Color.DimGray});
  guide.SetBounds(24,104,612,267);guide.BackColor=System.Drawing.Color.FromArgb(240,246,253);Controls.Add(guide);
  progress.SetBounds(20,17,570,24);progress.ForeColor=System.Drawing.Color.FromArgb(46,87,140);guide.Controls.Add(progress);
  guideTitle.SetBounds(20,52,570,34);guideTitle.Font=new System.Drawing.Font("맑은 고딕",15,System.Drawing.FontStyle.Bold);guide.Controls.Add(guideTitle);
  guideText.SetBounds(20,98,570,83);guide.Controls.Add(guideText);
  next.SetBounds(20,190,570,43);next.Font=new System.Drawing.Font("맑은 고딕",11,System.Drawing.FontStyle.Bold);next.BackColor=System.Drawing.Color.FromArgb(31,91,163);next.ForeColor=System.Drawing.Color.White;next.FlatStyle=FlatStyle.Flat;
  next.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Welcome)OpenPortal();else if(step==SetupStep.Certificate)Register();else Start(false);}};guide.Controls.Add(next);
  back.SetBounds(280,236,310,26);back.FlatStyle=FlatStyle.Flat;back.FlatAppearance.BorderSize=0;back.ForeColor=System.Drawing.Color.FromArgb(31,91,163);back.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Certificate)OpenPortal();else BeginSetup();}};guide.Controls.Add(back);
  neis.Text="나이스 열기";neis.SetBounds(27,391,190,28);neis.Checked=s.OpenNeis;Controls.Add(neis);
  kedu.Text="K-에듀파인 열기";kedu.SetBounds(246,391,300,28);kedu.Checked=s.OpenKedu;Controls.Add(kedu);
  cert.SetBounds(27,430,600,29);cert.ForeColor=System.Drawing.Color.DimGray;Controls.Add(cert);
  startupNote.SetBounds(27,465,605,30);startupNote.ForeColor=System.Drawing.Color.DimGray;Controls.Add(startupNote);
  setup.SetBounds(24,506,238,32);setup.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Welcome||step==SetupStep.Certificate){step=settings.CertUser==""?SetupStep.Welcome:(settings.Verified?SetupStep.Ready:SetupStep.Registered);UpdateGuide();}else BeginSetup();}};Controls.Add(setup);
  options.Text="자동 시작 · 설정";options.SetBounds(276,506,195,32);options.Click+=(o,e)=>{if(!busy)ShowSettings();};Controls.Add(options);
  stop.Text="중단";stop.SetBounds(485,506,150,32);stop.Click+=(o,e)=>RequestStop();Controls.Add(stop);
  status.SetBounds(25,550,610,55);status.ForeColor=System.Drawing.Color.FromArgb(55,65,81);Controls.Add(status);
  hotkeyNote.SetBounds(25,611,610,28);hotkeyNote.ForeColor=System.Drawing.Color.DimGray;Controls.Add(hotkeyNote);
  RefreshCertificate();UpdateGuide();
  Portal.Report=msg=>{if(!IsDisposed)BeginInvoke((Action)(()=>SetStatus(msg)));};
  FormClosing+=(o,e)=>{if(busy){RequestStop();e.Cancel=true;}};
  Shown+=(o,e)=>CreateTray();
  neis.CheckedChanged+=(o,e)=>SaveSelection();kedu.CheckedChanged+=(o,e)=>SaveSelection();
  SetStatus("");
 }
 void SetStatus(string message){lastStatus=message;status.Text=AppState.WithLogNotice(message);if(workProgress!=null)workProgress.UpdateProgress(message);UpdateTray();}
 void CreateTray(){if(tray!=null)return;trayIcon=AppVisuals.CreateIcon();Icon=trayIcon;trayMenu=new ContextMenuStrip();trayProgress=new ToolStripMenuItem("업무 시작 대기"){Enabled=false};trayStop=new ToolStripMenuItem("중단",null,(o,e)=>RequestStop());traySettings=new ToolStripMenuItem("설정 열기",null,(o,e)=>{ShowHelper();OpenPreferences();});trayExit=new ToolStripMenuItem("종료",null,(o,e)=>Close());
  trayMenu.Items.AddRange(new ToolStripItem[]{trayProgress,new ToolStripSeparator(),new ToolStripMenuItem("도우미 열기",null,(o,e)=>ShowHelper()),trayStop,traySettings,trayExit});tray=new NotifyIcon{Icon=trayIcon,Text="업무 시작 도우미 · 시작 대기",ContextMenuStrip=trayMenu,Visible=true};AppVisuals.OpenMenuOnClick(tray,trayMenu);tray.DoubleClick+=(o,e)=>ShowHelper();UpdateTray();
 }
 void ShowHelper(){Show();WindowState=FormWindowState.Normal;Activate();}
 void UpdateTray(){if(tray==null)return;string message=String.IsNullOrEmpty(lastStatus)?"업무 시작 대기":lastStatus;trayProgress.Text=message;string hint="업무 시작 도우미 · "+message;tray.Text=hint.Length>63?hint.Substring(0,63):hint;trayStop.Enabled=busy;traySettings.Enabled=!busy;trayExit.Enabled=!busy;}
 internal void ShowNotice(string message){SetStatus(message);}
 internal void OpenPreferences(){if(!busy)ShowSettings();}
 void RequestStop(){if(!busy){SetStatus("진행 중인 작업이 없습니다.");return;}bool started=Portal.RequestStop();SetStatus(started?"제출이 이미 시작되어 취소할 수 없습니다. 후속 작업을 중단하며 자동 재제출하지 않습니다.":"중단을 요청했어요. 새 제출은 시작하지 않습니다.");}
 void SaveSelection(){if(restoringSelection||busy)return;try{CaptureSettings();}catch(Exception){restoringSelection=true;try{neis.Checked=settings.OpenNeis;kedu.Checked=settings.OpenKedu;}finally{restoringSelection=false;}SetStatus("열 시스템 선택을 저장하지 못해 이전 선택으로 표시했습니다. 다시 시작할 때 저장된 선택을 확인해 주세요.");}}
 void UpdateGuide(){
  progress.Text="처음 한 번만   ① 포털 열기 → ② 인증서 확인·암호 저장";back.Visible=true;
  if(step==SetupStep.Welcome){guideTitle.Text="먼저, 인증서를 등록해 주세요";guideText.Text="① 아래 버튼을 누르면 업무용 Edge 창이 열려요.\n② 업무포털에서 인증서 로그인 버튼을 눌러 주세요.\n③ 인증서 목록이 보이면 도우미에서 확인하고 암호를 저장해요.";next.Text="업무포털 열고 등록 시작";back.Visible=false;}
  else if(step==SetupStep.Certificate){guideTitle.Text="업무포털에서 인증서창을 열어 주세요";guideText.Text="① Edge의 '교육행정 전자서명 인증서 로그인'을 누르세요.\n② 인증서 이름과 만료일이 보이는 목록까지 열어 주세요.\n③ 이 도우미로 돌아와 아래 '인증서 확인'을 누르세요.";next.Text="인증서창을 열었어요 · 인증서 확인";back.Text="업무포털 다시 보기";}
  else if(step==SetupStep.Registered){guideTitle.Text="암호 등록이 끝났어요";guideText.Text="이제 아래 '업무 시작' 버튼을 눌러 주세요.\n도우미가 로그인하고 선택한 시스템을 열어 드립니다.\n다음부터는 '업무 시작'만 누르면 됩니다.";next.Text="업무 시작";back.Text="인증서 다시 등록하기";}
  else {progress.Text="등록 완료 · 다음부터는 버튼 하나로 시작해요";guideTitle.Text="오늘 업무를 시작할까요?";guideText.Text="업무포털에 로그인하고 아래 선택한 시스템을 열어요.\n진행 중에는 작은 안내창이 표시됩니다.\n완료될 때까지 업무용 Edge를 앞에 두고 잠시 기다려 주세요.";next.Text="업무 시작";back.Text="처음 설정 안내 보기";}
  setup.Visible=settings.CertUser!="";
  setup.Text=step==SetupStep.Welcome||step==SetupStep.Certificate?"기존 설정으로 돌아가기":"인증서·암호 다시 등록";
 }
 internal void BeginSetup(){if(busy)return;step=SetupStep.Welcome;UpdateGuide();SetStatus("안내에 따라 한 단계씩 진행해 주세요.");}
 void RefreshCertificate(){
  string position="";try{if(settings.CertStorageConfigured)position=" · "+CertificateStorage.From(settings).Display;}catch(SafeFailure){position=" · 위치 확인 필요";}
  cert.Text=settings.CertUser==""?"인증서는 안내 2단계에서 등록합니다.":"인증서: "+settings.CertUser+" · "+settings.CertExpiry+position;
  startupNote.Text=DescribeStartup();
 }
 string DescribeStartup(){try{var c=Autostart.Controller;if(c.NeedsDelayUpdate)return "이전 즉시 실행 등록 · 켜기를 누르면 10초 지연을 적용해요.";if(c.IsFast)return settings.AutoRunAtLogon?"자동 시작 켜짐 · 로그온 10초 뒤 로그인·업무 시작":"자동 시작 켜짐 · 로그온 10초 뒤 도우미만 열기";if(c.HasTask||StartupRegistration.HasEntry)return "확인이 필요한 시작 항목이 있어요. 자동 시작 설정을 확인하세요.";return "자동 시작 꺼짐 · 필요하면 설정에서 켜 주세요.";}catch(Exception){return "자동 시작 상태를 확인하지 못했어요. 설정을 확인해 주세요.";}}
 void ChangeStartup(bool enabled){if(busy)return;
  AppState.Install();var controller=Autostart.Controller;
  if(enabled)controller.Enable();else controller.Remove();
  settings.StartAtLogon=Autostart.IsRegistered;settings.Save();AppState.Record(enabled?"FAST_STARTUP_ENABLED_BY_USER":"STARTUP_REMOVED_BY_USER");RefreshCertificate();
 }
 void CaptureSettings(){SelectionPreferences.Save(settings,neis.Checked,kedu.Checked,persistSelection);}
 void SetBusy(bool value){busy=value;foreach(var b in new[]{next,back,setup,options})b.Enabled=!value;neis.Enabled=kedu.Enabled=!value;UpdateTray();}
 void FinishProgress(){Native.PointerNoticeVisibility=visible=>{};if(workProgress!=null){workProgress.Finish();workProgress=null;}}
 void Work(Action action,Action completed=null,bool showProgress=false){if(busy)return;try{CaptureSettings();}catch(Exception){SetStatus("선택과 필수 상태를 저장하지 못해 작업을 시작하지 않았습니다.");ShowHelper();return;}Portal.BeginOperation();SetBusy(true);
  System.ComponentModel.BackgroundWorker worker=null;
  try{
   if(showProgress){workProgress=new WorkProgressWindow(RequestStop);workProgress.Show();Native.PointerNoticeVisibility=workProgress.SetPointerNoticeVisible;}
   worker=new System.ComponentModel.BackgroundWorker();worker.DoWork+=(o,e)=>action();worker.RunWorkerCompleted+=(o,e)=>{FinishProgress();SetBusy(false);RefreshCertificate();if(e.Error!=null){RunTrace.Record(TraceEvent.ERROR,failure:e.Error);AppState.Record("STOPPED");SetStatus(e.Error is SafeFailure?e.Error.Message:("작업을 진행하지 못했어요. 오류 유형: "+e.Error.GetType().Name+" · 자동으로 다시 실행하지 않습니다."));ShowHelper();}else{if(completed!=null)completed();SetStatus(lastStatus);}worker.Dispose();};worker.RunWorkerAsync();
  }catch(Exception ex){if(worker!=null)worker.Dispose();FinishProgress();SetBusy(false);RunTrace.Record(TraceEvent.ERROR,failure:ex);SetStatus("진행 안내를 준비하지 못해 시작하지 않았습니다. 자동으로 다시 실행하지 않습니다.");ShowHelper();}
 }
 void ExplainCertificateStep(bool loggedIn){
  step=SetupStep.Certificate;UpdateGuide();
  if(loggedIn)guideText.Text="지금은 업무포털에 로그인되어 있어요.\n업무포털에서 로그아웃한 뒤 인증서 로그인 버튼을 누르세요.\n인증서 목록이 보이면 아래 버튼을 눌러 주세요.";
  SetStatus("인증서 목록을 연 뒤 이 도우미로 돌아와 주세요.");
 }
 void OpenPortal(){Work(()=>{var c=Portal.OpenForSetup(settings.Profile);Invoke((Action)(()=>ExplainCertificateStep(c.Path==Portal.MainPath)));});}
 internal void Register(){RegisterAt(null,null);}
 internal void RegisterAt(long? existingWindow,CertificateStorage requested){Work(()=>{
  OperationSafety.RequireResolved(settings.PendingAttempt);
  PortalContext c;
  if(existingWindow.HasValue){Portal.BindProbeWindow(settings,existingWindow.Value);c=Portal.Locate(settings.Profile);}else c=Portal.OpenForRegistration(settings.Profile);
  if(c==null)throw new SafeFailure("등록할 업무포털 인증서 화면을 확인하지 못했습니다.");var d=Portal.Dialog(c);
  if(c.Path!=Portal.LoginPath||d==null||d.Current.IsOffscreen){Invoke((Action)(()=>ExplainCertificateStep(c.Path==Portal.MainPath)));return;}
  CertificateStorage location=requested;
  if(location==null)Invoke((Action)(()=>{CertificateStorage previous;try{previous=CertificateStorage.From(settings);}catch(SafeFailure){previous=new CertificateStorage(CertificateStorage.HardDisk,"");}using(var select=new CertificateStorageDialog(previous)){if(select.ShowDialog(this)==DialogResult.OK)location=select.Choice;}}));
  if(location==null){Invoke((Action)(()=>SetStatus("저장 위치 선택을 취소했어요. 기존 등록은 유지합니다.")));return;}
  var prepared=Portal.SelectCertificateStorage(c,location);var certs=Portal.Certificates(prepared.Context,true);
  Invoke((Action)(()=>{Activate();using(var dialog=new RegisterDialog(settings,certs,location,()=>Portal.ValidatePreparedStorage(prepared),selected=>Portal.ValidatePreparedCertificate(prepared,selected))){
   if(dialog.ShowDialog(this)==DialogResult.OK){step=SetupStep.Registered;UpdateGuide();SetStatus(location.Display+" 인증서와 암호를 저장했어요. '업무 시작'을 눌러 주세요.");RefreshCertificate();}
   else {step=SetupStep.Certificate;UpdateGuide();SetStatus("준비되면 암호 등록을 이어서 진행해 주세요.");}
  }}));
 });}
 internal void Start(bool testing){
  if(busy)return;
  if(Native.GetForegroundWindow()==Handle){WindowState=FormWindowState.Minimized;IntPtr previous=Native.GetForegroundWindow();if(previous!=IntPtr.Zero&&previous!=Handle)Portal.ReturnWindowHint=previous;}
  Work(()=>{AppState.Record(testing?"TEST_STARTED":"RUN_STARTED");if(Portal.Run(settings,testing)){AppState.Record(testing?"TEST_SUCCESS":"RUN_SUCCESS");}},()=>{step=settings.Verified?SetupStep.Ready:SetupStep.Registered;UpdateGuide();},true);
 }
 void ShowSettings(){
  using(var dialog=new Form{Text="자동 시작 · 설정",ClientSize=new System.Drawing.Size(592,512),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font}){
   var startup=new StartupOptions(settings,()=>{persistSelection();RefreshCertificate();},ChangeStartup,DescribeStartup){Left=20,Top=20};dialog.Controls.Add(startup);
   dialog.Controls.Add(new Label{Text="Edge 프로필 · 여러 프로필을 쓰는 경우에만 변경하세요.",Left=20,Top=270,Width=550});
   var profiles=new ComboBox{Left=20,Top=302,Width=550,DropDownStyle=ComboBoxStyle.DropDownList};profiles.Items.Add("Default");
   string userData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Edge","User Data");
   if(Directory.Exists(userData))foreach(var path in Directory.GetDirectories(userData,"Profile *")){string folder=Path.GetFileName(path);if(System.Text.RegularExpressions.Regex.IsMatch(folder,@"^Profile \d+$"))profiles.Items.Add(folder);}
   profiles.SelectedItem=settings.Profile;if(profiles.SelectedIndex<0)profiles.SelectedIndex=0;dialog.Controls.Add(profiles);
   var reset=new Button{Text="오류 상태 초기화",Left=20,Top=357,Width=267,Height=36};reset.Click+=(o,e)=>{if(MessageBox.Show(dialog,"현재 인증서창의 오류·입력 내용을 직접 확인하셨나요?\n초기화 후 다음 실행은 새로운 1회 시도입니다.","입력 결과 확인",MessageBoxButtons.YesNo)==DialogResult.Yes){settings.PendingAttempt=false;settings.Save();AppState.Record("RESET_BY_USER");SetStatus("오류 상태를 초기화했어요.");}};dialog.Controls.Add(reset);
   var remove=new Button{Text="저장한 암호 삭제",Left=303,Top=357,Width=267,Height=36};remove.Click+=(o,e)=>{if(MessageBox.Show(dialog,"이 도우미가 저장한 인증서 암호만 삭제할까요?",Text,MessageBoxButtons.YesNo)==DialogResult.Yes){Vault.Remove(settings.Target);settings.CertUser=settings.CertExpiry=settings.CertIssuer="";settings.Verified=false;settings.PendingAttempt=false;settings.CertMedia=CertificateStorage.HardDisk;settings.CertDrive="";settings.CertStorageConfigured=false;settings.RegistrationPending=false;settings.Save();step=SetupStep.Welcome;RefreshCertificate();UpdateGuide();AppState.Record("CREDENTIAL_REMOVED");}};dialog.Controls.Add(remove);
   dialog.Controls.Add(new Label{Text="자동 시작 선택은 즉시 저장됩니다. 로그인은 새로 시도하지 않아요.",Left=20,Top=414,Width=550,Height=28,ForeColor=System.Drawing.Color.DimGray});
   var close=new Button{Text="닫기 · 프로필 저장",Left=303,Top=459,Width=267,Height=36,DialogResult=DialogResult.OK};dialog.Controls.Add(close);
   if(dialog.ShowDialog(this)==DialogResult.OK){string previousProfile=settings.Profile;settings.Profile=profiles.SelectedItem.ToString();try{CaptureSettings();}catch(Exception){settings.Profile=previousProfile;SetStatus("설정을 저장하지 못했습니다. 이전 프로필을 유지합니다.");}}
  }
 }
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Portal.HelperWindow=Handle;stopHotkey=new StopHotkeyWindow(RequestStop);hotkeyNote.Text=stopHotkey.Registered?"중단: Ctrl+Alt+F10 또는 중단 버튼":"단축키를 등록하지 못했습니다. 중단 버튼을 사용해 주세요.";}
 protected override void OnHandleDestroyed(EventArgs e){if(stopHotkey!=null)stopHotkey.Dispose();base.OnHandleDestroyed(e);}
 protected override void Dispose(bool disposing){if(disposing){if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}if(trayMenu!=null){trayMenu.Dispose();trayMenu=null;}if(trayIcon!=null){trayIcon.Dispose();trayIcon=null;}}base.Dispose(disposing);}
}

internal static class Program {
 [STAThread]static int Main(string[] args){
  RunTrace.Start(args);
  try{
   Portal.ReturnWindowHint=Native.GetForegroundWindow();
   #if CONSOLE
   Console.OutputEncoding=new UTF8Encoding(false);
   #endif
   if(args.Contains("--logging-self-check")){RunTrace.Record(TraceEvent.LOGGING_CHECK);Console.WriteLine(AppState.HasLogFailure?"TRACE_WRITE_FAILED":"TRACE_WRITE_OK");return AppState.HasLogFailure?3:0;}
   if(args.Contains("--self-test")){if(Marshal.SizeOf(typeof(Native.INPUT))!=40)throw new SafeFailure("Windows 입력 구조체 크기가 맞지 않습니다.");Vault.SelfTest();Console.WriteLine("PASS: x64 input structure; random credential round-trip, local direct setter, guards and cleanup; no secret printed");return 0;}
   if(args.Contains("--status")){var s=Settings.Load();Console.WriteLine(new JavaScriptSerializer().Serialize(new {registered=s.CertUser!=""&&Vault.Exists(s.Target),verified=s.Verified,pending=s.PendingAttempt,startAtLogon=Autostart.IsRegistered,fastStartup=Autostart.Controller.IsFast,legacyStartup=StartupRegistration.LegacyPresent}));return 0;}
   using(var lease=new InstanceLease("Local\\TeacherWorkStart-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){
    if(!lease.Owned){
     RunTrace.Record(TraceEvent.DUPLICATE_EXIT);
     const string duplicate="업무 시작 도우미가 이미 실행 중입니다. 기존 도우미를 완전히 종료한 뒤 다시 실행·업데이트해 주세요.";
     #if CONSOLE
     Console.WriteLine(duplicate);
     #else
     if(!args.Contains("--auto"))MessageBox.Show(duplicate,"업무 시작 도우미");
     #endif
     return 2;
    }
   RunTrace.Record(TraceEvent.INSTANCE_ACQUIRED);
   if(args.Length==2&&args[0]=="--finish-install"){
    if(Native.IsPackaged)throw new SafeFailure("일반 바탕화면 환경에서 설치 확인을 실행해야 합니다.");
    var migrated=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(args[1],Encoding.UTF8));
    bool credentialAvailable=migrated.CertUser!=""&&Vault.Exists(migrated.Target);
    if(!credentialAvailable)migrated.Verified=false;
    migrated.StartAtLogon=Autostart.IsRegistered;migrated.Save();string installed=AppState.Install();
    File.WriteAllText(Path.Combine(Settings.Root,"installation-check.json"),new JavaScriptSerializer().Serialize(new {unpackaged=true,credentialAvailable=credentialAvailable,verified=migrated.Verified,exe=installed,startupRegistered=Autostart.IsRegistered}),new UTF8Encoding(false));
    lease.Release();Process.Start(new ProcessStartInfo(installed,"--show"){UseShellExecute=true});return 0;
   }
   if(args.Contains("--install-register")){string exe=AppState.Install();lease.Release();Process.Start(new ProcessStartInfo(exe,"--register-only"){UseShellExecute=true});return 0;}
   if(args.Contains("--install-only")){RunTrace.Record(TraceEvent.INSTALL_BEGIN);AppState.Install();RunTrace.Record(TraceEvent.INSTALL_COMPLETE);return 0;}
   if(!args.Any()){string actual=System.Reflection.Assembly.GetExecutingAssembly().Location;string installed=AppState.Install();if(!String.Equals(actual,installed,StringComparison.OrdinalIgnoreCase)){lease.Release();Process.Start(new ProcessStartInfo(installed){UseShellExecute=true});return 0;}}
    RunTrace.Record(TraceEvent.SETTINGS_LOAD_BEGIN);var settings=Settings.Load();RunTrace.Record(TraceEvent.SETTINGS_LOADED);
    if(args.Length==2&&args[0]=="--storage-state-existing"){
     Portal.BeginOperation();Portal.BindProbeWindow(settings,Int64.Parse(args[1]));
     File.WriteAllText(Path.Combine(Settings.Root,"storage-state.json"),new JavaScriptSerializer().Serialize(Portal.StorageViewFacts(Portal.Locate(settings.Profile))),new UTF8Encoding(false));return 0;
    }
    if(args.Length==3&&args[0]=="--storage-check-existing"){
     Portal.BeginOperation();OperationSafety.RequireResolved(settings.PendingAttempt);Portal.BindProbeWindow(settings,Int64.Parse(args[1]));
     var prepared=Portal.SelectCertificateStorage(Portal.Locate(settings.Profile),new CertificateStorage(CertificateStorage.Removable,args[2]));
     var certificates=Portal.Certificates(prepared.Context,true);
     var result=new {version=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),pid=Process.GetCurrentProcess().Id,media=prepared.Location.Media,drive=prepared.Location.Drive,freshListVerified=true,certificateCount=certificates.Count,registeredCertificatePresent=certificates.Any(c=>c.User==settings.CertUser&&c.Expiry==settings.CertExpiry&&c.Issuer==settings.CertIssuer),mediaDirect=prepared.MediaDirect,driveDirect=prepared.DriveDirect,passwordRead=false,passwordWritten=false,submitted=false,settingsWritten=false};
     File.WriteAllText(Path.Combine(Settings.Root,"storage-check.json"),new JavaScriptSerializer().Serialize(result),new UTF8Encoding(false));return 0;
    }
    if(args.Length==3&&args[0]=="--strict-session")return StrictSession.Execute(settings,Int64.Parse(args[1]),args[2]);
    if(args.Length==2&&args[0]=="--open-systems-existing"){Portal.Report=msg=>Console.WriteLine(msg);Portal.OpenExistingSystems(settings,Int64.Parse(args[1]));Console.WriteLine("SYSTEM_MENU_REQUESTS_DISPATCHED");return 0;}
    if(args.Length==2&&args[0]=="--background-once"){Portal.Report=msg=>Console.WriteLine(msg);Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.BackgroundOnce(settings,Int64.Parse(args[1]))));return 0;}
    if(args.Length==2&&args[0]=="--probe-background-existing"){Portal.BindProbeWindow(settings,Int64.Parse(args[1]));Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.ProbeBackground(settings)));return 0;}
    if(args.Contains("--probe-background")){Portal.Report=msg=>Console.WriteLine(msg);Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.ProbeBackground(settings)));return 0;}
    if(args.Contains("--prepare")){var c=Portal.Prepare(settings.Profile);foreach(var cert in Portal.Certificates(c))Console.WriteLine(cert.ToString());AppState.Record("PREPARED");return 0;}
    if(args.Contains("--run-once")){bool testing=args.Contains("--test");Portal.Report=msg=>Console.WriteLine(msg);AppState.Record(testing?"TEST_STARTED":"RUN_STARTED");Portal.Run(settings,testing);AppState.Record(testing?"TEST_SUCCESS":"RUN_SUCCESS");return 0;}
    if(args.Contains("--run-legacy-once")){Portal.Report=msg=>Console.WriteLine(msg);Portal.Run(settings,args.Contains("--test"),false);return 0;}
    if(args.Contains("--restore-startup"))throw new SafeFailure("기존 Startup 방식으로 복원하는 기능은 제거했습니다. 자동 시작 켜기·끄기를 사용해 주세요.");
    if(args.Contains("--enable-fast-startup")||args.Contains("--uninstall-startup")){
     var controller=Autostart.Controller;if(args.Contains("--enable-fast-startup"))controller.Enable();else controller.Remove();
     settings.StartAtLogon=Autostart.IsRegistered;settings.Save();AppState.Record(args.Contains("--enable-fast-startup")?"FAST_STARTUP_ENABLED_BY_USER":"STARTUP_REMOVED_BY_USER");Console.WriteLine("AUTOSTART_CONFIGURATION_UPDATED");return 0;
    }
    if(args.Contains("--auto"))AppState.Record("AUTO_LAUNCHED");
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(args.Length==3&&args[0]=="--register-storage-existing"){
     long existingWindow=Int64.Parse(args[1]);var location=new CertificateStorage(CertificateStorage.Removable,args[2]);RunTrace.Record(TraceEvent.MAIN_WINDOW_OPEN);var registration=new MainWindow(settings);
     registration.Shown+=(o,e)=>registration.BeginInvoke((Action)(()=>registration.RegisterAt(existingWindow,location)));Application.Run(registration);return 0;
    }
    bool setup=args.Contains("--register")||args.Contains("--register-only")||args.Contains("--setup");
    if(args.Contains("--auto")&&!setup){RunTrace.Record(TraceEvent.AUTO_CONTEXT_OPEN);using(var auto=new AutoRunContext(settings))Application.Run(auto);return 0;}
    RunTrace.Record(TraceEvent.MAIN_WINDOW_OPEN);
    var form=new MainWindow(settings);
    form.Shown+=(o,e)=>{if(setup)form.BeginSetup();};
    Application.Run(form);return 0;
   }
  }catch(SafeFailure e){RunTrace.Record(TraceEvent.ERROR,failure:e);AppState.Record("STOPPED");string message=AppState.WithLogNotice(e.Message);Console.WriteLine(message);
   #if !CONSOLE
   MessageBox.Show(message,"업무 시작 도우미 · 중단");
   #endif
   return 3;
  }catch(Exception e){RunTrace.Record(TraceEvent.ERROR,failure:e);AppState.Record("UNEXPECTED_STOP");string message=AppState.WithLogNotice("안전하게 중단했습니다. 오류 유형: "+e.GetType().Name);Console.WriteLine(message);
   #if !CONSOLE
   MessageBox.Show(message+"\n자동으로 재시도하지 않습니다.","업무 시작 도우미 · 중단");
   #endif
   return 4;}finally{RunTrace.Record(TraceEvent.PROCESS_EXIT);}
 }
}
