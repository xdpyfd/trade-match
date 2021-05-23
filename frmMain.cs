
using System;
using System.Windows.Forms;

namespace NDAXCore
{
    public partial class FrmMain : Form
    {        
        private Server _server;
        private bool _started;
               
        public FrmMain()
        {
            InitializeComponent();
        }

        private void CmdStartStop_Click(object sender, EventArgs e)
        {
            cmdStartStop.Enabled = false;
            Application.DoEvents();
            if (!_started)
            {
                _server = new Server();
                string error = _server.Start(txtAdminServerIP.Text, (int)numAdminServerPort.Value, txtExchangeServerIP.Text, (int)numExchangeServerPort.Value);
                if (string.IsNullOrEmpty(error))
                {
                    cmdStartStop.Text = "Stop";
                    _started = true;
                }
                else
                {
                    MessageBox.Show(string.Format("Failed to start server: {0}", error));
                }
            }
            else
            {
                _server.Stop();
                cmdStartStop.Text = "Start";
                _started = false;
            }
            cmdStartStop.Enabled = true;
            Application.DoEvents();
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_started)
            {
                cmdStartStop.PerformClick();
            }
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {

        }
    }
}