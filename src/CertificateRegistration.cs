using System;

internal static class CertificateRegistration {
 // Settings are explicitly blocked before touching the local credential. If
 // completion cannot be persisted, the old metadata returns in a blocked state.
 internal static void Commit(Settings settings,CertificateInfo cert,CertificateStorage location,Action validate,Action persist,Action persistSecret){
  validate();
  var previous=new[]{settings.CertUser,settings.CertExpiry,settings.CertIssuer,settings.CertMedia,settings.CertDrive};bool configured=settings.CertStorageConfigured,verified=settings.Verified,pending=settings.PendingAttempt;
  settings.RegistrationPending=true;persist();
  try{
   validate();persistSecret();
   settings.CertUser=cert.User;
   settings.CertExpiry=cert.Expiry;
   settings.CertIssuer=cert.Issuer;
   settings.CertMedia=location.Media;
   settings.CertDrive=location.Drive;
   settings.CertStorageConfigured=true;
   settings.Verified=false;
   settings.RegistrationPending=false;
   settings.PendingAttempt=false;
   persist();
  }catch{
   settings.CertUser=previous[0];
   settings.CertExpiry=previous[1];
   settings.CertIssuer=previous[2];
   settings.CertMedia=previous[3];
   settings.CertDrive=previous[4];
   settings.CertStorageConfigured=configured;
   settings.Verified=verified;
   settings.RegistrationPending=true;
   settings.PendingAttempt=pending;
   try{persist();}catch{}throw;
  }
 }
}
