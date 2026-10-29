using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class AppVisuals {
 [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
 internal static Icon CreateIcon(){
  using(var bitmap=new Bitmap(32,32)){
   using(var g=Graphics.FromImage(bitmap))using(var blue=new SolidBrush(Color.FromArgb(31,91,163)))using(var white=new SolidBrush(Color.White))using(var cyan=new Pen(Color.FromArgb(83,207,218),2)){
    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.FillEllipse(blue,1,1,30,30);
    g.FillRectangle(white,8,8,16,17);g.DrawLine(cyan,16,10,16,23);g.DrawLine(cyan,10,12,14,12);g.DrawLine(cyan,18,12,22,12);g.DrawLine(cyan,10,16,14,16);g.DrawLine(cyan,18,16,22,16);
   }
   IntPtr handle=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
  }
 }
 internal static void OpenMenuOnClick(NotifyIcon icon,ContextMenuStrip menu){icon.MouseClick+=(o,e)=>{if(e.Button==MouseButtons.Left)menu.Show(Cursor.Position);};}
}
