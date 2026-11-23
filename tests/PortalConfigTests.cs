using System;
using System.IO;
using System.Linq;

// Pure parsing/validation checks; nothing here touches a browser, credentials or the real install folder.
internal static class PortalConfigTests {
 internal static void Run(Action<bool,string> check,string root){
  var defaults=new PortalConfig();
  var empty=PortalConfig.Parse("{}");
  check(empty.Host=="gwe.eduptl.kr"&&empty.LoginPath==defaults.LoginPath&&empty.MainPath==defaults.MainPath&&empty.BrowserProcess=="msedge"&&empty.PollIntervalMs==250&&empty.TabSettleMs==150&&empty.Problems.Count==0,"config/empty file keeps every built-in default");
  check(empty.Origin=="https://gwe.eduptl.kr","config/default origin is the known portal over https");

  var custom=PortalConfig.Parse("{\"host\":\"Portal.Example.KR\",\"loginPath\":\"/login.do\",\"mainPath\":\"/main.do\",\"browserProcess\":\"msedge\",\"pollIntervalMs\":500,\"tabSettleMs\":0}");
  check(custom.Problems.Count==0&&custom.Host=="portal.example.kr"&&custom.LoginPath=="/login.do"&&custom.MainPath=="/main.do"&&custom.PollIntervalMs==500&&custom.TabSettleMs==0,"config/valid overrides apply and host is lower-cased");

  string[] badHosts={"evil.com/x","a b.kr","https://gwe.eduptl.kr","localhost","-bad.kr","gwe.eduptl.kr:8443","gwe.eduptl.kr@evil.com",""};
  foreach(var host in badHosts){
   var config=PortalConfig.Parse("{\"host\":\""+host+"\"}");
   check(config.Host=="gwe.eduptl.kr"&&config.Problems.Count==1,"config/invalid host falls back to default: "+host);
  }
  var wrongType=PortalConfig.Parse("{\"host\":5,\"pollIntervalMs\":\"fast\"}");
  check(wrongType.Host=="gwe.eduptl.kr"&&wrongType.PollIntervalMs==250&&wrongType.Problems.Count==2,"config/wrong value types fall back to defaults");
  var range=PortalConfig.Parse("{\"pollIntervalMs\":1,\"tabSettleMs\":99999}");
  check(range.PollIntervalMs==250&&range.TabSettleMs==150&&range.Problems.Count==2,"config/out-of-range timings fall back to defaults");
  var badPath=PortalConfig.Parse("{\"loginPath\":\"login.do\",\"mainPath\":\"/a?x=1\"}");
  check(badPath.LoginPath==defaults.LoginPath&&badPath.MainPath==defaults.MainPath&&badPath.Problems.Count==2,"config/paths must be absolute without query or odd characters");
  var same=PortalConfig.Parse("{\"loginPath\":\"/x.do\",\"mainPath\":\"/x.do\"}");
  check(same.LoginPath==defaults.LoginPath&&same.MainPath==defaults.MainPath&&same.Problems.Count==1,"config/identical login and main paths are rejected");
  var unknown=PortalConfig.Parse("{\"scheme\":\"http\",\"port\":80}");
  check(unknown.Problems.Count==2&&unknown.Origin.StartsWith("https://"),"config/scheme and port cannot be configured");
  check(PortalConfig.Parse("not json").Problems.Count==1&&PortalConfig.Parse("[1,2]").Host=="gwe.eduptl.kr","config/malformed or non-object JSON yields defaults with a problem");

  var url=PortalConfig.Parse("{\"host\":\"portal.example.kr\"}");
  Func<string,bool> ok=u=>url.IsPortalUrl(new Uri(u),"/a.do");
  check(ok("https://portal.example.kr/a.do")&&ok("https://PORTAL.example.kr:443/a.do?x=1"),"config/url check accepts the configured host over https on 443");
  check(!ok("http://portal.example.kr/a.do")&&!ok("https://portal.example.kr:8443/a.do")&&!ok("https://portal.example.kr.evil.com/a.do")&&!ok("https://evil.com/a.do")&&!ok("https://portal.example.kr/b.do")&&!url.IsPortalUrl(null,"/a.do"),"config/url check rejects other scheme, port, host, look-alike host and path");

  string folder=Path.Combine(root,"portal-config");Directory.CreateDirectory(folder);
  check(PortalConfig.LoadFrom(folder).Problems.Count==0&&PortalConfig.LoadFrom(folder).Host=="gwe.eduptl.kr","config/missing file means defaults without problems");
  File.WriteAllText(Path.Combine(folder,PortalConfig.FileName),"{\"host\":\"portal.example.kr\"}");
  check(PortalConfig.LoadFrom(folder).Host=="portal.example.kr","config/file next to the executable is read");
  File.WriteAllText(Path.Combine(folder,PortalConfig.FileName),"{broken");
  var broken=PortalConfig.LoadFrom(folder);
  check(broken.Host=="gwe.eduptl.kr"&&broken.Problems.Count==1&&broken.Problems.Single().Contains(PortalConfig.FileName),"config/broken file yields defaults and names the file in the problem");
 }
}
