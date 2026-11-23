using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("업무 시작 도우미 설치")]
[assembly: AssemblyDescription("현재 Windows 사용자에게 업무 시작 도우미를 설치합니다.")]
[assembly: AssemblyVersion(TeacherWorkStartSetup.PayloadMetadata.Version)]
[assembly: AssemblyFileVersion(TeacherWorkStartSetup.PayloadMetadata.Version)]

namespace TeacherWorkStartSetup {
 internal static class SetupProgram {
  [STAThread]static int Main(){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{using(var lease=new SetupLease("Local\\TeacherWorkStart-Setup-"+SetupPlatform.User)){
    if(!lease.Owned){MessageBox.Show("설치 창이 이미 열려 있습니다. 기존 설치 창에서 진행해 주세요.","업무 시작 도우미 설치",MessageBoxButtons.OK,MessageBoxIcon.Information);return 2;}
    var payload=new SetupPayload(PayloadMetadata.Version,PayloadMetadata.ExecutableHash,PayloadMetadata.ConfigurationHash,name=>Assembly.GetExecutingAssembly().GetManifestResourceStream("TeacherWorkStartSetup."+name));
    string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TeacherWorkStart");
    using(var form=new SetupWindow(new SetupCore(root,Path.GetTempPath(),payload,new SetupPlatform()),new Version(PayloadMetadata.Version).ToString(3)))Application.Run(form);return 0;
   }}catch(Exception){MessageBox.Show("설치 창을 열지 못했어요. 파일 탐색기에서 설치 파일을 직접 열어 주세요.","업무 시작 도우미 설치",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
