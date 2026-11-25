using System;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class CertificateStorage {
 internal const string HardDisk="HDD",Removable="REMOVABLE";
 internal readonly string Media,Drive;
 internal CertificateStorage(string media,string drive){
  if(media!=HardDisk&&media!=Removable)throw new SafeFailure("인증서 저장 매체를 확인해 주세요.");
  Media=media;Drive=NormalizeDrive(drive);
  if((media==HardDisk&&Drive!="")||(media==Removable&&Drive==""))throw new SafeFailure("인증서 저장 위치와 드라이브를 확인해 주세요.");
 }
 internal static string NormalizeDrive(string drive){
  string normalized=(drive??"").Trim().ToUpperInvariant();if(normalized=="")return "";
  if(!Regex.IsMatch(normalized,@"^[A-Z]:$"))throw new SafeFailure("드라이브 문자는 D: 또는 Z:처럼 선택해 주세요.");return normalized;
 }
  internal static CertificateStorage From(Settings settings){
  var location=new CertificateStorage(settings.CertMedia,settings.CertDrive);
  if(!settings.CertStorageConfigured&&location.Media!=HardDisk)throw new SafeFailure("인증서 저장 위치 등록을 먼저 완료해 주세요.");
  return location;
 }
 internal string Display {get{return Media==HardDisk?"하드디스크":"이동식디스크 "+Drive;}}
 internal string ButtonName {get{return Media==HardDisk?"하드디스크":"이동식디스크";}}
 internal bool IsMediaName(string name){return Regex.Replace(name??"",@"\s+","")==ButtonName;}
 internal bool IsDriveName(string name){
  var found=Regex.Matches(name??"",@"(?:^|[\s(])([A-Za-z]:)(?=[\s)\\/]|$)").Cast<Match>().Select(m=>m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
  return Media==Removable&&found.Length==1&&found[0]==Drive;
 }
}

internal sealed class FreshCertificateRows {
 readonly System.Collections.Generic.HashSet<string> before;
 internal FreshCertificateRows(System.Collections.Generic.IEnumerable<string> previous){before=new System.Collections.Generic.HashSet<string>(previous);}
 internal bool Ready(System.Collections.Generic.IEnumerable<string> current){var rows=current.ToArray();return rows.Length>0&&rows.All(key=>!before.Contains(key));}
}

internal static class CertificateRowData {
 internal static string IdentityKey(string[] cells){
  var parsed=Parse(cells);return parsed==null?String.Join("\u001f",cells??new string[0]):parsed.User+"\u001f"+parsed.Expiry+"\u001f"+parsed.Issuer;
 }
 internal static CertificateInfo Parse(string[] cells){
  if(cells==null||cells.Length<4)return null;var names=cells.Select(value=>(value??"").Trim()).ToArray();
  string date=names.FirstOrDefault(value=>Regex.IsMatch(value,@"^\d{4}-\d{2}-\d{2}$"));DateTime expiry;
  if(date==null||!DateTime.TryParse(date,out expiry))return null;
  int index=Array.IndexOf(names,date);if(index<1||index+1>=names.Length)return null;
  return new CertificateInfo{User=names[index-1],Expiry=date,Issuer=names[index+1]};
 }
}
