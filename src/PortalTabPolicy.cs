using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class PortalTabFact {
 internal string Key,Name;
}
internal static class PortalTabPolicy {
 internal static bool IsPortalName(string name){return (name??"").StartsWith("업무포털 로그인")||(name??"").StartsWith("업무포털 메인");}
 internal static string Resolve(IEnumerable<PortalTabFact> source,string remembered){
  var tabs=source.ToList();var named=tabs.Where(t=>IsPortalName(t.Name)).ToList();
  if(named.Count>1)throw new SafeFailure("같은 업무 창에 포털 탭이 여러 개 표시됩니다. 임의의 탭으로 진행하지 않습니다.");
  if(remembered!=null&&tabs.Count(t=>t.Key==remembered)==1)return remembered;
  return named.Count==1?named[0].Key:null; // A temporary title gap is not a missing-portal verdict.
 }
}
