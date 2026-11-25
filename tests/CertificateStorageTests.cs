using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

// Pure storage/identity policies with fictional certificate metadata only.
// No real UI, settings files, credential APIs or input fields are accessed.
internal static class CertificateStorageTests {
 static bool Refuses(Action action){try{action();return false;}catch(SafeFailure){return true;}}
 static bool FailsToPersist(Action action){try{action();return false;}catch(IOException){return true;}}
 static Settings RegistrationState(){return new Settings{CertUser="OLD_CERT",CertExpiry="2099-01-01",CertIssuer="OLD_ISSUER",CertMedia=CertificateStorage.HardDisk,CertDrive="",CertStorageConfigured=true,Verified=true,AutoRunAtLogon=false,OpenNeis=false,OpenKedu=true};}
 static bool OldMetadata(Settings state){return state.CertUser=="OLD_CERT"&&state.CertExpiry=="2099-01-01"&&state.CertIssuer=="OLD_ISSUER"&&state.CertMedia==CertificateStorage.HardDisk&&state.CertDrive==""&&state.CertStorageConfigured&&state.Verified;}
 static Settings Snapshot(Settings state){return new Settings{CertUser=state.CertUser,CertExpiry=state.CertExpiry,CertIssuer=state.CertIssuer,CertMedia=state.CertMedia,CertDrive=state.CertDrive,CertStorageConfigured=state.CertStorageConfigured,Verified=state.Verified,RegistrationPending=state.RegistrationPending,PendingAttempt=state.PendingAttempt,AutoRunAtLogon=state.AutoRunAtLogon,OpenNeis=state.OpenNeis,OpenKedu=state.OpenKedu};}
 static void Registration(Action<bool,string> check){
  var certificate=new CertificateInfo{User="NEW_CERT",Expiry="2099-12-31",Issuer="NEW_ISSUER"};var location=new CertificateStorage(CertificateStorage.Removable,"D:");
  var success=RegistrationState();var writes=new List<Settings>();int validations=0,localWrites=0;bool blockedDuringLocalWrite=false;
  CertificateRegistration.Commit(success,certificate,location,()=>validations++,()=>writes.Add(Snapshot(success)),()=>{localWrites++;blockedDuringLocalWrite=success.RegistrationPending&&OldMetadata(success);});
  check(validations==2&&writes.Count==2&&localWrites==1&&blockedDuringLocalWrite&&writes[0].RegistrationPending&&OldMetadata(writes[0]),"registration/persists blocked old metadata before the one local credential callback");
  check(success.CertUser==certificate.User&&success.CertExpiry==certificate.Expiry&&success.CertIssuer==certificate.Issuer&&success.CertMedia==CertificateStorage.Removable&&success.CertDrive=="D:"&&success.CertStorageConfigured&&!success.Verified&&!success.RegistrationPending&&!writes[1].RegistrationPending,"registration/success commits matching certificate and drive and requires a fresh verification");
  check(!success.AutoRunAtLogon&&!success.OpenNeis&&success.OpenKedu&&!success.PendingAttempt,"registration/success preserves startup behavior system choices and resolved input state");
  var unresolved=RegistrationState();unresolved.PendingAttempt=true;bool pendingDuringWrite=false;
  CertificateRegistration.Commit(unresolved,certificate,location,()=>{},()=>{},()=>pendingDuringWrite=unresolved.PendingAttempt);
  check(pendingDuringWrite&&!unresolved.PendingAttempt&&!unresolved.RegistrationPending&&!unresolved.Verified,"registration/explicit registration accepts old pending and clears it only after saving new credentials");
  var failedPending=RegistrationState();failedPending.PendingAttempt=true;int pendingWrites=0;
  check(FailsToPersist(()=>CertificateRegistration.Commit(failedPending,certificate,location,()=>{},()=>{if(++pendingWrites==2)throw new IOException("TEST");},()=>{}))&&failedPending.PendingAttempt&&failedPending.RegistrationPending&&OldMetadata(failedPending),"registration/failed completion restores old unresolved attempt and blocks automatic login");
  var cancelledPending=RegistrationState();cancelledPending.PendingAttempt=true;
  check(Refuses(()=>CertificateRegistration.Commit(cancelledPending,certificate,location,()=>{throw new SafeFailure("cancelled");},()=>{},()=>{}))&&cancelledPending.PendingAttempt,"registration/cancelled validation never clears previous pending attempt");

  var firstFailure=RegistrationState();int firstPersists=0,firstLocalWrites=0;
  check(FailsToPersist(()=>CertificateRegistration.Commit(firstFailure,certificate,location,()=>{},()=>{firstPersists++;throw new IOException("TEST_FIRST_PERSIST");},()=>firstLocalWrites++))&&firstPersists==1&&firstLocalWrites==0&&firstFailure.RegistrationPending&&OldMetadata(firstFailure),"registration/initial persistence failure blocks all local credential writes and preserves old metadata");

  var completionFailure=RegistrationState();var failedWrites=new List<Settings>();int completionPersists=0,completionLocalWrites=0;
  check(FailsToPersist(()=>CertificateRegistration.Commit(completionFailure,certificate,location,()=>{},()=>{completionPersists++;failedWrites.Add(Snapshot(completionFailure));if(completionPersists==2)throw new IOException("TEST_COMPLETION_PERSIST");},()=>completionLocalWrites++))&&completionPersists==3&&completionLocalWrites==1&&OldMetadata(completionFailure)&&completionFailure.RegistrationPending,"registration/completion persistence failure restores old metadata in a blocked state after one local write");
  check(failedWrites.Count==3&&failedWrites[0].RegistrationPending&&OldMetadata(failedWrites[0])&&!failedWrites[1].RegistrationPending&&failedWrites[1].CertDrive=="D:"&&failedWrites[2].RegistrationPending&&OldMetadata(failedWrites[2]),"registration/failed completion is followed by a persisted blocked recovery snapshot");

  var firstValidationFailure=RegistrationState();int rejectedPersists=0,rejectedLocalWrites=0;
  check(Refuses(()=>CertificateRegistration.Commit(firstValidationFailure,certificate,location,()=>{throw new SafeFailure("TEST_FIRST_VALIDATION");},()=>rejectedPersists++,()=>rejectedLocalWrites++))&&rejectedPersists==0&&rejectedLocalWrites==0&&!firstValidationFailure.RegistrationPending&&OldMetadata(firstValidationFailure),"registration/first validation refusal performs no persistence or local credential callback");

  var changedLocation=RegistrationState();int changedValidations=0,changedPersists=0,changedLocalWrites=0;
  check(Refuses(()=>CertificateRegistration.Commit(changedLocation,certificate,location,()=>{changedValidations++;if(changedValidations==2)throw new SafeFailure("TEST_LOCATION_CHANGED");},()=>changedPersists++,()=>changedLocalWrites++))&&changedValidations==2&&changedPersists==2&&changedLocalWrites==0&&changedLocation.RegistrationPending&&OldMetadata(changedLocation),"registration/location change on second validation preserves old metadata and blocks local credential writing");

  var registrationPending=RegistrationState();registrationPending.RegistrationPending=true;int credentialLookups=0;
  string pendingReason=AutoRunEligibility.Check(registrationPending,()=>{credentialLookups++;return true;});
  check(pendingReason!=null&&credentialLookups==0&&registrationPending.RegistrationPending,"registration/unfinished registration blocks automatic work before credential availability lookup");
  var invalidLocation=RegistrationState();invalidLocation.CertMedia=CertificateStorage.Removable;invalidLocation.CertDrive="D:\\cert";
  string locationReason=AutoRunEligibility.Check(invalidLocation,()=>{credentialLookups++;return true;});
  check(locationReason!=null&&credentialLookups==0&&invalidLocation.CertDrive=="D:\\cert","registration/invalid saved storage blocks automatic work before credential availability lookup without rewriting it");
 }
 internal static void Run(Action<bool,string> check){
  check(CertificateRowData.Parse(new[]{"구분","사용자","만료일","발급자"})==null,"storage/fixed table header is never included in the certificate row refresh guard");
  var data=CertificateRowData.Parse(new[]{"개인","TEST_CERT","2099-12-31","TEST_ISSUER"});
  check(data!=null&&data.User=="TEST_CERT"&&data.Expiry=="2099-12-31"&&data.Issuer=="TEST_ISSUER","storage/certificate row parsing extracts the same identity used by selection guards");
  check(CertificateRowData.IdentityKey(new[]{"개인","TEST_CERT","2099-12-31","TEST_ISSUER"})==CertificateRowData.IdentityKey(new[]{"개인","TEST_CERT","2099-12-31","TEST_ISSUER"}),"storage/identical duplicate certificate rows collapse to one logical identity");
  check(CertificateRowData.IdentityKey(new[]{"개인","TEST_CERT","2099-12-31","TEST_ISSUER"})!=CertificateRowData.IdentityKey(new[]{"개인","OTHER","2099-12-31","TEST_ISSUER"}),"storage/different certificate identity remains distinct and guarded");
  check(CertificateRowData.Parse(new[]{"개인","TEST_CERT","2099-99-99","TEST_ISSUER"})==null,"storage/invalid dates cannot turn placeholders into certificate rows");
  const string originalTarget="TeacherWorkStart/gwe/593B0EA56F6ECA2B33BDAF6270C457A8BF125B6C97AE6B7A238007E04A8AECA1";
  var old=new JavaScriptSerializer().Deserialize<Settings>("{\"CertUser\":\"TEST_CERT\",\"CertExpiry\":\"2099-12-31\",\"CertIssuer\":\"TEST_ISSUER\",\"OpenNeis\":false,\"PendingAttempt\":true,\"Verified\":true}");
  check(old.CertMedia==CertificateStorage.HardDisk&&old.CertDrive==""&&!old.CertStorageConfigured&&!old.RegistrationPending,"storage/legacy settings default to hard disk without inventing a completed storage setup or registration attempt");
  check(old.Target==originalTarget&&old.PendingAttempt&&old.Verified&&!old.OpenNeis,"storage/legacy certificate and portal retain their original target identity and saved safety state");
  var hardDisk=CertificateStorage.From(old);
  check(hardDisk.Media==CertificateStorage.HardDisk&&hardDisk.Drive=="","storage/legacy hard disk settings form a valid storage selection");
  old.CertMedia=CertificateStorage.Removable;old.CertDrive="z:";old.CertStorageConfigured=true;old.RegistrationPending=true;
  check(CertificateStorage.From(old).Drive=="Z:"&&old.Target==originalTarget,"storage/changing storage location preserves the existing certificate and portal credential identity");

  foreach(string drive in new[]{"D:","d:"," d: ","Z:","z:","\t z: \r\n"}){
   string expected=drive.Trim().ToUpperInvariant();var storage=new CertificateStorage(CertificateStorage.Removable,drive);
   check(storage.Media==CertificateStorage.Removable&&storage.Drive==expected&&CertificateStorage.NormalizeDrive(drive)==expected,"storage/removable drive normalizes letter case and outer whitespace for "+expected);
  }
  foreach(string media in new[]{null,"","USB","hdd","removable","HDD ","REMOVABLE --auto"}){
   string bad=media;check(Refuses(()=>new CertificateStorage(bad,"")),"storage/unknown or decorated media is rejected");
  }
  foreach(string drive in new[]{null,""," ","D","DD:","1:","D:\\","D:/","D:\\cert","../D:","D: E:","D:;E:","D: --auto","D:\nE:","D:\0","Ｄ:"}){
   string bad=drive;check(Refuses(()=>new CertificateStorage(CertificateStorage.Removable,bad)),"storage/removable storage rejects missing letters paths and decorated drive input");
  }
  check(Refuses(()=>new CertificateStorage(CertificateStorage.HardDisk,"D:")),"storage/hard disk cannot silently retain a removable drive selector");
  check(CertificateStorage.NormalizeDrive(null)==""&&CertificateStorage.NormalizeDrive("")==""&&CertificateStorage.NormalizeDrive(" \t ")=="","storage/empty normalized drive remains valid only for media that does not require a drive");

  var removable=new CertificateStorage(CertificateStorage.Removable,"D:");
  foreach(string label in new[]{"D:","d:","(D:)","USB 드라이브 (D:)","이동식디스크 D:"}){
   check(removable.IsDriveName(label),"storage/drive label identifies the selected drive including parenthesized volume labels");
  }
  foreach(string label in new[]{null,"","E:","USB 드라이브 (Z:)","사용 가능한 드라이브 D: E:","USB (D:) (Z:)","D: / E:","DD:","XD:","D:backup"}){
   check(!removable.IsDriveName(label),"storage/other ambiguous aggregate and decorated drive labels cannot select the target drive");
  }
  check(!hardDisk.IsDriveName("D:")&&!hardDisk.IsDriveName("USB 드라이브 (D:)"),"storage/hard disk mode never matches a removable drive row");
  check(hardDisk.IsMediaName("하드디스크")&&hardDisk.IsMediaName(" 하드 디스크 ")&&hardDisk.IsMediaName("\t하드\n디스크"),"storage/hard disk media name accepts whitespace normalization only");
  check(removable.IsMediaName("이동식디스크")&&removable.IsMediaName(" 이동식 디스크 "),"storage/removable media name accepts whitespace normalization only");
  check(!hardDisk.IsMediaName(null)&&!hardDisk.IsMediaName("인증서 하드디스크")&&!hardDisk.IsMediaName("하드디스크 선택")&&!hardDisk.IsMediaName("이동식디스크"),"storage/hard disk media name rejects unrelated words or the other storage media");
  check(!removable.IsMediaName("이동식디스크 선택")&&!removable.IsMediaName("USB 이동식디스크")&&!removable.IsMediaName("하드디스크"),"storage/removable media name rejects broader parent labels and the other media");

  var fresh=new FreshCertificateRows(new[]{"OLD_A","OLD_B"});
  check(!fresh.Ready(new string[0]),"storage/empty certificate list cannot prove the selected storage has loaded");
  check(!fresh.Ready(new[]{"OLD_A","OLD_B"})&&!fresh.Ready(new[]{"NEW_A","OLD_B"}),"storage/any remaining old runtime row prevents premature use of the previous storage list");
  check(fresh.Ready(new[]{"NEW_A","NEW_B"}),"storage/nonempty entirely new certificate rows confirm the list was rebuilt");
  var first=new FreshCertificateRows(Enumerable.Empty<string>());
  check(!first.Ready(new string[0])&&first.Ready(new[]{"FIRST_ROW"}),"storage/initial empty snapshot still requires at least one freshly observed row");
  Registration(check);
 }
}
