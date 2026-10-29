using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
internal static class PreloginRecoveryTests {
 static PreloginRecoveryFacts Ready(){return new PreloginRecoveryFacts{TargetVerified=true,Blank=true,ReloadSupported=true};}
 internal static void Run(Action<bool,string> check){
  var gate=new PreloginRecoveryGate();
  check(!gate.TryClaim(Ready(),0)&&!gate.TryClaim(Ready(),9999)&&gate.TryClaim(Ready(),10000),"recovery/verified blank must persist ten seconds before refresh claim");
  check(!gate.TryClaim(Ready(),20000)&&!gate.TryClaim(Ready(),100000),"recovery/claimed refresh cannot repeat even if dispatch failed or outcome is unknown");
  foreach(string blocked in new[]{"target","content","certificate","password","dialog","pending","cancelled","unsupported"}){
   var g=new PreloginRecoveryGate();g.TryClaim(Ready(),0);var f=Ready();
   if(blocked=="target")f.TargetVerified=false;if(blocked=="content")f.Blank=false;if(blocked=="certificate")f.CertificateVisible=true;if(blocked=="password")f.PasswordVisible=true;if(blocked=="dialog")f.BlockingDialog=true;if(blocked=="pending")f.Pending=true;if(blocked=="cancelled")f.Cancelled=true;if(blocked=="unsupported")f.ReloadSupported=false;
   check(!g.TryClaim(f,10000),"recovery/block refresh for "+blocked);
   if(blocked=="certificate"||blocked=="password")check(!g.TryClaim(Ready(),20000)&&!g.TryClaim(Ready(),30000),"recovery/previous authentication UI permanently closes recovery gate for "+blocked);
  }
  var transient=new PreloginRecoveryGate();transient.TryClaim(Ready(),0);var populated=Ready();populated.Blank=false;transient.TryClaim(populated,9999);
  check(!transient.TryClaim(Ready(),10000)&&!transient.TryClaim(Ready(),19999)&&transient.TryClaim(Ready(),20000),"recovery/nonblank interval resets blank timer without reloading usable page");
  var samples=new List<PreloginTraceData>();var observation=new PreloginStateTrace(samples.Add);var facts=Ready();observation.Observe(facts);observation.Observe(facts);facts.Blank=false;observation.Observe(facts);
  check(samples.Count==2&&samples[0].blank==true&&samples[1].blank==false,"recovery/log boolean state transitions without repeated samples");
  observation.Observe(null);check(!samples.Last().document_found&&!samples.Last().blank.HasValue,"recovery/missing document is recorded as unknown rather than blank");
  var fields=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(new JavaScriptSerializer().Serialize(samples[0]));
  var allowed=new HashSet<string>{"document_found","target_verified","blank","certificate_visible","password_visible","blocking_dialog","pending","reload_supported"};
  check(fields.Count==allowed.Count&&fields.Keys.All(allowed.Contains)&&fields.Values.All(v=>v==null||v is bool),"recovery/page observations contain booleans only without URLs text or credential values");
  bool unaffected=true;try{new PreloginStateTrace(d=>{throw new InvalidOperationException("INJECTED");}).Observe(facts);}catch{unaffected=false;}check(unaffected,"recovery/diagnostic sink failure cannot change recovery behavior");
 }
}
