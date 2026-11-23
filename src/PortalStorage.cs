using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Automation;

internal sealed class PreparedCertificateStorage {
 internal PortalContext Context;internal CertificateStorage Location;internal string DocumentKey;internal HashSet<string> Rows;
 internal bool MediaDirect,DriveDirect;
}

internal static partial class Portal {
 static string RuntimeKey(AutomationElement element){return String.Join("-",element.GetRuntimeId());}
 static string[] StorageRows(PortalContext c){
  var dialog=Dialog(c);var list=dialog==null?null:Id(dialog,"kc_cert_list");
  return list==null||dialog.Current.IsOffscreen||list.Current.IsOffscreen?new string[0]:All(list,ControlType.DataItem).Where(e=>CertificateRowData.Parse(All(e,ControlType.DataItem).Select(cell=>cell.Current.Name).ToArray())!=null).Select(RuntimeKey).ToArray();
 }
 internal static PortalContext OpenForRegistration(string profile){
  if(BoundWindow()!=null&&workProfile==profile)return OpenForSetup(profile);
  var candidates=new List<PortalContext>();
  foreach(var window in Windows())foreach(var doc in All(window.Item2,ControlType.Document)){
   Uri url;if(doc.Current.IsOffscreen||!Uri.TryCreate(DocUrl(doc),UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="gwe.eduptl.kr"||url.Port!=443||url.AbsolutePath!=LoginPath)continue;
   var candidate=new PortalContext{Hwnd=window.Item1,Window=window.Item2,Doc=doc,Path=LoginPath};var dialog=Dialog(candidate);
   if(dialog==null||dialog.Current.IsOffscreen)continue;
   try{Validate(candidate,LoginPath,false);candidates.Add(candidate);}catch(SafeFailure){}
  }
  var windows=candidates.GroupBy(c=>c.Hwnd).ToArray();
  if(windows.Length>1)throw new SafeFailure("인증서창이 여러 업무 창에 열려 있습니다. 등록할 창 하나만 남겨 주세요.");
  if(windows.Length==1){BindProbeWindow(new Settings{Profile=profile},windows[0].Key.ToInt64());return Locate(profile);}
  return OpenForSetup(profile);
 }
 internal static PreparedCertificateStorage SelectCertificateStorage(PortalContext initial,CertificateStorage location){
  CheckCancel();Validate(initial,LoginPath,false);
  var dialog=Dialog(initial);if(dialog==null||dialog.Current.IsOffscreen)throw new SafeFailure("먼저 업무포털에서 인증서창을 열어 주세요.");
  var media=All(dialog,ControlType.Button).Where(b=>!b.Current.IsOffscreen&&b.Current.IsEnabled&&location.IsMediaName(b.Current.Name)).ToList();
  if(media.Count!=1)throw new SafeFailure("인증서 저장 매체 버튼을 하나로 확인하지 못했습니다. 암호를 입력하지 않습니다.");
  var previous=StorageRows(initial);
  object mediaPattern;bool mediaDirect=media[0].TryGetCurrentPattern(InvokePattern.Pattern,out mediaPattern)||media[0].TryGetCurrentPattern(SelectionItemPattern.Pattern,out mediaPattern),driveDirect=false;
  bool driveMenuOpen=location.Media==CertificateStorage.Removable&&dialog.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Any(e=>!e.Current.IsPassword&&!e.Current.IsOffscreen&&location.IsDriveName(e.Current.Name));
  if(!driveMenuOpen)Native.Click(media[0],initial.Hwnd,workProcess);
  if(location.Media==CertificateStorage.Removable){
   var choices=Wait(()=>{
    var current=Locate(workProfile);if(current==null)return null;Validate(current,LoginPath,false);var shown=Dialog(current);if(shown==null)return null;
    var matches=shown.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Where(e=>!e.Current.IsPassword&&!e.Current.IsOffscreen&&e.Current.IsEnabled&&location.IsDriveName(e.Current.Name)).ToList();
    if(matches.Count==0)return null;
    // A caption may be exposed on both the clickable container and its text.
    var direct=matches.Where(e=>{object p;return e.TryGetCurrentPattern(InvokePattern.Pattern,out p)||e.TryGetCurrentPattern(SelectionItemPattern.Pattern,out p);}).ToList();
    var pool=direct.Count>0?direct:matches;
    var leaves=pool.Where(e=>!e.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Any(child=>pool.Any(other=>Automation.Compare(child,other)))).ToList();
    if(leaves.Count!=1)throw new SafeFailure("선택한 인증서 드라이브 항목을 하나로 확인하지 못했습니다. 다른 드라이브로 대신 진행하지 않습니다.");
    return Tuple.Create(current,leaves[0]);
   },7000,"등록한 이동식 드라이브가 인증서창에 보이지 않습니다. 연결 상태와 드라이브 문자를 확인해 주세요. 암호는 입력하지 않았습니다.");
   previous=StorageRows(choices.Item1);Validate(choices.Item1,LoginPath,false);
   object drivePattern;driveDirect=choices.Item2.TryGetCurrentPattern(InvokePattern.Pattern,out drivePattern)||choices.Item2.TryGetCurrentPattern(SelectionItemPattern.Pattern,out drivePattern);
   Native.Click(choices.Item2,choices.Item1.Hwnd,workProcess);
  }
  var fresh=new FreshCertificateRows(previous);
  var prepared=Wait(()=>{
   var current=Locate(workProfile);if(current==null)return null;Validate(current,LoginPath,false);
   var keys=StorageRows(current);if(!fresh.Ready(keys))return null;
   return new PreparedCertificateStorage{Context=current,Location=location,DocumentKey=RuntimeKey(current.Doc),Rows=new HashSet<string>(keys),MediaDirect=mediaDirect,DriveDirect=driveDirect};
  },20000,"선택한 저장 위치의 새 인증서 목록을 확인하지 못했습니다. 이전 목록으로 암호를 입력하거나 다른 위치로 대신 진행하지 않습니다.");
  ValidatePreparedStorage(prepared);return prepared;
 }
 internal static void ValidatePreparedStorage(PreparedCertificateStorage prepared){
  CheckCancel();var current=Locate(workProfile);
  if(current==null||current.Hwnd!=prepared.Context.Hwnd)throw new SafeFailure("등록 중인 인증서 업무 창이 바뀌었습니다.");
  Validate(current,LoginPath,false);
  if(RuntimeKey(current.Doc)!=prepared.DocumentKey||!prepared.Rows.SetEquals(StorageRows(current)))throw new SafeFailure("확인한 저장 위치의 인증서 목록이 바뀌었습니다. 위치와 인증서를 다시 확인해 주세요.");
 }
 internal static void ValidatePreparedCertificate(PreparedCertificateStorage prepared,CertificateInfo certificate){
  ValidatePreparedStorage(prepared);var current=Locate(workProfile);var list=Id(Dialog(current),"kc_cert_list");
  var selected=new Settings{CertUser=certificate.User,CertExpiry=certificate.Expiry,CertIssuer=certificate.Issuer};
  if(list==null||MatchingCertificateRows(list,selected).Count!=1)throw new SafeFailure("선택한 인증서 정보가 바뀌었거나 중복되어 저장하지 않았습니다. 다시 확인해 주세요.");
 }
 internal static object StorageViewFacts(PortalContext current){
  Validate(current,LoginPath,false);var dialog=Dialog(current);var list=dialog==null?null:Id(dialog,"kc_cert_list");
  return new {version=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),pid=System.Diagnostics.Process.GetCurrentProcess().Id,dialogPresent=dialog!=null,dialogOffscreen=dialog==null?(bool?)null:dialog.Current.IsOffscreen,listPresent=list!=null,listOffscreen=list==null?(bool?)null:list.Current.IsOffscreen,dataItems=list==null?0:All(list,ControlType.DataItem).Count,actualRowKeys=StorageRows(current),guardRows=StorageRows(current).Length,certificateCount=list==null?0:ReadCertificatesFromList(list,true).Count,passwordRead=false,settingsWritten=false};
 }
}
