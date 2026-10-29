using System;

internal static class CertificateStartup {
 internal const int ButtonReadyTimeoutMs=60000;
 internal const int ButtonStableMs=2000;
 internal const int DialogReadyTimeoutMs=60000;
}

// Readiness is only an observation of the same document/button over time.
// This does not assert that the web security module has finished initializing.
internal sealed class StableElementObservation {
 readonly long required;string key;long since;bool tracking;
 internal StableElementObservation(long milliseconds){if(milliseconds<0)throw new ArgumentOutOfRangeException("milliseconds");required=milliseconds;}
 internal bool Observe(string identity,bool ready,long now){
  if(!ready||String.IsNullOrEmpty(identity)){tracking=false;key=null;return false;}
  if(!tracking||key!=identity||now<since){key=identity;since=now;tracking=true;}
  return now-since>=required;
 }
}
