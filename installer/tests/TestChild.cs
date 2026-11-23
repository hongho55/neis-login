using System;
using System.IO;
internal static class TestChild {
 static int Main(string[] args){
  string target=Environment.GetEnvironmentVariable("TEACHERWORKSTART_SETUP_TEST_ROOT");
  if(args.Length!=1||args[0]!="--install-only"||String.IsNullOrEmpty(target))return 7;
  string full=Path.GetFullPath(target),temp=Path.GetFullPath(Path.GetTempPath());
  if(!full.StartsWith(temp.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!full.Contains("TeacherWorkStart-Setup-Tests-"))return 8;
  string self=System.Reflection.Assembly.GetExecutingAssembly().Location;Directory.CreateDirectory(full);File.Copy(self,Path.Combine(full,"TeacherWorkStart.exe"),true);File.Copy(self+".config",Path.Combine(full,"TeacherWorkStart.exe.config"),true);File.WriteAllText(Path.Combine(full,"arguments.txt"),args[0]);return 0;
 }
}
