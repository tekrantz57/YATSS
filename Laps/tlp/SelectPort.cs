using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace tlp
{
    public partial class SelectPort : Form
    {
        public string port = "";
        public SelectPort(string port)
        {
            InitializeComponent();
            foreach (string s in SerialPort.GetPortNames())
            {
                Trace.WriteLine(s);
                listBox1.Items.Add(s);
                listBox1.SelectedItem = port;
            }
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            port = listBox1.SelectedItem == null ? string.Empty : listBox1.GetItemText(listBox1.SelectedItem) ?? string.Empty;
            this.Close();
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
