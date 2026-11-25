using System;
using System.Linq;

internal enum SecurityLoadingState { Unknown, Loading, Clear }
internal sealed class SecurityLoadingSample {
 internal readonly SecurityLoadingState State;internal readonly string Identity,Reason;
 internal SecurityLoadingSample(SecurityLoadingState state,string identity,string reason=null){State=state;Identity=identity;Reason=reason;}
 internal static SecurityLoadingSample FromVisibility(bool[] visible,string identity){return new SecurityLoadingSample(visible==null?SecurityLoadingState.Unknown:visible.Any(x=>x)?SecurityLoadingState.Loading:SecurityLoadingState.Clear,identity);}
}

// The detector observes; this policy owns bounded, cancellable waiting and clear stabilization.
// A single gate is shared for one operation. Its cumulative time never consumes stage budgets.
internal sealed class SecurityLoadingGate {
 internal const int PollMs=250,StableMs=1000,LimitMs=120000;
 readonly Func<long> clock;readonly Action<int> sleep;readonly Action cancel;readonly Action<string> report;
 readonly int limit,stable,poll;long paused;
 internal long PausedMilliseconds {get{return paused;}}
 internal SecurityLoadingGate(Func<long> clock,Action<int> sleep,Action cancel,Action<string> report,int limit=LimitMs,int stable=StableMs,int poll=PollMs){
  if(limit<=0||stable<0||poll<=0)throw new ArgumentOutOfRangeException();
  this.clock=clock;this.sleep=sleep;this.cancel=cancel;this.report=report;this.limit=limit;this.stable=stable;this.poll=poll;
 }
 internal void WaitWhenBlocked(Func<SecurityLoadingSample> detect){
  cancel();SecurityLoadingSample initial;
  try{initial=detect();}catch(System.Windows.Automation.ElementNotAvailableException){initial=null;}catch(System.Runtime.InteropServices.COMException){initial=null;}catch(InvalidOperationException){initial=null;}
  cancel();
  // A normal screen needs no waiting message or additional stabilization. Only an observed
  // loading/unknown interval starts the bounded wait; later action guards remain immediate.
  if(initial!=null&&initial.State==SecurityLoadingState.Clear&&!String.IsNullOrEmpty(initial.Identity))return;
  bool first=true;Wait(()=>{if(first){first=false;return initial;}return detect();});
 }
 internal void Wait(Func<SecurityLoadingSample> detect){
  long start=clock(),clearSince=-1;string identity=null;SecurityLoadingState? previous=null;
  try{
   while(true){
    cancel();long now=clock();if(paused+now-start>=limit)throw new SafeFailure("보안 로딩 상태를 제한 시간 안에 확인하지 못했습니다. 업무포털 창이 가려지거나 최소화되었는지 확인해 주세요. 암호 재입력·재제출·새로고침하지 않습니다.");
    SecurityLoadingSample sample;
    try{sample=detect();}catch(System.Windows.Automation.ElementNotAvailableException){sample=null;}catch(System.Runtime.InteropServices.COMException){sample=null;}catch(InvalidOperationException){sample=null;}
    cancel();if(paused+clock()-start>=limit)throw new SafeFailure("보안 화면 관찰 중 대기 제한 시간을 초과했습니다. 입력·제출하지 않습니다.");
    var state=sample==null?SecurityLoadingState.Unknown:sample.State;
    if(previous!=state){report(state==SecurityLoadingState.Loading?"보안프로그램 구동 중 · 입력과 클릭을 보류하고 있어요.":state==SecurityLoadingState.Unknown?"보안 화면 확인 대기 · 업무포털 Edge 창을 보이게 해 주세요.":"보안 안내 해제 확인 중 · 화면이 안정되면 다시 확인해요.");previous=state;}
    if(state!=SecurityLoadingState.Clear||String.IsNullOrEmpty(sample.Identity)){clearSince=-1;identity=null;}
    else{
     if(clearSince<0||identity!=sample.Identity){clearSince=clock();identity=sample.Identity;}
     if(clock()-clearSince>=stable){cancel();report("보안 안내 확인 완료 · 현재 인증서 화면을 확인하고 있어요.");return;}
    }
    sleep((int)Math.Min(poll,Math.Max(1,limit-paused-(clock()-start))));
   }
  }finally{paused+=Math.Max(0,clock()-start);}
 }
 internal static void RequireClear(SecurityLoadingSample sample){
  if(sample==null||sample.State!=SecurityLoadingState.Clear||String.IsNullOrEmpty(sample.Identity))throw new SafeFailure("보안 로딩이 나타났거나 화면 상태를 확인할 수 없어 중단했습니다. 이미 입력한 암호를 다시 입력하거나 제출하지 않습니다.");
 }
}
