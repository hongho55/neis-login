using System;

internal static class CertificateStartupTests {
 internal static void Run(Action<bool,string> check){
  var stable=new StableElementObservation(CertificateStartup.ButtonStableMs);
  check(!stable.Observe("DOC/BUTTON",true,0)&&!stable.Observe("DOC/BUTTON",true,1999)&&stable.Observe("DOC/BUTTON",true,2000),"certificate/same visible enabled document and button must remain stable for two seconds");
  var rebuilt=new StableElementObservation(5000);rebuilt.Observe("OLD/BUTTON",true,0);
  check(!rebuilt.Observe("NEW/BUTTON",true,4000)&&!rebuilt.Observe("NEW/BUTTON",true,8999)&&rebuilt.Observe("NEW/BUTTON",true,9000),"certificate/document rebuild restarts stabilization instead of clicking stale target");
  var disabled=new StableElementObservation(5000);disabled.Observe("DOC/BUTTON",true,0);disabled.Observe("DOC/BUTTON",false,4000);
  check(!disabled.Observe("DOC/BUTTON",true,6000)&&!disabled.Observe("DOC/BUTTON",true,10999)&&disabled.Observe("DOC/BUTTON",true,11000),"certificate/disabling or hiding target discards previous ready interval");
  var missing=new StableElementObservation(5000);missing.Observe("DOC/BUTTON",true,0);missing.Observe(null,true,4000);
  check(!missing.Observe("DOC/BUTTON",true,5000)&&missing.Observe("DOC/BUTTON",true,10000),"certificate/missing document cannot inherit previous stabilization");
  var replaced=new StableElementObservation(5000);replaced.Observe("DOC/BUTTON_A",true,0);
  check(!replaced.Observe("DOC/BUTTON_B",true,5000)&&replaced.Observe("DOC/BUTTON_B",true,10000),"certificate/replaced button requires its own full stable interval");
  var backwards=new StableElementObservation(5000);backwards.Observe("X",true,5000);
  check(!backwards.Observe("X",true,1000)&&backwards.Observe("X",true,6000),"certificate/regressing clock does not produce premature readiness");
  bool negative=false;try{new StableElementObservation(-1);}catch(ArgumentOutOfRangeException){negative=true;}
  check(negative&&CertificateStartup.ButtonStableMs==2000&&CertificateStartup.ButtonReadyTimeoutMs==60000&&CertificateStartup.DialogReadyTimeoutMs==60000,"certificate/two-second stability retains sixty-second preparation and list limits");
 }
}
