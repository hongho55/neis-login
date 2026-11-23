using System;
using System.Collections.Generic;
using System.Linq;

// Pure delegates only: no real handles, UI, waiting, credentials or settings.
internal static class ForegroundRecoveryTests {
 sealed class Scenario {
  internal bool Valid=true,Foreground,Cancelled;
  internal int Restores,Requests,FocusRequests,Waiting,Delay,CancelChecks;
  internal readonly List<int> Pauses=new List<int>(),RequestTimes=new List<int>();
  internal Action<Scenario> OnRequest,OnFocus,OnCancel;
  internal Action<Scenario,int> OnPause;
  internal bool Acquire(){
   return ForegroundRecovery.Acquire(()=>Valid,()=>Foreground,()=>Restores++,()=>{Requests++;RequestTimes.Add(Delay);if(OnRequest!=null)OnRequest(this);},()=>{FocusRequests++;if(OnFocus!=null)OnFocus(this);},milliseconds=>{Pauses.Add(milliseconds);Delay+=milliseconds;if(OnPause!=null)OnPause(this,milliseconds);},()=>{CancelChecks++;if(CancelChecks>1000)throw new InvalidOperationException("TEST_WATCHDOG");if(OnCancel!=null)OnCancel(this);if(Cancelled)throw new SafeFailure("TEST_CANCELLED");},()=>Waiting++);
  }
 }
 static bool Cancelled(Scenario scenario){try{scenario.Acquire();return false;}catch(SafeFailure ex){return ex.Message=="TEST_CANCELLED";}}
 static bool InvalidTarget(Scenario scenario){try{scenario.Acquire();return false;}catch(SafeFailure ex){return ex.Message.Contains("업무 창이 닫혔거나 바뀌어");}}
 internal static void Run(Action<bool,string> check){
  var already=new Scenario{Foreground=true};bool alreadyResult=already.Acquire();
  check(alreadyResult&&already.Requests==0&&already.Restores==0&&already.FocusRequests==0&&already.Pauses.Count==0&&already.Waiting==0,"foreground-recovery/already foreground returns without any activation request or delay");

  var second=new Scenario{OnRequest=s=>{if(s.Requests==2)s.Foreground=true;}};bool secondResult=second.Acquire();
  check(secondResult&&second.Requests==2&&second.Restores==2&&second.FocusRequests==1&&second.Delay==250&&second.Waiting==1,"foreground-recovery/failed first request can succeed on the second request after a paced delay");

  var uia=new Scenario{OnFocus=s=>s.Foreground=true};bool uiaResult=uia.Acquire();
  check(uiaResult&&uia.Requests==1&&uia.FocusRequests==1&&uia.Restores==1&&uia.Delay==0&&uia.Waiting==0,"foreground-recovery/supported focus fallback succeeds only after foreground observation");

  bool apiFinished=false;
  var claimed=new Scenario{OnRequest=s=>apiFinished=true};bool claimedResult=claimed.Acquire();
  check(!claimedResult&&apiFinished&&!claimed.Foreground&&claimed.Requests==3&&claimed.FocusRequests==2,"foreground-recovery/completed API requests do not count as actual foreground success");

  var paced=new Scenario();bool pacedResult=paced.Acquire();
  check(!pacedResult&&paced.RequestTimes.SequenceEqual(new[]{0,250,500})&&paced.Requests==3&&paced.Restores==3&&paced.Delay==500&&paced.Pauses.All(ms=>ms>0&&ms<=100)&&paced.FocusRequests==2&&paced.Waiting==1,"foreground-recovery/all denied activation stops after three attempts and two short cancellable intervals");

  var cancelledDuringPause=new Scenario{OnPause=(s,ms)=>s.Cancelled=true};
  check(Cancelled(cancelledDuringPause)&&cancelledDuringPause.Requests==1&&cancelledDuringPause.Restores==1&&cancelledDuringPause.FocusRequests==1&&cancelledDuringPause.Pauses.Count==1,"foreground-recovery/cancellation during a delay prevents another activation request");

  var invalid=new Scenario{Valid=false};
  check(InvalidTarget(invalid)&&invalid.Restores==0&&invalid.Requests==0&&invalid.FocusRequests==0&&invalid.Pauses.Count==0,"foreground-recovery/invalid initial window identity stops before touching any target");

  var replaced=new Scenario{OnRequest=s=>s.Valid=false};
  check(InvalidTarget(replaced)&&replaced.Requests==1&&replaced.Restores==1&&replaced.FocusRequests==0&&replaced.Pauses.Count==0,"foreground-recovery/window identity change after request stops without focus fallback or replacement target");

  var observed=new Scenario{OnPause=(s,ms)=>s.Foreground=true};bool observedResult=observed.Acquire();
  check(observedResult&&observed.Requests==1&&observed.FocusRequests==1&&observed.Pauses.Count==1&&observed.Delay==100,"foreground-recovery/foreground arriving during observation returns without another request");

  var preCancelled=new Scenario{Cancelled=true};
  check(Cancelled(preCancelled)&&preCancelled.Restores==0&&preCancelled.Requests==0&&preCancelled.FocusRequests==0,"foreground-recovery/pre-existing cancellation blocks every activation attempt");
 }
}
