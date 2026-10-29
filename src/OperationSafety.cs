using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security;

// Cancellation and submission have one explicit, testable ordering boundary.
internal sealed class OperationSafety {
 readonly object sync=new object();bool cancelled,submissionStarted;
 internal bool Cancelled {get{lock(sync)return cancelled;}}
 internal void Begin(){lock(sync){cancelled=false;submissionStarted=false;}}
 internal bool RequestStop(){lock(sync){cancelled=true;return submissionStarted;}}
 internal void Check(){lock(sync){if(cancelled)throw new SafeFailure(submissionStarted?"중단했습니다. 이미 시작한 제출은 취소할 수 없으며 자동 재제출하지 않습니다.":"제출 전에 중단했습니다. 새 제출은 시작하지 않습니다.");}}
 internal void Input(Action persistPending,Action input){
  Check();try{persistPending();}
  catch(IOException){throw StateSaveFailure();}catch(UnauthorizedAccessException){throw StateSaveFailure();}catch(SecurityException){throw StateSaveFailure();}
  Check();input();
 }
 static SafeFailure StateSaveFailure(){return new SafeFailure("필수 시도 상태를 저장하지 못했습니다. 암호 읽기·입력·제출을 시작하지 않았습니다.");}
 internal void Submit(Action dispatch){
  lock(sync){if(cancelled)throw new SafeFailure("제출 전에 중단했습니다. 확인 버튼을 누르지 않았습니다.");if(submissionStarted)throw new SafeFailure("이미 시작한 제출을 반복하지 않습니다.");submissionStarted=true;}
  // No UI/API call holds the lock. A later stop is reported as after submission start.
  dispatch();
 }
 internal static void RequireResolved(bool pending){if(pending)throw new SafeFailure("이전 입력 결과가 미확인입니다. 화면을 확인하고 오류 상태를 직접 초기화한 뒤 진행해 주세요.");}
}

internal sealed class WindowLaunchState {
 internal bool Pending {get;private set;}
 internal bool Failed {get;private set;}
 internal HashSet<IntPtr> Before {get;private set;}
 internal void Begin(IEnumerable<IntPtr> before){Before=new HashSet<IntPtr>(before);Pending=true;Failed=false;}
 internal void MarkFailed(){if(Pending)Failed=true;}
 internal void PrepareExplicitRetry(IEnumerable<IntPtr> current){if(Pending&&Failed&&!current.Any(h=>!Before.Contains(h)))Reset();}
 internal void Reset(){Pending=false;Failed=false;Before=null;}
}
