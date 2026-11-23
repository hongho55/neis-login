using System;

// A returned call means the official menu request was dispatched, not that
// the destination page loaded or authenticated. Never retries an uncertain call.
internal static class SystemLaunchRequests {
 internal static int Send(bool neis,bool kedu,Action<string,string> dispatch){
  int count=0;
  if(neis){dispatch("나이스","gwe.neis.go.kr");count++;}
  if(kedu){dispatch("K-에듀파인","klef.gwe.go.kr");count++;}
  return count;
 }
}
