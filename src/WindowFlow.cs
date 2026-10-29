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

internal enum ForegroundReturnOutcome { NO_ORIGINAL, ALREADY_ORIGINAL, USER_WINDOW_SELECTED, ORIGINAL_UNAVAILABLE, RESTORED, RESTORE_FAILED }
internal sealed class ForegroundReturn {
 readonly IntPtr target;
 readonly HashSet<IntPtr> controlled=new HashSet<IntPtr>();
 readonly Func<IntPtr> current;
 readonly Func<IntPtr,bool> usable,activate;
 internal ForegroundReturn(IntPtr original,Func<IntPtr> getCurrent,Func<IntPtr,bool> canUse,Func<IntPtr,bool> setForeground){target=original;current=getCurrent;usable=canUse;activate=setForeground;}
 internal void Own(IntPtr hwnd){if(hwnd!=IntPtr.Zero&&hwnd!=target)controlled.Add(hwnd);}
 internal IntPtr Target {get{return target;}}
 internal bool TryReturn(Action<ForegroundReturnOutcome> observed=null){
  // If the user has already switched to another app, preserve their choice.
  IntPtr foreground=current();
  ForegroundReturnOutcome reason;
  if(target==IntPtr.Zero)reason=ForegroundReturnOutcome.NO_ORIGINAL;
  else if(foreground==target)reason=ForegroundReturnOutcome.ALREADY_ORIGINAL;
  else if(!controlled.Contains(foreground))reason=ForegroundReturnOutcome.USER_WINDOW_SELECTED;
  else if(!usable(target))reason=ForegroundReturnOutcome.ORIGINAL_UNAVAILABLE;
  else {bool result=activate(target);if(observed!=null)try{observed(result?ForegroundReturnOutcome.RESTORED:ForegroundReturnOutcome.RESTORE_FAILED);}catch(Exception){}return result;}
  if(observed!=null)try{observed(reason);}catch(Exception){}return false;
 }
}
