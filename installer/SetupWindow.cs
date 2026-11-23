using System;
using System.Drawing;
using System.ComponentModel;
using System.Windows.Forms;

namespace TeacherWorkStartSetup {
 internal sealed class SetupWindow:Form {
  readonly SetupCore installer;readonly Label heading=new Label(),status=new Label();readonly ProgressBar progress=new ProgressBar();
  internal readonly Button Primary=new Button(),CloseButton=new Button();internal bool Busy {get;private set;}internal bool Complete {get;private set;}
  internal SetupWindow(SetupCore core,string version){
   installer=core;Text="업무 시작 도우미 설치 · "+version;Font=new Font("맑은 고딕",10);AutoScaleMode=AutoScaleMode.Font;ClientSize=new Size(640,520);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Icon=SystemIcons.Application;BackColor=Color.White;
   heading.SetBounds(26,24,588,38);heading.Font=new Font(Font.FontFamily,19,FontStyle.Bold);heading.Text="업무 시작 도우미 설치";Controls.Add(heading);
   Controls.Add(new Label{Left=28,Top=69,Width=584,Height=25,Text="강원 업무포털 · Windows x64 · Microsoft Edge · "+version,ForeColor=Color.DimGray});
   var steps=new Panel{Left=26,Top=112,Width=588,Height=145,BackColor=Color.FromArgb(240,246,252)};steps.Controls.Add(new Label{Left=18,Top=15,Width=552,Height=120,Text="1. 아래 설치하기를 눌러 주세요.\n\n2. 설치가 끝나면 도우미 열기를 누릅니다.\n\n3. 도우미 안내에 따라 인증서·암호를 직접 등록합니다."});Controls.Add(steps);
   Controls.Add(new Label{Left=27,Top=273,Width=580,Height=24,Text="이 Windows 계정에 설치합니다."});
   var location=new TextBox{Left=26,Top=300,Width=588,ReadOnly=true,Text=core.Root,AccessibleName="설치 위치"};Controls.Add(location);
   status.SetBounds(27,345,586,65);status.Text="바탕화면에 업무 시작 도우미 바로가기를 만듭니다.\n기존 설정은 유지하며 자동 시작은 설치 후 직접 선택합니다.";status.AccessibleName="설치 상태";Controls.Add(status);
   progress.SetBounds(26,414,588,7);progress.Style=ProgressBarStyle.Marquee;progress.Visible=false;Controls.Add(progress);
   CloseButton.Text="닫기";CloseButton.SetBounds(310,447,108,44);CloseButton.Click+=(s,e)=>Close();Controls.Add(CloseButton);
   Primary.Text="설치하기";Primary.SetBounds(434,447,180,44);Primary.BackColor=Color.FromArgb(34,91,159);Primary.ForeColor=Color.White;Primary.FlatStyle=FlatStyle.Flat;Primary.FlatAppearance.BorderSize=0;Primary.AccessibleName="설치 진행";Primary.Click+=(s,e)=>Proceed();Controls.Add(Primary);AcceptButton=Primary;CancelButton=CloseButton;
   FormClosing+=(s,e)=>{if(Busy)e.Cancel=true;};
  }
  void Proceed(){
   if(Busy)return;if(Complete){try{installer.Open();Close();}catch(Exception){status.ForeColor=Color.Firebrick;status.Text="도우미를 열지 못했습니다. 위 설치 위치의 TeacherWorkStart.exe를 열어 주세요.";}return;}
   Busy=true;Primary.Enabled=false;CloseButton.Enabled=false;ControlBox=false;progress.Visible=true;status.ForeColor=Color.DimGray;status.Text="설치 중입니다. 잠시 기다려 주세요.\n업무포털 로그인이나 자동 시작 등록은 진행하지 않습니다.";
   var worker=new BackgroundWorker();worker.DoWork+=(s,e)=>e.Result=installer.Install();worker.RunWorkerCompleted+=(s,e)=>{
    Busy=false;Primary.Enabled=true;CloseButton.Enabled=true;ControlBox=true;progress.Visible=false;
    if(e.Error!=null){status.ForeColor=Color.Firebrick;status.Text=e.Error is SetupFailure?e.Error.Message:"설치를 완료하지 못했어요. 파일 사용 상태나 보안제품 안내를 확인해 주세요. 권한과 보안 설정은 변경하지 않았습니다.";worker.Dispose();return;}
    var result=(SetupResult)e.Result;Complete=true;heading.Text="설치가 완료됐어요";Primary.Text="도우미 열기";status.ForeColor=Color.FromArgb(25,91,53);
    status.Text=result.ShortcutReady?"다음부터 바탕화면의 업무 시작 도우미를 열면 됩니다.\n지금 도우미 열기를 눌러 첫 설정을 시작해 주세요.":"설치는 완료됐지만 바탕화면 바로가기를 확인하지 못했어요.\n지금은 아래 도우미 열기를 눌러 진행할 수 있습니다.";
    if(!result.TemporaryFilesRemoved)status.Text+="\n임시 설치 파일 일부가 남아 있습니다.";worker.Dispose();
   };worker.RunWorkerAsync();
  }
 }
}
