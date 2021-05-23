namespace NDAXCore
{
    partial class FrmMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.cmdStartStop = new System.Windows.Forms.Button();
            this.lblAdminServerIP = new System.Windows.Forms.Label();
            this.txtAdminServerIP = new System.Windows.Forms.TextBox();
            this.lblAdminServerPort = new System.Windows.Forms.Label();
            this.lblExchangeServerIP = new System.Windows.Forms.Label();
            this.txtExchangeServerIP = new System.Windows.Forms.TextBox();
            this.lblExchangeServerPort = new System.Windows.Forms.Label();
            this.numAdminServerPort = new System.Windows.Forms.NumericUpDown();
            this.numExchangeServerPort = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.numAdminServerPort)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numExchangeServerPort)).BeginInit();
            this.SuspendLayout();
            // 
            // cmdStartStop
            // 
            this.cmdStartStop.Location = new System.Drawing.Point(154, 114);
            this.cmdStartStop.Name = "cmdStartStop";
            this.cmdStartStop.Size = new System.Drawing.Size(75, 23);
            this.cmdStartStop.TabIndex = 4;
            this.cmdStartStop.Text = "Start";
            this.cmdStartStop.UseVisualStyleBackColor = true;
            this.cmdStartStop.Click += new System.EventHandler(this.CmdStartStop_Click);
            // 
            // lblAdminServerIP
            // 
            this.lblAdminServerIP.AutoSize = true;
            this.lblAdminServerIP.Location = new System.Drawing.Point(15, 13);
            this.lblAdminServerIP.Name = "lblAdminServerIP";
            this.lblAdminServerIP.Size = new System.Drawing.Size(84, 13);
            this.lblAdminServerIP.TabIndex = 1;
            this.lblAdminServerIP.Text = "Admin server IP:";
            // 
            // txtAdminServerIP
            // 
            this.txtAdminServerIP.Location = new System.Drawing.Point(131, 10);
            this.txtAdminServerIP.Name = "txtAdminServerIP";
            this.txtAdminServerIP.Size = new System.Drawing.Size(237, 20);
            this.txtAdminServerIP.TabIndex = 0;
            this.txtAdminServerIP.Text = "127.0.0.1";
            // 
            // lblAdminServerPort
            // 
            this.lblAdminServerPort.AutoSize = true;
            this.lblAdminServerPort.Location = new System.Drawing.Point(15, 39);
            this.lblAdminServerPort.Name = "lblAdminServerPort";
            this.lblAdminServerPort.Size = new System.Drawing.Size(92, 13);
            this.lblAdminServerPort.TabIndex = 1;
            this.lblAdminServerPort.Text = "Admin server port:";
            // 
            // lblExchangeServerIP
            // 
            this.lblExchangeServerIP.AutoSize = true;
            this.lblExchangeServerIP.Location = new System.Drawing.Point(15, 65);
            this.lblExchangeServerIP.Name = "lblExchangeServerIP";
            this.lblExchangeServerIP.Size = new System.Drawing.Size(103, 13);
            this.lblExchangeServerIP.TabIndex = 1;
            this.lblExchangeServerIP.Text = "Exchange server IP:";
            // 
            // txtExchangeServerIP
            // 
            this.txtExchangeServerIP.Location = new System.Drawing.Point(131, 62);
            this.txtExchangeServerIP.Name = "txtExchangeServerIP";
            this.txtExchangeServerIP.Size = new System.Drawing.Size(237, 20);
            this.txtExchangeServerIP.TabIndex = 2;
            this.txtExchangeServerIP.Text = "127.0.0.1";
            // 
            // lblExchangeServerPort
            // 
            this.lblExchangeServerPort.AutoSize = true;
            this.lblExchangeServerPort.Location = new System.Drawing.Point(15, 91);
            this.lblExchangeServerPort.Name = "lblExchangeServerPort";
            this.lblExchangeServerPort.Size = new System.Drawing.Size(111, 13);
            this.lblExchangeServerPort.TabIndex = 1;
            this.lblExchangeServerPort.Text = "Exchange server port:";
            // 
            // numAdminServerPort
            // 
            this.numAdminServerPort.Location = new System.Drawing.Point(131, 36);
            this.numAdminServerPort.Maximum = new decimal(new int[] {
            -1530494976,
            232830,
            0,
            0});
            this.numAdminServerPort.Name = "numAdminServerPort";
            this.numAdminServerPort.Size = new System.Drawing.Size(237, 20);
            this.numAdminServerPort.TabIndex = 1;
            this.numAdminServerPort.Value = new decimal(new int[] {
            2013,
            0,
            0,
            0});
            // 
            // numExchangeServerPort
            // 
            this.numExchangeServerPort.Location = new System.Drawing.Point(131, 89);
            this.numExchangeServerPort.Maximum = new decimal(new int[] {
            -1530494976,
            232830,
            0,
            0});
            this.numExchangeServerPort.Name = "numExchangeServerPort";
            this.numExchangeServerPort.Size = new System.Drawing.Size(237, 20);
            this.numExchangeServerPort.TabIndex = 3;
            this.numExchangeServerPort.Value = new decimal(new int[] {
            2012,
            0,
            0,
            0});
            // 
            // FrmMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(370, 162);
            this.Controls.Add(this.numExchangeServerPort);
            this.Controls.Add(this.numAdminServerPort);
            this.Controls.Add(this.lblExchangeServerPort);
            this.Controls.Add(this.lblAdminServerPort);
            this.Controls.Add(this.txtExchangeServerIP);
            this.Controls.Add(this.lblExchangeServerIP);
            this.Controls.Add(this.txtAdminServerIP);
            this.Controls.Add(this.lblAdminServerIP);
            this.Controls.Add(this.cmdStartStop);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FrmMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "NDAXCore";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrmMain_FormClosing);
            this.Load += new System.EventHandler(this.FrmMain_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numAdminServerPort)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numExchangeServerPort)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button cmdStartStop;
        private System.Windows.Forms.Label lblAdminServerIP;
        private System.Windows.Forms.TextBox txtAdminServerIP;
        private System.Windows.Forms.Label lblAdminServerPort;
        private System.Windows.Forms.Label lblExchangeServerIP;
        private System.Windows.Forms.TextBox txtExchangeServerIP;
        private System.Windows.Forms.Label lblExchangeServerPort;
        private System.Windows.Forms.NumericUpDown numAdminServerPort;
        private System.Windows.Forms.NumericUpDown numExchangeServerPort;
    }
}