using System;
using System.Collections.Generic;
using System.Linq;

internal static class SystemLaunchRequestTests {
 internal static void Run(Action<bool,string> check){
  var requests=new List<string>();
  int sent=SystemLaunchRequests.Send(true,true,(name,host)=>requests.Add(name+"@"+host));
  check(sent==2&&requests.SequenceEqual(new[]{"나이스@gwe.neis.go.kr","K-에듀파인@klef.gwe.go.kr"}),"launch/selected official requests run in order exactly once without a destination observation callback");
  foreach(bool neis in new[]{false,true})foreach(bool kedu in new[]{false,true}){
   requests.Clear();sent=SystemLaunchRequests.Send(neis,kedu,(name,host)=>requests.Add(name));
   check(sent==(neis?1:0)+(kedu?1:0)&&requests.Count==sent&&requests.Contains("나이스")==neis&&requests.Contains("K-에듀파인")==kedu,"launch/respects selection "+neis+" "+kedu);
  }
  requests.Clear();bool failed=false,completed=false;
  try{SystemLaunchRequests.Send(true,true,(name,host)=>{requests.Add(name);throw new SafeFailure("TEST_UNKNOWN_DISPATCH_RESULT");});completed=true;}catch(SafeFailure){failed=true;}
  check(failed&&!completed&&requests.SequenceEqual(new[]{"나이스"}),"launch/uncertain first request is neither repeated nor reported as completion");
  requests.Clear();failed=false;
  try{SystemLaunchRequests.Send(true,true,(name,host)=>{requests.Add(name);if(name=="K-에듀파인")throw new SafeFailure("TEST_UNKNOWN_DISPATCH_RESULT");});}catch(SafeFailure){failed=true;}
  check(failed&&requests.SequenceEqual(new[]{"나이스","K-에듀파인"}),"launch/second request failure never repeats the first SSO request");
 }
}
