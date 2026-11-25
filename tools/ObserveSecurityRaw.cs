using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

// Read-only, explicitly targeted Edge Raw View probe. Never reads ValuePattern on edits,
// passwords, certificate captions, or unrelated tabs. No click/focus/reload/credential calls.
internal static class ObserveSecurityRaw {
 [STAThread] static int Main(string[] args){
  if(args.Length!=2)return 2;
  var window=AutomationElement.FromHandle(new IntPtr(Int64.Parse(args[0])));
  if(Process.GetProcessById(window.Current.ProcessId).ProcessName!="msedge")return 3;
  var clock=Stopwatch.StartNew();string previous=null;
  while(clock.ElapsedMilliseconds<Int32.Parse(args[1])){
   try{
    var documents=window.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Document)).Cast<AutomationElement>();
    var doc=documents.SingleOrDefault(d=>{object p;return !d.Current.IsOffscreen&&d.TryGetCurrentPattern(ValuePattern.Pattern,out p)&&((ValuePattern)p).Current.Value=="https://gwe.eduptl.kr/bpm_lgn_lg00_001.do";});
    string snapshot="DOCUMENT_UNAVAILABLE";
    if(doc!=null){
     var cache=new CacheRequest();cache.Add(AutomationElement.ControlTypeProperty);cache.Add(AutomationElement.ClassNameProperty);cache.Add(AutomationElement.AutomationIdProperty);cache.Add(AutomationElement.IsOffscreenProperty);cache.Add(AutomationElement.BoundingRectangleProperty);
     using(cache.Activate())snapshot=String.Join("\n",doc.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Select(e=>{
      var v=e.Cached;
      return v.ControlType.ProgrammaticName+" id="+v.AutomationId+" class="+v.ClassName+" off="+v.IsOffscreen+" rect="+v.BoundingRectangle;
     }).ToArray());
    }
    if(snapshot!=previous){Console.WriteLine("UTC="+DateTime.UtcNow.ToString("o")+" elapsed="+clock.ElapsedMilliseconds+"\n"+snapshot+"\nEND");Console.Out.Flush();previous=snapshot;}
   }catch(Exception e){Console.WriteLine("UTC="+DateTime.UtcNow.ToString("o")+" UNKNOWN="+e.GetType().Name);}
   Thread.Sleep(100);
  }
  return 0;
 }
}
