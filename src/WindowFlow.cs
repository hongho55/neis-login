using System;
using System.Collections.Generic;
using System.Linq;

// Policy tests use fake handles; they never activate windows or read credentials.
internal static class WindowFlow {
 internal static string LaunchArguments(string profile,string url){
  if(profile!="Default"&&!System.Text.RegularExpressions.Regex.IsMatch(profile??"",@"^Profile \d+$"))throw new SafeFailure("Edge 프로필 설정이 올바르지 않습니다.");
  return "--new-window --profile-directory=\""+profile+"\" "+url;
 }
 internal static IntPtr NewWindow(IEnumerable<IntPtr> candidates,HashSet<IntPtr> previous){
  var matches=candidates.Where(h=>!previous.Contains(h)).Distinct().ToList();
  if(matches.Count>1)throw new SafeFailure("새 업무포털 창이 여러 개여서 대상을 정하지 못했어요. 자동 입력하지 않습니다.");
  return matches.Count==1?matches[0]:IntPtr.Zero;
 }
}

internal sealed class ForegroundReturn {
 readonly IntPtr target;
 readonly HashSet<IntPtr> controlled=new HashSet<IntPtr>();
 readonly Func<IntPtr> current;
 readonly Func<IntPtr,bool> usable,activate;
 internal ForegroundReturn(IntPtr original,Func<IntPtr> getCurrent,Func<IntPtr,bool> canUse,Func<IntPtr,bool> setForeground){target=original;current=getCurrent;usable=canUse;activate=setForeground;}
 internal void Own(IntPtr hwnd){if(hwnd!=IntPtr.Zero&&hwnd!=target)controlled.Add(hwnd);}
 internal bool TryReturn(){
  // If the user has already switched to another app, preserve their choice.
  IntPtr foreground=current();
  if(target==IntPtr.Zero||foreground==target||!controlled.Contains(foreground)||!usable(target))return false;
  return activate(target);
 }
}
