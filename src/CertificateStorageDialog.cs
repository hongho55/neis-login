using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class CertificateStorageDialog:Form {
 readonly RadioButton hdd=new RadioButton(),removable=new RadioButton();readonly ComboBox drives=new ComboBox();readonly Label error=new Label();
 internal CertificateStorage Choice {get;private set;}
 internal CertificateStorageDialog(CertificateStorage previous){
  Text="인증서 저장 위치";ClientSize=new Size(540,270);Font=new Font("맑은 고딕",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
  Controls.Add(new Label{Text="인증서를 보관한 위치를 선택하세요.\n선택한 위치에서 인증서를 다시 확인한 뒤 암호를 저장합니다.",Left=20,Top=20,Width=500,Height=55});
  hdd.Text="하드디스크";hdd.SetBounds(20,87,240,28);Controls.Add(hdd);
  removable.Text="이동식디스크";removable.SetBounds(20,125,200,28);Controls.Add(removable);
  drives.SetBounds(235,125,285,30);drives.DropDownStyle=ComboBoxStyle.DropDownList;drives.AccessibleName="인증서 드라이브";
  foreach(string root in Environment.GetLogicalDrives())drives.Items.Add(root.Substring(0,2).ToUpperInvariant());
  if(previous.Drive!=""&&!drives.Items.Contains(previous.Drive))drives.Items.Add(previous.Drive);
  if(previous.Drive!="")drives.SelectedItem=previous.Drive;
  Controls.Add(drives);hdd.Checked=previous.Media==CertificateStorage.HardDisk;removable.Checked=!hdd.Checked;drives.Enabled=removable.Checked;
  removable.CheckedChanged+=(o,e)=>drives.Enabled=removable.Checked;
  error.SetBounds(20,169,500,40);error.ForeColor=Color.Firebrick;Controls.Add(error);
  var next=new Button{Text="이 위치에서 인증서 확인",Left=20,Top=220,Width=325,Height=32};next.Click+=(o,e)=>{try{Choice=new CertificateStorage(hdd.Checked?CertificateStorage.HardDisk:CertificateStorage.Removable,hdd.Checked?"":(drives.SelectedItem as string??""));DialogResult=DialogResult.OK;Close();}catch(SafeFailure ex){error.Text=ex.Message;}};Controls.Add(next);
  var cancel=new Button{Text="취소",Left=360,Top=220,Width=160,Height=32,DialogResult=DialogResult.Cancel};Controls.Add(cancel);CancelButton=cancel;
 }
}
