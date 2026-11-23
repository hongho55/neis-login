using System;

// This retries only window activation, before credential access. It never sends
// keys, submits authentication, changes policy or substitutes another window.
internal static class ForegroundRecovery {
 internal static bool Acquire(Func<bool> valid,Func<bool> foreground,Action restore,Action request,Action focus,Action<int> pause,Action cancel,Action waiting){
  bool reported=false;
  for(int attempt=0;attempt<3;attempt++){
   Check(valid,cancel);if(foreground())return true;
   restore();Check(valid,cancel);if(foreground())return true;request();Check(valid,cancel);if(foreground())return true;
   // UIA is an alternative supported request, not proof that activation worked.
   // Use it only twice, and never wait indefinitely for foreground permission.
   if(attempt==0||attempt==2){focus();Check(valid,cancel);if(foreground())return true;}
   if(!reported){reported=true;waiting();}
   if(attempt==2)break;
   for(int elapsed=0;elapsed<250;elapsed+=100){Check(valid,cancel);if(foreground())return true;pause(Math.Min(100,250-elapsed));}
  }
  return false;
 }
 static void Check(Func<bool> valid,Action cancel){cancel();if(!valid())throw new SafeFailure("업무 창이 닫혔거나 바뀌어 전면 전환을 중단했습니다. 다른 창에 입력하지 않습니다.");}
}
