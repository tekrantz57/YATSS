namespace YATSS
{
    partial class Configure
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
            groupBox3 = new GroupBox();
            cbSoundOnTooFastLap = new CheckBox();
            sensorDebounceLabel = new Label();
            nudSensorDebounceMilliseconds = new NumericUpDown();
            rawSensorLockoutLabel = new Label();
            nudRawSensorLockoutMilliseconds = new NumericUpDown();
            label1 = new Label();
            nudMinLapMilliseconds = new NumericUpDown();
            bOK = new Button();
            bCancel = new Button();
            groupBox4 = new GroupBox();
            cbSerialPort = new ComboBox();
            groupBox5 = new GroupBox();
            cbVoiceAnnouncements = new CheckBox();
            speechBackendLabel = new Label();
            cbSpeechBackend = new ComboBox();
            speechVoiceLabel = new Label();
            cbSpeechVoice = new ComboBox();
            groupBoxTrack = new GroupBox();
            activeLaneCountLabel = new Label();
            nudActiveLaneCount = new NumericUpDown();
            trackLengthLabel = new Label();
            nudTrackLengthFeet = new NumericUpDown();
            groupBoxRaceReports = new GroupBox();
            cbExportRaceJson = new CheckBox();
            cbExportRaceCsv = new CheckBox();
            groupBoxLaneColors = new GroupBox();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudSensorDebounceMilliseconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudRawSensorLockoutMilliseconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).BeginInit();
            groupBox4.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBoxTrack.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudActiveLaneCount).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudTrackLengthFeet).BeginInit();
            groupBoxRaceReports.SuspendLayout();
            groupBoxLaneColors.SuspendLayout();
            SuspendLayout();
            //
            // groupBox3
            //
            groupBox3.Controls.Add(cbSoundOnTooFastLap);
            groupBox3.Controls.Add(sensorDebounceLabel);
            groupBox3.Controls.Add(nudSensorDebounceMilliseconds);
            groupBox3.Controls.Add(rawSensorLockoutLabel);
            groupBox3.Controls.Add(nudRawSensorLockoutMilliseconds);
            groupBox3.Controls.Add(label1);
            groupBox3.Controls.Add(nudMinLapMilliseconds);
            groupBox3.Location = new Point(12, 12);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(399, 139);
            groupBox3.TabIndex = 0;
            groupBox3.TabStop = false;
            groupBox3.Text = "Lap Timing";
            //
            // cbSoundOnTooFastLap
            //
            cbSoundOnTooFastLap.AutoSize = true;
            cbSoundOnTooFastLap.Location = new Point(11, 111);
            cbSoundOnTooFastLap.Name = "cbSoundOnTooFastLap";
            cbSoundOnTooFastLap.Size = new Size(231, 19);
            cbSoundOnTooFastLap.TabIndex = 2;
            cbSoundOnTooFastLap.Text = "Play sound when a lap is ignored";
            cbSoundOnTooFastLap.UseVisualStyleBackColor = true;
            //
            // sensorDebounceLabel
            //
            sensorDebounceLabel.AutoSize = true;
            sensorDebounceLabel.Location = new Point(137, 55);
            sensorDebounceLabel.Name = "sensorDebounceLabel";
            sensorDebounceLabel.Size = new Size(209, 15);
            sensorDebounceLabel.TabIndex = 4;
            sensorDebounceLabel.Text = "Controller sensor debounce (ms)";
            //
            // nudSensorDebounceMilliseconds
            //
            nudSensorDebounceMilliseconds.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            nudSensorDebounceMilliseconds.Location = new Point(11, 51);
            nudSensorDebounceMilliseconds.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudSensorDebounceMilliseconds.Name = "nudSensorDebounceMilliseconds";
            nudSensorDebounceMilliseconds.Size = new Size(120, 23);
            nudSensorDebounceMilliseconds.TabIndex = 3;
            nudSensorDebounceMilliseconds.Value = new decimal(new int[] { 1800, 0, 0, 0 });
            //
            // rawSensorLockoutLabel
            //
            rawSensorLockoutLabel.AutoSize = true;
            rawSensorLockoutLabel.Location = new Point(137, 84);
            rawSensorLockoutLabel.Name = "rawSensorLockoutLabel";
            rawSensorLockoutLabel.Size = new Size(187, 15);
            rawSensorLockoutLabel.TabIndex = 6;
            rawSensorLockoutLabel.Text = "Windows raw edge lockout (ms)";
            //
            // nudRawSensorLockoutMilliseconds
            //
            nudRawSensorLockoutMilliseconds.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            nudRawSensorLockoutMilliseconds.Location = new Point(11, 80);
            nudRawSensorLockoutMilliseconds.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudRawSensorLockoutMilliseconds.Name = "nudRawSensorLockoutMilliseconds";
            nudRawSensorLockoutMilliseconds.Size = new Size(120, 23);
            nudRawSensorLockoutMilliseconds.TabIndex = 5;
            //
            // label1
            //
            label1.AutoSize = true;
            label1.Location = new Point(137, 26);
            label1.Name = "label1";
            label1.Size = new Size(166, 15);
            label1.TabIndex = 1;
            label1.Text = "Minimum lap time (ms)";
            //
            // nudMinLapMilliseconds
            //
            nudMinLapMilliseconds.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            nudMinLapMilliseconds.Location = new Point(11, 22);
            nudMinLapMilliseconds.Maximum = new decimal(new int[] { 60000, 0, 0, 0 });
            nudMinLapMilliseconds.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            nudMinLapMilliseconds.Name = "nudMinLapMilliseconds";
            nudMinLapMilliseconds.Size = new Size(120, 23);
            nudMinLapMilliseconds.TabIndex = 0;
            nudMinLapMilliseconds.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            //
            // groupBox4
            //
            groupBox4.Controls.Add(cbSerialPort);
            groupBox4.Location = new Point(12, 164);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(399, 53);
            groupBox4.TabIndex = 1;
            groupBox4.TabStop = false;
            groupBox4.Text = "Serial Port";
            //
            // cbSerialPort
            //
            cbSerialPort.FormattingEnabled = true;
            cbSerialPort.Location = new Point(11, 21);
            cbSerialPort.Name = "cbSerialPort";
            cbSerialPort.Size = new Size(166, 23);
            cbSerialPort.TabIndex = 0;
            //
            // groupBox5
            //
            groupBox5.Controls.Add(cbVoiceAnnouncements);
            groupBox5.Controls.Add(speechBackendLabel);
            groupBox5.Controls.Add(cbSpeechBackend);
            groupBox5.Controls.Add(speechVoiceLabel);
            groupBox5.Controls.Add(cbSpeechVoice);
            groupBox5.Location = new Point(12, 230);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(399, 105);
            groupBox5.TabIndex = 2;
            groupBox5.TabStop = false;
            groupBox5.Text = "Voice";
            //
            // cbVoiceAnnouncements
            //
            cbVoiceAnnouncements.AutoSize = true;
            cbVoiceAnnouncements.Location = new Point(11, 22);
            cbVoiceAnnouncements.Name = "cbVoiceAnnouncements";
            cbVoiceAnnouncements.Size = new Size(178, 19);
            cbVoiceAnnouncements.TabIndex = 0;
            cbVoiceAnnouncements.Text = "Enable voice announcements";
            cbVoiceAnnouncements.UseVisualStyleBackColor = true;
            //
            // speechBackendLabel
            //
            speechBackendLabel.AutoSize = true;
            speechBackendLabel.Location = new Point(205, 23);
            speechBackendLabel.Name = "speechBackendLabel";
            speechBackendLabel.Size = new Size(44, 15);
            speechBackendLabel.TabIndex = 1;
            speechBackendLabel.Text = "Engine";
            //
            // cbSpeechBackend
            //
            cbSpeechBackend.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSpeechBackend.FormattingEnabled = true;
            cbSpeechBackend.Location = new Point(255, 19);
            cbSpeechBackend.Name = "cbSpeechBackend";
            cbSpeechBackend.Size = new Size(126, 23);
            cbSpeechBackend.TabIndex = 2;
            //
            // speechVoiceLabel
            //
            speechVoiceLabel.AutoSize = true;
            speechVoiceLabel.Location = new Point(11, 57);
            speechVoiceLabel.Name = "speechVoiceLabel";
            speechVoiceLabel.Size = new Size(35, 15);
            speechVoiceLabel.TabIndex = 3;
            speechVoiceLabel.Text = "Voice";
            //
            // cbSpeechVoice
            //
            cbSpeechVoice.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSpeechVoice.FormattingEnabled = true;
            cbSpeechVoice.Location = new Point(52, 53);
            cbSpeechVoice.Name = "cbSpeechVoice";
            cbSpeechVoice.Size = new Size(329, 23);
            cbSpeechVoice.TabIndex = 4;
            //
            // groupBoxTrack
            //
            groupBoxTrack.Controls.Add(activeLaneCountLabel);
            groupBoxTrack.Controls.Add(nudActiveLaneCount);
            groupBoxTrack.Controls.Add(trackLengthLabel);
            groupBoxTrack.Controls.Add(nudTrackLengthFeet);
            groupBoxTrack.Location = new Point(12, 348);
            groupBoxTrack.Name = "groupBoxTrack";
            groupBoxTrack.Size = new Size(399, 53);
            groupBoxTrack.TabIndex = 3;
            groupBoxTrack.TabStop = false;
            groupBoxTrack.Text = "Track";
            //
            // activeLaneCountLabel
            //
            activeLaneCountLabel.AutoSize = true;
            activeLaneCountLabel.Location = new Point(81, 25);
            activeLaneCountLabel.Name = "activeLaneCountLabel";
            activeLaneCountLabel.Size = new Size(138, 15);
            activeLaneCountLabel.TabIndex = 1;
            activeLaneCountLabel.Text = "Number of lanes (2-8)";
            //
            // nudActiveLaneCount
            //
            nudActiveLaneCount.Location = new Point(11, 21);
            nudActiveLaneCount.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            nudActiveLaneCount.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            nudActiveLaneCount.Name = "nudActiveLaneCount";
            nudActiveLaneCount.Size = new Size(64, 23);
            nudActiveLaneCount.TabIndex = 0;
            nudActiveLaneCount.Value = new decimal(new int[] { 8, 0, 0, 0 });
            //
            // trackLengthLabel
            //
            trackLengthLabel.AutoSize = true;
            trackLengthLabel.Location = new Point(225, 25);
            trackLengthLabel.Name = "trackLengthLabel";
            trackLengthLabel.Size = new Size(97, 15);
            trackLengthLabel.TabIndex = 2;
            trackLengthLabel.Text = "Track length (ft)";
            //
            // nudTrackLengthFeet
            //
            nudTrackLengthFeet.DecimalPlaces = 2;
            nudTrackLengthFeet.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            nudTrackLengthFeet.Location = new Point(328, 21);
            nudTrackLengthFeet.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudTrackLengthFeet.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudTrackLengthFeet.Name = "nudTrackLengthFeet";
            nudTrackLengthFeet.Size = new Size(59, 23);
            nudTrackLengthFeet.TabIndex = 3;
            nudTrackLengthFeet.Value = new decimal(new int[] { 155, 0, 0, 0 });
            //
            // groupBoxRaceReports
            //
            groupBoxRaceReports.Controls.Add(cbExportRaceJson);
            groupBoxRaceReports.Controls.Add(cbExportRaceCsv);
            groupBoxRaceReports.Location = new Point(12, 414);
            groupBoxRaceReports.Name = "groupBoxRaceReports";
            groupBoxRaceReports.Size = new Size(399, 53);
            groupBoxRaceReports.TabIndex = 4;
            groupBoxRaceReports.TabStop = false;
            groupBoxRaceReports.Text = "Race Reports";
            //
            // cbExportRaceJson
            //
            cbExportRaceJson.AutoSize = true;
            cbExportRaceJson.Location = new Point(11, 23);
            cbExportRaceJson.Name = "cbExportRaceJson";
            cbExportRaceJson.Size = new Size(153, 19);
            cbExportRaceJson.TabIndex = 0;
            cbExportRaceJson.Text = "Write JSON race archive";
            cbExportRaceJson.UseVisualStyleBackColor = true;
            //
            // cbExportRaceCsv
            //
            cbExportRaceCsv.AutoSize = true;
            cbExportRaceCsv.Location = new Point(205, 23);
            cbExportRaceCsv.Name = "cbExportRaceCsv";
            cbExportRaceCsv.Size = new Size(137, 19);
            cbExportRaceCsv.TabIndex = 1;
            cbExportRaceCsv.Text = "Write CSV data files";
            cbExportRaceCsv.UseVisualStyleBackColor = true;
            //
            // groupBoxLaneColors
            //
            groupBoxLaneColors.Location = new Point(12, 480);
            groupBoxLaneColors.Name = "groupBoxLaneColors";
            groupBoxLaneColors.Size = new Size(399, 174);
            groupBoxLaneColors.TabIndex = 5;
            groupBoxLaneColors.TabStop = false;
            groupBoxLaneColors.Text = "Lane Names and Colors";
            //
            // bOK
            //
            bOK.Location = new Point(255, 672);
            bOK.Name = "bOK";
            bOK.Size = new Size(75, 23);
            bOK.TabIndex = 6;
            bOK.Text = "OK";
            bOK.UseVisualStyleBackColor = true;
            bOK.Click += bOK_Click;
            //
            // bCancel
            //
            bCancel.Location = new Point(336, 672);
            bCancel.Name = "bCancel";
            bCancel.Size = new Size(75, 23);
            bCancel.TabIndex = 7;
            bCancel.Text = "Cancel";
            bCancel.UseVisualStyleBackColor = true;
            bCancel.Click += bCancel_Click;
            //
            // Configure
            //
            AcceptButton = bOK;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = bCancel;
            ClientSize = new Size(428, 711);
            Controls.Add(bCancel);
            Controls.Add(bOK);
            Controls.Add(groupBox5);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBoxTrack);
            Controls.Add(groupBoxRaceReports);
            Controls.Add(groupBoxLaneColors);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Configure";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Configure";
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudSensorDebounceMilliseconds).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudRawSensorLockoutMilliseconds).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).EndInit();
            groupBox4.ResumeLayout(false);
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBoxTrack.ResumeLayout(false);
            groupBoxTrack.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudActiveLaneCount).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudTrackLengthFeet).EndInit();
            groupBoxRaceReports.ResumeLayout(false);
            groupBoxRaceReports.PerformLayout();
            groupBoxLaneColors.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox3;
        private CheckBox cbSoundOnTooFastLap;
        private Label sensorDebounceLabel;
        private NumericUpDown nudSensorDebounceMilliseconds;
        private Label rawSensorLockoutLabel;
        private NumericUpDown nudRawSensorLockoutMilliseconds;
        private Label label1;
        private NumericUpDown nudMinLapMilliseconds;
        private Button bOK;
        private Button bCancel;
        private GroupBox groupBox4;
        private ComboBox cbSerialPort;
        private GroupBox groupBox5;
        private CheckBox cbVoiceAnnouncements;
        private Label speechBackendLabel;
        private ComboBox cbSpeechBackend;
        private Label speechVoiceLabel;
        private ComboBox cbSpeechVoice;
        private GroupBox groupBoxTrack;
        private Label activeLaneCountLabel;
        private NumericUpDown nudActiveLaneCount;
        private Label trackLengthLabel;
        private NumericUpDown nudTrackLengthFeet;
        private GroupBox groupBoxRaceReports;
        private CheckBox cbExportRaceJson;
        private CheckBox cbExportRaceCsv;
        private GroupBox groupBoxLaneColors;
    }
}
