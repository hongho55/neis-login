using System;

internal sealed class PreloginRecoveryFacts {
 internal bool TargetVerified,Blank,CertificateVisible,PasswordVisible,BlockingDialog,Pending,Cancelled,ReloadSupported;
 internal bool Safe {get{return TargetVerified&&Blank&&!CertificateVisible&&!PasswordVisible&&!BlockingDialog&&!Pending&&!Cancelled&&ReloadSupported;}}
}
// Boolean observations already used by recovery. No extra UI reads or actions.
internal sealed class PreloginTraceData {
 public bool document_found;public bool? target_verified,blank,certificate_visible,password_visible,blocking_dialog,pending,reload_supported;
}
internal sealed class PreloginStateTrace {
 string previous;readonly Action<PreloginTraceData> record;
 internal PreloginStateTrace():this(data=>RunTrace.Record(TraceEvent.PRELOGIN_STATE,prelogin:data)){}
 internal PreloginStateTrace(Action<PreloginTraceData> sink){record=sink;}
 internal void Observe(PreloginRecoveryFacts facts){try{
  var data=new PreloginTraceData{document_found=facts!=null};
  if(facts!=null){data.target_verified=facts.TargetVerified;data.blank=facts.Blank;data.certificate_visible=facts.CertificateVisible;data.password_visible=facts.PasswordVisible;data.blocking_dialog=facts.BlockingDialog;data.pending=facts.Pending;data.reload_supported=facts.ReloadSupported;}
  string key=data.document_found+"/"+data.target_verified+"/"+data.blank+"/"+data.certificate_visible+"/"+data.password_visible+"/"+data.blocking_dialog+"/"+data.pending+"/"+data.reload_supported;
  if(previous!=key){previous=key;record(data);}
 }catch(Exception){}}
}
internal sealed class PreloginRecoveryGate {
 internal const long BlankIntervalMs=10000;
 readonly StableElementObservation blank=new StableElementObservation(BlankIntervalMs);bool used,certificateSeen;
 internal bool TryClaim(PreloginRecoveryFacts facts,long now){
  certificateSeen|=facts.CertificateVisible||facts.PasswordVisible;
  if(used||certificateSeen||!facts.Safe){blank.Observe(null,false,now);return false;}
  if(!blank.Observe("VERIFIED_LOGIN_BLANK",true,now))return false;
  used=true;return true; // Claimed before dispatch; an uncertain invocation is never repeated.
 }
}
