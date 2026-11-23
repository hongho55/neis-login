using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Optional per-install overrides read from portal.config.json next to the executable.
// Every value is validated; a missing, unreadable or invalid entry falls back to the built-in
// default (and is listed in Problems), so a bad file can never widen where a password may be typed.
// Scheme (https) and port (443) are deliberately not configurable, nor is the browser executable.
internal sealed class PortalConfig {
 internal const string FileName="portal.config.json";
 static readonly Regex HostPattern=new Regex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",RegexOptions.CultureInvariant);
 static readonly Regex PathPattern=new Regex(@"^/[A-Za-z0-9_./-]*$",RegexOptions.CultureInvariant);
 static readonly Regex ProcessPattern=new Regex(@"^[A-Za-z0-9_.-]+$",RegexOptions.CultureInvariant);

 internal string Host="gwe.eduptl.kr";
 internal string LoginPath="/bpm_lgn_lg00_001.do";
 internal string MainPath="/bpm_man_mn00_001.do";
 internal string BrowserProcess="msedge";
 internal int PollIntervalMs=250;
 internal int TabSettleMs=150;
 internal readonly List<string> Problems=new List<string>();

 static PortalConfig current;
 static readonly object gate=new object();
 internal static PortalConfig Current {
  get{lock(gate){if(current==null)current=LoadFrom(AppDomain.CurrentDomain.BaseDirectory);return current;}}
  set{lock(gate){current=value;}}
 }

 internal static PortalConfig LoadFrom(string directory){
  string path=Path.Combine(directory,FileName);
  if(!File.Exists(path))return new PortalConfig();
  try{return Parse(File.ReadAllText(path,Encoding.UTF8));}
  catch(IOException ex){return Failed("읽을 수 없습니다: "+ex.Message);}
  catch(UnauthorizedAccessException ex){return Failed("읽을 수 없습니다: "+ex.Message);}
 }

 static PortalConfig Failed(string problem){var config=new PortalConfig();config.Problems.Add(FileName+" "+problem);return config;}

 internal static PortalConfig Parse(string json){
  var config=new PortalConfig();
  Dictionary<string,object> values;
  try{values=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;}
  catch(ArgumentException ex){return Failed("JSON 형식이 올바르지 않아 기본값을 사용합니다: "+ex.Message);}
  catch(InvalidOperationException ex){return Failed("JSON 형식이 올바르지 않아 기본값을 사용합니다: "+ex.Message);}
  if(values==null)return Failed("최상위 값이 JSON 객체가 아니어서 기본값을 사용합니다.");
  foreach(var pair in values){
   switch(pair.Key){
    case "host":config.Host=Text(config,pair,HostPattern,true,config.Host);break;
    case "loginPath":config.LoginPath=Text(config,pair,PathPattern,false,config.LoginPath);break;
    case "mainPath":config.MainPath=Text(config,pair,PathPattern,false,config.MainPath);break;
    case "browserProcess":config.BrowserProcess=Text(config,pair,ProcessPattern,false,config.BrowserProcess);break;
    case "pollIntervalMs":config.PollIntervalMs=Number(config,pair,50,5000,config.PollIntervalMs);break;
    case "tabSettleMs":config.TabSettleMs=Number(config,pair,0,2000,config.TabSettleMs);break;
    default:config.Problems.Add("알 수 없는 설정 항목을 무시합니다: "+pair.Key);break;
   }
  }
  if(config.LoginPath==config.MainPath){
   config.Problems.Add("loginPath와 mainPath가 같아 두 경로를 기본값으로 되돌립니다.");
   var defaults=new PortalConfig();config.LoginPath=defaults.LoginPath;config.MainPath=defaults.MainPath;
  }
  return config;
 }

 static string Text(PortalConfig config,KeyValuePair<string,object> pair,Regex pattern,bool lower,string fallback){
  var text=pair.Value as string;
  if(text!=null&&lower)text=text.Trim().ToLowerInvariant();
  if(text==null||!pattern.IsMatch(text)){config.Problems.Add(pair.Key+" 값이 올바르지 않아 기본값을 사용합니다.");return fallback;}
  return text;
 }

 static int Number(PortalConfig config,KeyValuePair<string,object> pair,int min,int max,int fallback){
  if(pair.Value is int){int value=(int)pair.Value;if(value>=min&&value<=max)return value;}
  config.Problems.Add(pair.Key+" 값은 "+min+"~"+max+" 사이의 정수여야 해서 기본값을 사용합니다.");
  return fallback;
 }

 internal string Origin{get{return "https://"+Host;}}

 // Single place that decides whether a URL is the portal page we are allowed to automate.
 internal bool IsPortalUrl(Uri url,string path){
  return url!=null&&url.Scheme==Uri.UriSchemeHttps&&url.Host==Host&&url.Port==443&&url.AbsolutePath==path;
 }
}
