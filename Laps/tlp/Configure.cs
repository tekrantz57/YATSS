using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace tlp
{
    public partial class Configure : Form
    {
        public int MinLapMilliseconds { get; private set; }
        public bool SoundOnTooFastLap { get; private set; }
        public string SelectedPort { get; private set; } = "";

        public Configure(int minLapMilliseconds, bool soundOnTooFastLap, string selectedPort)
        {
            InitializeComponent();
            MinLapMilliseconds = minLapMilliseconds;
            SoundOnTooFastLap = soundOnTooFastLap;
            SelectedPort = selectedPort;
            nudMinLapMilliseconds.Value = Math.Clamp(minLapMilliseconds, (int)nudMinLapMilliseconds.Minimum, (int)nudMinLapMilliseconds.Maximum);
            cbSoundOnTooFastLap.Checked = soundOnTooFastLap;
            LoadSerialPorts(selectedPort);
        }

        private void LoadSerialPorts(string selectedPort)
        {
            cbSerialPort.Items.Clear();
            foreach (string portName in SerialPort.GetPortNames().OrderBy(p => p))
            {
                cbSerialPort.Items.Add(portName);
            }

            if (!string.IsNullOrWhiteSpace(selectedPort) && !cbSerialPort.Items.Contains(selectedPort))
            {
                cbSerialPort.Items.Add(selectedPort);
            }

            cbSerialPort.Text = selectedPort;
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            FontDialog fontDialog1 = new FontDialog();
            fontDialog1.ShowColor = true;

            //fontDialog1.Font = textBox1.Font;
            //fontDialog1.Color = textBox1.ForeColor;

            if (fontDialog1.ShowDialog() != DialogResult.Cancel)
            {
                //textBox1.Font = fontDialog1.Font;
                //textBox1.ForeColor = fontDialog1.Color;
            }
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            MinLapMilliseconds = (int)nudMinLapMilliseconds.Value;
            SoundOnTooFastLap = cbSoundOnTooFastLap.Checked;
            SelectedPort = cbSerialPort.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
