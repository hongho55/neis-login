using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Modeless notice: never an authentication dialog and never reads browser contents.
internal sealed class WorkProgressWindow:Form {
 readonly Label phase=new Label();readonly Button stop=new Button();readonly Action requestStop;bool finishing,pointerWasVisible;
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
 internal WorkProgressWindow(Action cancel){
  requestStop=cancel;Text="업무 시작 안내";ClientSize=new Size(440,190);Font=new Font("맑은 고딕",10);BackColor=Color.White;
  FormBorderStyle=FormBorderStyle.FixedToolWindow;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
  Location=CenterWithin(Screen.FromHandle(Native.GetForegroundWindow()).WorkingArea,Size);
  Controls.Add(new Label{Text="로그인 중입니다. 잠시만 기다려 주세요.",Left=18,Top=16,Width=405,Height=30,Font=new Font(Font,FontStyle.Bold)});
  phase.SetBounds(18,53,405,45);phase.Text="업무용 Edge를 준비하고 있어요.";Controls.Add(phase);
  Controls.Add(new Label{Text="완료될 때까지 업무용 Edge 창을 그대로 두세요.",Left=18,Top=104,Width=405,Height=24,ForeColor=Color.DimGray});
  stop.SetBounds(18,140,405,32);stop.Text="중단";stop.Click+=(o,e)=>requestStop();Controls.Add(stop);
  FormClosing+=(o,e)=>{if(!finishing){e.Cancel=true;requestStop();}};
 }
 internal void UpdateProgress(string message){phase.Text=message;}
 internal void MoveOutsideObservation(Rectangle observation){
  if(IsDisposed||!IsHandleCreated)return;
  Action apply=()=>{
   if(IsDisposed||finishing||!Bounds.IntersectsWith(observation))return;
   var chosen=OutsideObservation(Screen.AllScreens.Select(s=>s.WorkingArea).ToArray(),Size,observation);
   if(chosen.HasValue){if(Location!=chosen.Value)Location=chosen.Value;}
   else if(Visible)Hide(); // No space: keep tray/hotkey stop; never hide/show per sample.
  };
  try{if(InvokeRequired)Invoke(apply);else apply();}catch(InvalidOperationException){}
 }
 internal static Point? OutsideObservation(Rectangle[] screens,Size size,Rectangle observation){
  foreach(var area in screens)foreach(var point in new[]{new Point(area.Left,area.Top),new Point(area.Right-size.Width,area.Top),new Point(area.Left,area.Bottom-size.Height),new Point(area.Right-size.Width,area.Bottom-size.Height)}){
   var rect=new Rectangle(point,size);if(area.Contains(rect)&&!rect.IntersectsWith(observation))return point;
  }
  return null;
 }
 internal static Point CenterWithin(Rectangle area,Size size){return new Point(area.Left+Math.Max(0,(area.Width-size.Width)/2),area.Top+Math.Max(0,(area.Height-size.Height)/2));}
 internal void SetPointerNoticeVisible(bool visible){
  if(IsDisposed||!IsHandleCreated)return;
  Action apply=()=>{if(IsDisposed||finishing)return;if(!visible){pointerWasVisible=Visible;Hide();}else{bool show=pointerWasVisible;pointerWasVisible=false;if(show)Show();}};
  try{if(InvokeRequired)Invoke(apply);else apply();}catch(InvalidOperationException){}
 }
 internal void Finish(){if(IsDisposed)return;finishing=true;Close();if(!IsDisposed)Dispose();}
}
