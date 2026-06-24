using System.IO.Ports;

namespace tlp
{
    public partial class Configure : Form
    {
        public int MinLapMilliseconds { get; private set; }
        public bool SoundOnTooFastLap { get; private set; }
        public string SelectedPort { get; private set; } = "";
        public string SelectedSpeechVoice { get; private set; } = "";

        public Configure(int minLapMilliseconds, bool soundOnTooFastLap, string selectedPort, string selectedSpeechVoice)
        {
            InitializeComponent();
            MinLapMilliseconds = minLapMilliseconds;
            SoundOnTooFastLap = soundOnTooFastLap;
            SelectedPort = selectedPort;
            SelectedSpeechVoice = selectedSpeechVoice;
            nudMinLapMilliseconds.Value = Math.Clamp(minLapMilliseconds, (int)nudMinLapMilliseconds.Minimum, (int)nudMinLapMilliseconds.Maximum);
            cbSoundOnTooFastLap.Checked = soundOnTooFastLap;
            LoadSerialPorts(selectedPort);
            LoadSpeechVoices(selectedSpeechVoice);
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

        private void LoadSpeechVoices(string selectedSpeechVoice)
        {
            cbSpeechVoice.Items.Clear();
            cbSpeechVoice.Items.Add("");
            foreach (string voiceName in SpeechAnnouncer.GetInstalledVoices())
            {
                cbSpeechVoice.Items.Add(voiceName);
            }

            cbSpeechVoice.Text = selectedSpeechVoice;
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            MinLapMilliseconds = (int)nudMinLapMilliseconds.Value;
            SoundOnTooFastLap = cbSoundOnTooFastLap.Checked;
            SelectedPort = cbSerialPort.Text.Trim();
            SelectedSpeechVoice = cbSpeechVoice.Text.Trim();
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
