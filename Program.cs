using System;
using System.Threading;
using System.Windows.Forms;
using System.Linq;

namespace NDAXCore
{
    static class Program
    {        
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool createNew;
            var  mutex = new Mutex(true, Application.ProductName, out createNew);
            if (createNew)
            {
                //Application.Run(new frmOrderRequest());
                Application.Run(new FrmMain());
            }
            else
            {
                MessageBox.Show("The application is already running.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }           
        }
    }
}
