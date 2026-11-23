using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class StartupOptions:UserControl {
 readonly RadioButton automatic=new RadioButton(),manual=new RadioButton();readonly Label note=new Label();bool restoring;
 internal StartupOptions(Settings settings,Action persist,Action<bool> changeRegistration,Func<string> describe){
  Size=new Size(552,235);Font=new Font("맑은 고딕",10);
  Controls.Add(new Label{Text="Windows 로그인 후",Left=0,Top=0,Width=540,Height=26,Font=new Font(Font,FontStyle.Bold)});
  var enable=new Button{Text="자동 시작 켜기",Left=0,Top=32,Width=265,Height=34};enable.Click+=(o,e)=>Change(true,changeRegistration,describe);Controls.Add(enable);
  var disable=new Button{Text="자동 시작 끄기",Left=278,Top=32,Width=265,Height=34};disable.Click+=(o,e)=>Change(false,changeRegistration,describe);Controls.Add(disable);
  automatic.Text="업무포털 로그인·선택한 시스템 열기";automatic.SetBounds(0,78,543,28);Controls.Add(automatic);
  manual.Text="도우미만 열고, 업무 시작은 직접 누르기";manual.SetBounds(0,110,543,28);Controls.Add(manual);
  automatic.Checked=settings.AutoRunAtLogon;manual.Checked=!settings.AutoRunAtLogon;
  note.SetBounds(0,151,543,76);note.ForeColor=Color.DimGray;Controls.Add(note);note.Text=describe();
  EventHandler selected=(o,e)=>{if(restoring||!((RadioButton)o).Checked)return;try{StartupPreferences.Save(settings,automatic.Checked,persist);note.Text=describe();}
   catch(Exception){restoring=true;try{automatic.Checked=settings.AutoRunAtLogon;manual.Checked=!settings.AutoRunAtLogon;}finally{restoring=false;}note.Text="선택을 저장하지 못했습니다. 이전 선택을 유지합니다.";}};
  automatic.CheckedChanged+=selected;manual.CheckedChanged+=selected;
 }
 void Change(bool enabled,Action<bool> apply,Func<string> describe){try{apply(enabled);note.Text=describe();}catch(SafeFailure ex){note.Text=ex.Message;}catch(Exception){note.Text="자동 시작 변경을 완료하지 못했습니다. 등록 상태와 보안제품 알림을 확인해 주세요.";}}
}
