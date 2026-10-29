using System;

internal static class StartupPreferences {
 internal static void Save(Settings settings,bool automatic,Action persist){bool previous=settings.AutoRunAtLogon;settings.AutoRunAtLogon=automatic;try{persist();}catch{settings.AutoRunAtLogon=previous;throw;}}
}
