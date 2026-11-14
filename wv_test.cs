using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;

class Test
{
    [STAThread]
    static void Main()
    {
        try
        {
            var wv = new WebView2();
            Console.WriteLine("WebView2 instantiated successfully: " + (wv != null));
        }
        catch (Exception ex)
        {
            Console.WriteLine("WebView2 instantiate error: " + ex);
        }
    }
}
