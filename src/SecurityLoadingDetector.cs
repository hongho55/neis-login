using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Automation;

// Restricted visual observation of the official kcase spinner, never OCR/password readback.
// UIA alone did not expose the overlay class or text in actual Edge (2026-11-24).
internal static class SecurityLoadingDetector {
 [StructLayout(LayoutKind.Sequential)]struct Rect {internal int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out Rect value,int size);
 [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]static extern int DwmGetCloaked(IntPtr h,int attribute,out int value,int size);
 static readonly Bitmap template=Load();
 static Bitmap Load(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SecurityLoading.png")){if(stream==null)throw new InvalidOperationException("Security loading reference missing");using(var image=new Bitmap(stream))return new Bitmap(image);}}
 internal static SecurityLoadingSample Observe(PortalContext c){
  if(c==null)return Unknown("document-unavailable");
  string identity=c.Hwnd.ToInt64()+"/"+String.Join("-",c.Doc.GetRuntimeId());
  if(!Portal.DocumentBelongsToWindow(c.Doc,c.Hwnd))return Unknown("document-window-mismatch");
  if(!Native.IsWindow(c.Hwnd)||!Native.IsWindowVisible(c.Hwnd)||Native.IsIconic(c.Hwnd)||c.Doc.Current.IsOffscreen)return Unknown("window-unobservable");
  var bounds=c.Doc.Current.BoundingRectangle;
  if(bounds.IsEmpty||bounds.Width<350||bounds.Height<200)return Unknown("viewport-too-small");
  var area=Rectangle.FromLTRB((int)Math.Ceiling(bounds.Left),(int)Math.Ceiling(bounds.Top),(int)Math.Floor(bounds.Right),(int)Math.Floor(bounds.Bottom));
  if(!System.Windows.Forms.SystemInformation.VirtualScreen.Contains(area))return Unknown("viewport-outside-desktop");
  var capture=Rectangle.Intersect(area,new Rectangle(area.Left+area.Width/2-525,area.Top+area.Height/2-300,1050,561));
  Native.SecurityObservationArea(capture);
  // The actual Edge UIA tree exposes the spinner as an anonymous centered 350x187
  // group. When that marker is absent, avoid an expensive desktop capture entirely.
  // A marker appearing routes through the stricter image comparison below.
  if(!HasLoadingShapedGroup(c.Doc,bounds))return new SecurityLoadingSample(SecurityLoadingState.Clear,identity);
  // Any visible top-level window above this document makes observation unknown. A background
  // window is supported only when its whole document remains unobscured. Never capture other apps.
  if(!Unobscured(c.Hwnd,capture))return Unknown("document-occluded");
  using(var frame=new Bitmap(capture.Width,capture.Height)){
   try{using(var graphics=Graphics.FromImage(frame))graphics.CopyFromScreen(capture.Location,Point.Empty,capture.Size);}
   catch(System.ComponentModel.Win32Exception){return Unknown("capture-unavailable");}
   catch(ArgumentException){return Unknown("capture-unavailable");}
   if(c.Doc.Current.BoundingRectangle!=bounds||!Unobscured(c.Hwnd,capture)||Native.IsIconic(c.Hwnd))return Unknown("frame-changed");
   var result=Analyze(frame,area.Size,new Point(capture.Left-area.Left,capture.Top-area.Top));
   // Raw View exposed only an anonymous image-sized group in this Edge build. Its presence
   // cannot prove Loading, but prevents Clear if changed artwork/scale defeats visual matching.
   if(result==SecurityLoadingState.Clear&&HasLoadingShapedGroup(c.Doc,bounds))return Unknown("unrecognized-centered-image");
   return new SecurityLoadingSample(result,identity);
  }
 }
 static bool HasLoadingShapedGroup(AutomationElement doc,System.Windows.Rect viewport){
  foreach(AutomationElement group in doc.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Group))){
   var value=group.Current;var r=value.BoundingRectangle;
   if(value.IsOffscreen||r.IsEmpty||r.Height<30||!String.IsNullOrEmpty(value.AutomationId)||!String.IsNullOrEmpty(value.ClassName)||!String.IsNullOrEmpty(value.Name))continue;
   double scale=r.Width/350.0;
   if(Math.Abs(r.Height-187*scale)<4&&Math.Abs(r.Left+r.Width/2-viewport.Left-viewport.Width/2)<20&&Math.Abs(r.Top-(viewport.Top+viewport.Height/2-100*scale))<10)return true;
  }
  return false;
 }
 static bool Unobscured(IntPtr hwnd,Rectangle area){
  bool reached=false,blocked=false;
  Native.EnumWindows((h,p)=>{
   if(h==hwnd){reached=true;return false;}
   int cloaked;Rect r;
   if(Native.IsWindowVisible(h)&&!Native.IsIconic(h)&&!(DwmGetCloaked(h,14,out cloaked,4)==0&&cloaked!=0)){
    // GetWindowRect includes invisible resize borders; those can cross onto the next monitor.
    if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf(typeof(Rect)))!=0&&!GetWindowRect(h,out r)){blocked=true;return true;}
    if(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom).IntersectsWith(area))blocked=true;
   }
   return true;
  },IntPtr.Zero);
  return reached&&!blocked;
 }
 internal static SecurityLoadingState Analyze(Bitmap frame){
  return Analyze(frame,frame.Size,Point.Empty);
 }
 internal static SecurityLoadingState Analyze(Bitmap frame,Size viewport,Point origin){
   // The image is centered at viewport.width/2, viewport.height/2 - 100 CSS px.
   // Compare all supported effective zoom/DPI scales. No fixed physical pixel assumption.
   bool suspicious=false;
   for(int step=10;step<=60;step++){
    double scale=step/20.0;int w=(int)Math.Round(350*scale),h=(int)Math.Round(187*scale);
    int left=(viewport.Width-w)/2-origin.X,top=(int)Math.Round(viewport.Height/2.0-100*scale)-origin.Y;
    if(left<0||top<0||left+w>frame.Width||top+h>frame.Height)continue;
    // Small viewport/scrollbar rounding offsets are tested without searching unrelated content.
    for(int dx=-10;dx<=10;dx+=5){
     var rect=new Rectangle(left+dx,top,w,h);if(rect.Left<0||rect.Right>frame.Width)continue;
     double score=Difference(frame,rect);
     if(score<13)return SecurityLoadingState.Loading;
     if(score<30)suspicious=true;
    }
   }
   return suspicious?SecurityLoadingState.Unknown:SecurityLoadingState.Clear;
 }
 // Sample the whole reference, including illustration and text bands; do not match one flat color.
 internal static double Difference(Bitmap frame,Rectangle rect){
  long error=0;int count=0;
  for(int y=4;y<187;y+=9)for(int x=4;x<350;x+=9){
   var a=template.GetPixel(x,y);var b=frame.GetPixel(rect.Left+x*rect.Width/350,rect.Top+y*rect.Height/187);
   error+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);count+=3;
  }
  return (double)error/count;
 }
 static SecurityLoadingSample Unknown(string reason){return new SecurityLoadingSample(SecurityLoadingState.Unknown,null,reason);}
}
