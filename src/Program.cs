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
 public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TeacherWorkStart");}}
 public static string FilePath {get{return Path.Combine(Root,"settings.json");}}
 [ScriptIgnore] public string Target {get{using(var hash=SHA256.Create()){var b=hash.ComputeHash(Encoding.UTF8.GetBytes(Portal.Origin+"\n"+CertUser+"\n"+CertExpiry+"\n"+CertIssuer));return "TeacherWorkStart/gwe/"+BitConverter.ToString(b).Replace("-","");}}}
 public static Settings Load(){if(!File.Exists(FilePath))return new Settings();try{return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath,Encoding.UTF8));}catch{throw new SafeFailure("설정 파일을 읽지 못했습니다. 자동 로그인을 시작하지 않습니다.");}}
 public void Save(){SaveTo(FilePath);}
 internal void SaveTo(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
}

internal static class AppState {
 static readonly object sync=new object();static bool logFailed;
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
 internal RegisterDialog(Settings s,System.Collections.Generic.List<CertificateInfo> certs){
  settings=s;Text="인증서 암호 저장";Width=620;Height=395;StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  Font=new System.Drawing.Font("맑은 고딕",10);
  Controls.Add(new Label{Text="사용할 곳: 강원 업무포털 (gwe.eduptl.kr)\n본인 인증서인지 확인하고, 아래 두 칸에 암호를 같게 입력해 주세요.",Left=22,Top=18,Width=560,Height=55});
  certificates.SetBounds(22,78,555,32);certificates.DropDownStyle=ComboBoxStyle.DropDownList;foreach(var c in certs)certificates.Items.Add(c);if(certs.Count==1)certificates.SelectedIndex=0;Controls.Add(certificates);
  Controls.Add(new Label{Text="인증서 암호",Left=22,Top=130,Width=150});Controls.Add(new Label{Text="암호 다시 입력",Left=22,Top=183,Width=150});
  AddSecret(first,175,123);AddSecret(second,175,176);
  Controls.Add(new Label{Text="암호는 이 Windows 계정의 자격 증명 관리자에 저장됩니다.\n저장한 뒤에는 도우미가 자동으로 입력합니다.",Left=22,Top=224,Width=555,Height=48});
  error.SetBounds(22,274,555,30);error.ForeColor=System.Drawing.Color.Firebrick;Controls.Add(error);
  var save=new Button{Text="저장하고 다음",Left=315,Top=310,Width=140,Height=32};save.Click+=(o,e)=>SaveSecret();Controls.Add(save);
  var cancel=new Button{Text="이전으로",Left=468,Top=310,Width=110,Height=32,DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
 }
 void AddSecret(System.Windows.Controls.PasswordBox p,int x,int y){p.FontSize=19;p.Padding=new System.Windows.Thickness(5);p.ContextMenu=null;
  System.Windows.DataObject.AddPastingHandler(p,(o,e)=>e.CancelCommand());
  p.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Copy,(o,e)=>e.Handled=true,(o,e)=>{e.CanExecute=false;e.Handled=true;}));
  p.CommandBindings.Add(new System.Windows.Input.CommandBinding(System.Windows.Input.ApplicationCommands.Cut,(o,e)=>e.Handled=true,(o,e)=>{e.CanExecute=false;e.Handled=true;}));
  var h=new ElementHost{Left=x,Top=y,Width=402,Height=37,Child=p};Controls.Add(h);
 }
 void SaveSecret(){try{
  var cert=certificates.SelectedItem as CertificateInfo;if(cert==null)throw new SafeFailure("인증서를 선택해 주세요.");
  using(var a=first.SecurePassword)using(var b=second.SecurePassword){if(a.Length==0||!Vault.Equal(a,b))throw new SafeFailure("암호 두 칸의 입력이 일치하는지 확인해 주세요.");
   string oldTarget=settings.Target;string oldUser=settings.CertUser;
   var candidate=new Settings{CertUser=cert.User,CertExpiry=cert.Expiry,CertIssuer=cert.Issuer};
   Vault.Save(candidate.Target,cert.User,a);
   settings.CertUser=cert.User;settings.CertExpiry=cert.Expiry;settings.CertIssuer=cert.Issuer;settings.Verified=false;settings.PendingAttempt=false;settings.Save();
   if(oldUser!=""&&oldTarget!=settings.Target)Vault.Remove(oldTarget);
  }
  first.Clear();second.Clear();AppState.Record("REGISTERED");DialogResult=DialogResult.OK;Close();
 }catch(SafeFailure ex){error.Text=ex.Message;}catch(Exception){error.Text="등록 중 오류가 발생했습니다. 저장 여부를 확인해 주세요.";}}
 protected override void OnFormClosed(FormClosedEventArgs e){first.Clear();second.Clear();base.OnFormClosed(e);}
}

internal sealed class MainWindow:Form {
 enum SetupStep { Welcome, Certificate, Registered, Ready }
 readonly Settings settings;readonly Action persistSelection;
 readonly Label status=new Label(),cert=new Label(),guideTitle=new Label(),guideText=new Label(),progress=new Label(),startupNote=new Label();
 readonly CheckBox neis=new CheckBox(),kedu=new CheckBox();
 readonly Button next=new Button(),back=new Button(),setup=new Button(),options=new Button(),stop=new Button(),enableStartup=new Button(),removeStartup=new Button();
 readonly Panel guide=new Panel();readonly Label hotkeyNote=new Label();bool busy,restoringSelection;string lastStatus="";SetupStep step;StopHotkeyRegistration stopHotkey;
 [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint vk);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
 internal MainWindow(Settings s,Action saveSelection=null){
  settings=s;persistSelection=saveSelection??s.Save;step=s.CertUser==""||!Vault.Exists(s.Target)?SetupStep.Welcome:(s.Verified?SetupStep.Ready:SetupStep.Registered);
  Text="강원 업무 시작 도우미 · Edge 전용";ClientSize=new System.Drawing.Size(660,705);StartPosition=FormStartPosition.CenterScreen;
  Font=new System.Drawing.Font("맑은 고딕",10);FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;BackColor=System.Drawing.Color.White;
  Controls.Add(new Label{Text="업무 시작 도우미",Left=24,Top=21,Width=600,Height=34,Font=new System.Drawing.Font("맑은 고딕",17,System.Drawing.FontStyle.Bold)});
  Controls.Add(new Label{Text="강원 업무포털 · Edge 전용 창 · 입력·제출 때 잠시 전면 실행",Left=25,Top=62,Width=600,Height=26,ForeColor=System.Drawing.Color.DimGray});
  guide.SetBounds(24,104,612,267);guide.BackColor=System.Drawing.Color.FromArgb(240,246,253);Controls.Add(guide);
  progress.SetBounds(20,17,570,24);progress.ForeColor=System.Drawing.Color.FromArgb(46,87,140);guide.Controls.Add(progress);
  guideTitle.SetBounds(20,52,570,34);guideTitle.Font=new System.Drawing.Font("맑은 고딕",15,System.Drawing.FontStyle.Bold);guide.Controls.Add(guideTitle);
  guideText.SetBounds(20,98,570,83);guide.Controls.Add(guideText);
  next.SetBounds(20,190,570,43);next.Font=new System.Drawing.Font("맑은 고딕",11,System.Drawing.FontStyle.Bold);next.BackColor=System.Drawing.Color.FromArgb(31,91,163);next.ForeColor=System.Drawing.Color.White;next.FlatStyle=FlatStyle.Flat;
  next.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Welcome)OpenPortal();else if(step==SetupStep.Certificate)Register();else Start(false);}};guide.Controls.Add(next);
  back.SetBounds(280,236,310,26);back.FlatStyle=FlatStyle.Flat;back.FlatAppearance.BorderSize=0;back.ForeColor=System.Drawing.Color.FromArgb(31,91,163);back.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Certificate)OpenPortal();else BeginSetup();}};guide.Controls.Add(back);
  neis.Text="나이스 열기";neis.SetBounds(27,391,190,28);neis.Checked=s.OpenNeis;Controls.Add(neis);
  kedu.Text="K-에듀파인 열기";kedu.SetBounds(246,391,300,28);kedu.Checked=s.OpenKedu;Controls.Add(kedu);
  enableStartup.Text="자동 시작 켜기";enableStartup.SetBounds(27,430,285,36);enableStartup.Click+=(o,e)=>ChangeStartup(true);Controls.Add(enableStartup);
  removeStartup.Text="자동 시작 등록 삭제";removeStartup.SetBounds(326,430,309,36);removeStartup.Click+=(o,e)=>ChangeStartup(false);Controls.Add(removeStartup);
  startupNote.SetBounds(27,476,605,40);startupNote.ForeColor=System.Drawing.Color.DimGray;Controls.Add(startupNote);
  cert.SetBounds(27,523,600,29);cert.ForeColor=System.Drawing.Color.DimGray;Controls.Add(cert);
  setup.SetBounds(24,564,238,32);setup.Click+=(o,e)=>{if(!busy){if(step==SetupStep.Welcome||step==SetupStep.Certificate){step=settings.CertUser==""?SetupStep.Welcome:(settings.Verified?SetupStep.Ready:SetupStep.Registered);UpdateGuide();}else BeginSetup();}};Controls.Add(setup);
  options.Text="설정";options.SetBounds(276,564,125,32);options.Click+=(o,e)=>{if(!busy)ShowSettings();};Controls.Add(options);
  stop.Text="중단";stop.SetBounds(415,564,220,32);stop.Click+=(o,e)=>RequestStop();Controls.Add(stop);
  status.SetBounds(25,612,610,55);status.ForeColor=System.Drawing.Color.FromArgb(55,65,81);Controls.Add(status);
  hotkeyNote.SetBounds(25,668,610,28);hotkeyNote.ForeColor=System.Drawing.Color.DimGray;Controls.Add(hotkeyNote);
  RefreshCertificate();UpdateGuide();
  Portal.Report=msg=>{if(!IsDisposed)BeginInvoke((Action)(()=>SetStatus(msg)));};
  FormClosing+=(o,e)=>{if(busy){RequestStop();e.Cancel=true;}};
  neis.CheckedChanged+=(o,e)=>SaveSelection();kedu.CheckedChanged+=(o,e)=>SaveSelection();
  SetStatus("");
 }
 void SetStatus(string message){lastStatus=message;status.Text=AppState.WithLogNotice(message);}
 void RequestStop(){if(!busy){SetStatus("진행 중인 작업이 없습니다.");return;}bool started=Portal.RequestStop();SetStatus(started?"제출이 이미 시작되어 취소할 수 없습니다. 후속 작업을 중단하며 자동 재제출하지 않습니다.":"중단을 요청했어요. 새 제출은 시작하지 않습니다.");}
 void SaveSelection(){if(restoringSelection||busy)return;try{CaptureSettings();}catch(Exception){restoringSelection=true;try{neis.Checked=settings.OpenNeis;kedu.Checked=settings.OpenKedu;}finally{restoringSelection=false;}SetStatus("열 시스템 선택을 저장하지 못해 이전 선택으로 표시했습니다. 다시 시작할 때 저장된 선택을 확인해 주세요.");}}
 void UpdateGuide(){
  progress.Text="1  업무포털 열기   →   2  암호 등록   →   3  업무 시작";back.Visible=true;
  if(step==SetupStep.Welcome){guideTitle.Text="1. 업무포털을 열어 주세요";guideText.Text="처음에는 아래 버튼을 눌러 주세요.\n업무 전용 Edge 창을 열어 안내를 시작합니다.\n기존에 사용하던 Edge 창과 따로 진행합니다.";next.Text="업무포털 열기";back.Visible=false;}
  else if(step==SetupStep.Certificate){guideTitle.Text="2. 인증서창을 열어 주세요";guideText.Text="업무포털에서 '교육행정 전자서명 인증서 로그인'을 누르세요.\n인증서 목록이 보이면 이 도우미로 돌아와\n아래 버튼을 눌러 주세요.";next.Text="인증서창을 열었어요 · 암호 등록하기";back.Text="업무포털 다시 보기";}
  else if(step==SetupStep.Registered){guideTitle.Text="3. 암호 등록이 끝났어요";guideText.Text="이제 아래 '업무 시작' 버튼을 눌러 주세요.\n도우미가 로그인하고 선택한 시스템을 열어 드립니다.\n다음부터는 '업무 시작'만 누르면 됩니다.";next.Text="업무 시작";back.Text="인증서 다시 등록하기";}
  else {progress.Text="처음 설정이 완료됐어요";guideTitle.Text="업무를 시작할 준비가 됐어요";guideText.Text="전용 Edge 창에서 선택한 시스템을 엽니다.\n암호 입력·제출 중에는 잠시 다른 입력을 멈춰 주세요.\n시스템을 열 때도 창이 잠깐 전환될 수 있어요. 완료 후 원래 창으로 돌아갑니다.";next.Text="업무 시작";back.Text="처음 설정 안내 보기";}
  setup.Visible=settings.CertUser!="";
  setup.Text=step==SetupStep.Welcome||step==SetupStep.Certificate?"기존 설정으로 돌아가기":"인증서·암호 다시 등록";
 }
 internal void BeginSetup(){if(busy)return;step=SetupStep.Welcome;UpdateGuide();SetStatus("안내에 따라 한 단계씩 진행해 주세요.");}
 void RefreshCertificate(){
  cert.Text=settings.CertUser==""?"인증서는 안내 2단계에서 등록합니다.":"등록된 인증서: "+settings.CertUser+"  ·  만료일 "+settings.CertExpiry;
  bool registered=StartupRegistration.IsRegistered;bool hasEntry=StartupRegistration.HasEntry;
  enableStartup.Text=registered?"자동 시작 켜짐":"자동 시작 켜기";enableStartup.Enabled=!busy&&!registered;removeStartup.Enabled=!busy&&hasEntry;
  startupNote.Text=registered?(settings.PendingAttempt?"자동 시작 켜짐 · 로그인 오류 확인 전에는 도우미만 열려요.":(settings.Verified?"자동 시작 켜짐 · Windows 로그인 후 업무를 시작해요.":"자동 시작 켜짐 · 처음 설정을 마치면 자동 로그인해요.")):(hasEntry?"기존 등록 또는 확인이 필요한 바로가기가 있어요. 등록 삭제로 정리하세요.":"자동 시작 꺼짐 · 켜기 버튼을 누를 때만 등록해요.");
 }
 void ChangeStartup(bool enabled){if(busy)return;try{
  if(enabled){AppState.Install();StartupRegistration.Enable();}
  else StartupRegistration.Remove();
  settings.StartAtLogon=StartupRegistration.IsRegistered;settings.Save();AppState.Record(enabled?"STARTUP_ENABLED_BY_USER":"STARTUP_REMOVED_BY_USER");SetStatus(enabled?"자동 시작을 켰어요. 실제 재로그온 시험은 편한 시점에 직접 해 주세요.":"자동 시작 등록을 삭제했어요. 저장한 암호와 인증서는 그대로예요.");
 }catch(SafeFailure ex){SetStatus(ex.Message);}catch(Exception){SetStatus("자동 시작 변경을 완료하지 못했어요. 보안제품 알림과 실제 등록 상태를 확인해 주세요.");}finally{RefreshCertificate();}}
 void CaptureSettings(){SelectionPreferences.Save(settings,neis.Checked,kedu.Checked,persistSelection);}
 void Work(Action action,Action completed=null){if(busy)return;try{CaptureSettings();}catch(Exception){SetStatus("선택과 필수 상태를 저장하지 못해 작업을 시작하지 않았습니다.");return;}Portal.BeginOperation();busy=true;foreach(var b in new[]{next,back,setup,options,enableStartup,removeStartup})b.Enabled=false;neis.Enabled=kedu.Enabled=false;
  var worker=new System.ComponentModel.BackgroundWorker();worker.DoWork+=(o,e)=>action();worker.RunWorkerCompleted+=(o,e)=>{busy=false;foreach(var b in new[]{next,back,setup,options})b.Enabled=true;neis.Enabled=kedu.Enabled=true;RefreshCertificate();if(e.Error!=null){AppState.Record("STOPPED");SetStatus(e.Error is SafeFailure?e.Error.Message:"작업을 진행하지 못했어요. 화면을 확인한 뒤 다시 시작해 주세요.");}else{if(completed!=null)completed();SetStatus(lastStatus);}};worker.RunWorkerAsync();
 }
 void ExplainCertificateStep(bool loggedIn){
  step=SetupStep.Certificate;UpdateGuide();
  if(loggedIn)guideText.Text="지금은 업무포털에 로그인되어 있어요.\n업무포털에서 로그아웃한 뒤 인증서 로그인 버튼을 누르세요.\n인증서 목록이 보이면 아래 버튼을 눌러 주세요.";
  SetStatus("인증서 목록을 연 뒤 이 도우미로 돌아와 주세요.");
 }
 void OpenPortal(){Work(()=>{var c=Portal.OpenForSetup(settings.Profile);Invoke((Action)(()=>ExplainCertificateStep(c.Path==Portal.MainPath)));});}
 internal void Register(){Work(()=>{
  var c=Portal.OpenForSetup(settings.Profile);var d=Portal.Dialog(c);
  if(c.Path!=Portal.LoginPath||d==null||d.Current.IsOffscreen){Invoke((Action)(()=>ExplainCertificateStep(c.Path==Portal.MainPath)));return;}
  var certs=Portal.Certificates(c);
  Invoke((Action)(()=>{Activate();using(var dialog=new RegisterDialog(settings,certs)){
   if(dialog.ShowDialog(this)==DialogResult.OK){step=SetupStep.Registered;UpdateGuide();SetStatus("암호를 저장했어요. '업무 시작'을 눌러 주세요.");RefreshCertificate();}
   else {step=SetupStep.Certificate;UpdateGuide();SetStatus("준비되면 암호 등록을 이어서 진행해 주세요.");}
  }}));
 });}
 internal void Start(bool testing){
  if(busy)return;
  if(Native.GetForegroundWindow()==Handle){WindowState=FormWindowState.Minimized;IntPtr previous=Native.GetForegroundWindow();if(previous!=IntPtr.Zero&&previous!=Handle)Portal.ReturnWindowHint=previous;}
  Work(()=>{AppState.Record(testing?"TEST_STARTED":"RUN_STARTED");if(Portal.Run(settings,testing)){AppState.Record(testing?"TEST_SUCCESS":"RUN_SUCCESS");}},()=>{step=settings.Verified?SetupStep.Ready:SetupStep.Registered;UpdateGuide();});
 }
 void ShowSettings(){
  using(var dialog=new Form{Text="설정",ClientSize=new System.Drawing.Size(560,260),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font}){
   dialog.Controls.Add(new Label{Text="여러 Edge 프로필을 사용하는 경우에만 변경하세요.",Left=20,Top=20,Width=520});
   var profiles=new ComboBox{Left=20,Top=55,Width=520,DropDownStyle=ComboBoxStyle.DropDownList};profiles.Items.Add("Default");
   string userData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","Edge","User Data");
   if(Directory.Exists(userData))foreach(var path in Directory.GetDirectories(userData,"Profile *")){string folder=Path.GetFileName(path);if(System.Text.RegularExpressions.Regex.IsMatch(folder,@"^Profile \d+$"))profiles.Items.Add(folder);}
   profiles.SelectedItem=settings.Profile;if(profiles.SelectedIndex<0)profiles.SelectedIndex=0;dialog.Controls.Add(profiles);
   var reset=new Button{Text="오류 상태 초기화",Left=20,Top=111,Width=250,Height=36};reset.Click+=(o,e)=>{if(MessageBox.Show(dialog,"현재 인증서창의 오류·입력 내용을 직접 확인하셨나요?\n초기화 후 다음 실행은 새로운 1회 시도입니다.","입력 결과 확인",MessageBoxButtons.YesNo)==DialogResult.Yes){settings.PendingAttempt=false;settings.Save();AppState.Record("RESET_BY_USER");SetStatus("오류 상태를 초기화했어요.");}};dialog.Controls.Add(reset);
   var remove=new Button{Text="저장한 암호 삭제",Left=290,Top=111,Width=250,Height=36};remove.Click+=(o,e)=>{if(MessageBox.Show(dialog,"이 도우미가 저장한 인증서 암호만 삭제할까요?",Text,MessageBoxButtons.YesNo)==DialogResult.Yes){Vault.Remove(settings.Target);settings.CertUser=settings.CertExpiry=settings.CertIssuer="";settings.Verified=false;settings.PendingAttempt=false;settings.Save();step=SetupStep.Welcome;RefreshCertificate();UpdateGuide();AppState.Record("CREDENTIAL_REMOVED");}};dialog.Controls.Add(remove);
   var close=new Button{Text="설정 저장하고 닫기",Left=290,Top=200,Width=250,Height=36,DialogResult=DialogResult.OK};dialog.Controls.Add(close);
   if(dialog.ShowDialog(this)==DialogResult.OK){string previousProfile=settings.Profile;settings.Profile=profiles.SelectedItem.ToString();try{CaptureSettings();}catch(Exception){settings.Profile=previousProfile;SetStatus("설정을 저장하지 못했습니다. 이전 프로필을 유지합니다.");}}
  }
 }
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Portal.HelperWindow=Handle;IntPtr registeredHandle=Handle;stopHotkey=new StopHotkeyRegistration(()=>RegisterHotKey(registeredHandle,1,StopHotkeyRegistration.Modifiers,StopHotkeyRegistration.Key),()=>UnregisterHotKey(registeredHandle,1));hotkeyNote.Text=stopHotkey.Register()?"중단: Ctrl+Alt+F10 또는 중단 버튼":"단축키를 등록하지 못했습니다. 중단 버튼을 사용해 주세요.";}
 protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam==(IntPtr)1)RequestStop();base.WndProc(ref m);}
 protected override void OnHandleDestroyed(EventArgs e){if(stopHotkey!=null)stopHotkey.Dispose();base.OnHandleDestroyed(e);}
}

internal static class Program {
 [STAThread]static int Main(string[] args){
  try{
   Portal.ReturnWindowHint=Native.GetForegroundWindow();
   #if CONSOLE
   Console.OutputEncoding=new UTF8Encoding(false);
   #endif
   if(args.Contains("--self-test")){if(Marshal.SizeOf(typeof(Native.INPUT))!=40)throw new SafeFailure("Windows 입력 구조체 크기가 맞지 않습니다.");Vault.SelfTest();Console.WriteLine("PASS: x64 input structure; random credential round-trip, local direct setter, guards and cleanup; no secret printed");return 0;}
   if(args.Contains("--status")){var s=Settings.Load();Console.WriteLine(new JavaScriptSerializer().Serialize(new {registered=s.CertUser!=""&&Vault.Exists(s.Target),verified=s.Verified,pending=s.PendingAttempt,startAtLogon=StartupRegistration.IsRegistered,legacyStartup=StartupRegistration.LegacyPresent}));return 0;}
   using(var lease=new InstanceLease("Local\\TeacherWorkStart-"+System.Security.Principal.WindowsIdentity.GetCurrent().User.Value)){
    if(!lease.Owned){
     const string duplicate="업무 시작 도우미가 이미 실행 중입니다. 기존 도우미를 완전히 종료한 뒤 다시 실행·업데이트해 주세요.";
     #if CONSOLE
     Console.WriteLine(duplicate);
     #else
     if(!args.Contains("--auto"))MessageBox.Show(duplicate,"업무 시작 도우미");
     #endif
     return 2;
    }
   if(args.Length==2&&args[0]=="--finish-install"){
    if(Native.IsPackaged)throw new SafeFailure("일반 바탕화면 환경에서 설치 확인을 실행해야 합니다.");
    var migrated=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(args[1],Encoding.UTF8));
    bool credentialAvailable=migrated.CertUser!=""&&Vault.Exists(migrated.Target);
    if(!credentialAvailable)migrated.Verified=false;
    migrated.StartAtLogon=StartupRegistration.IsRegistered;migrated.Save();string installed=AppState.Install();
    File.WriteAllText(Path.Combine(Settings.Root,"installation-check.json"),new JavaScriptSerializer().Serialize(new {unpackaged=true,credentialAvailable=credentialAvailable,verified=migrated.Verified,exe=installed,startupRegistered=StartupRegistration.IsRegistered}),new UTF8Encoding(false));
    lease.Release();Process.Start(new ProcessStartInfo(installed,"--show"){UseShellExecute=true});return 0;
   }
   if(args.Contains("--install-register")){string exe=AppState.Install();lease.Release();Process.Start(new ProcessStartInfo(exe,"--register-only"){UseShellExecute=true});return 0;}
   if(args.Contains("--install-only")){AppState.Install();return 0;}
   if(!args.Any()){string actual=System.Reflection.Assembly.GetExecutingAssembly().Location;string installed=AppState.Install();if(!String.Equals(actual,installed,StringComparison.OrdinalIgnoreCase)){lease.Release();Process.Start(new ProcessStartInfo(installed){UseShellExecute=true});return 0;}}
    var settings=Settings.Load();
    if(args.Length==3&&args[0]=="--strict-session")return StrictSession.Execute(settings,Int64.Parse(args[1]),args[2]);
    if(args.Length==2&&args[0]=="--open-systems-existing"){Portal.Report=msg=>Console.WriteLine(msg);Portal.OpenExistingSystems(settings,Int64.Parse(args[1]));Console.WriteLine("SYSTEM_MENUS_VERIFIED");return 0;}
    if(args.Length==2&&args[0]=="--background-once"){Portal.Report=msg=>Console.WriteLine(msg);Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.BackgroundOnce(settings,Int64.Parse(args[1]))));return 0;}
    if(args.Length==2&&args[0]=="--probe-background-existing"){Portal.BindProbeWindow(settings,Int64.Parse(args[1]));Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.ProbeBackground(settings)));return 0;}
    if(args.Contains("--probe-background")){Portal.Report=msg=>Console.WriteLine(msg);Console.WriteLine(new JavaScriptSerializer().Serialize(Portal.ProbeBackground(settings)));return 0;}
    if(args.Contains("--prepare")){var c=Portal.Prepare(settings.Profile);foreach(var cert in Portal.Certificates(c))Console.WriteLine(cert.ToString());AppState.Record("PREPARED");return 0;}
    if(args.Contains("--run-once")){bool testing=args.Contains("--test");Portal.Report=msg=>Console.WriteLine(msg);AppState.Record(testing?"TEST_STARTED":"RUN_STARTED");Portal.Run(settings,testing);AppState.Record(testing?"TEST_SUCCESS":"RUN_SUCCESS");return 0;}
    if(args.Contains("--run-legacy-once")){Portal.Report=msg=>Console.WriteLine(msg);Portal.Run(settings,args.Contains("--test"),false);return 0;}
    if(args.Contains("--uninstall-startup")){StartupRegistration.Remove();settings.StartAtLogon=false;settings.Save();return 0;}
    if(args.Contains("--auto"))AppState.Record("AUTO_LAUNCHED");
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    var form=new MainWindow(settings);
    form.Shown+=(o,e)=>{if(args.Contains("--register")||args.Contains("--register-only")||args.Contains("--setup"))form.BeginSetup();else if(args.Contains("--auto")){if(settings.Verified&&!settings.PendingAttempt&&Vault.Exists(settings.Target))form.Start(false);}};
    Application.Run(form);return 0;
   }
  }catch(SafeFailure e){AppState.Record("STOPPED");string message=AppState.WithLogNotice(e.Message);Console.WriteLine(message);
   #if !CONSOLE
   MessageBox.Show(message,"업무 시작 도우미 · 중단");
   #endif
   return 3;
  }catch(Exception e){AppState.Record("UNEXPECTED_STOP");string message=AppState.WithLogNotice("안전하게 중단했습니다. 오류 유형: "+e.GetType().Name);Console.WriteLine(message);
   #if !CONSOLE
   MessageBox.Show(message+"\n자동으로 재시도하지 않습니다.","업무 시작 도우미 · 중단");
   #endif
   return 4;}
 }
}
