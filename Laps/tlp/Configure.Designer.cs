namespace tlp
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
            label1 = new Label();
            nudMinLapMilliseconds = new NumericUpDown();
            bOK = new Button();
            bCancel = new Button();
            groupBox4 = new GroupBox();
            cbSerialPort = new ComboBox();
            groupBox5 = new GroupBox();
            cbSpeechVoice = new ComboBox();
            groupBoxTrack = new GroupBox();
            activeLaneCountLabel = new Label();
            nudActiveLaneCount = new NumericUpDown();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).BeginInit();
            groupBox4.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBoxTrack.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudActiveLaneCount).BeginInit();
            SuspendLayout();
            //
            // groupBox3
            //
            groupBox3.Controls.Add(cbSoundOnTooFastLap);
            groupBox3.Controls.Add(label1);
            groupBox3.Controls.Add(nudMinLapMilliseconds);
            groupBox3.Location = new Point(12, 12);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(399, 85);
            groupBox3.TabIndex = 0;
            groupBox3.TabStop = false;
            groupBox3.Text = "Lap Timing";
            //
            // cbSoundOnTooFastLap
            //
            cbSoundOnTooFastLap.AutoSize = true;
            cbSoundOnTooFastLap.Location = new Point(11, 55);
            cbSoundOnTooFastLap.Name = "cbSoundOnTooFastLap";
            cbSoundOnTooFastLap.Size = new Size(231, 19);
            cbSoundOnTooFastLap.TabIndex = 2;
            cbSoundOnTooFastLap.Text = "Play sound when a lap is ignored";
            cbSoundOnTooFastLap.UseVisualStyleBackColor = true;
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
            groupBox4.Location = new Point(12, 110);
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
            groupBox5.Controls.Add(cbSpeechVoice);
            groupBox5.Location = new Point(12, 176);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(399, 53);
            groupBox5.TabIndex = 2;
            groupBox5.TabStop = false;
            groupBox5.Text = "Voice";
            //
            // cbSpeechVoice
            //
            cbSpeechVoice.DropDownStyle = ComboBoxStyle.DropDownList;
            cbSpeechVoice.FormattingEnabled = true;
            cbSpeechVoice.Location = new Point(11, 21);
            cbSpeechVoice.Name = "cbSpeechVoice";
            cbSpeechVoice.Size = new Size(370, 23);
            cbSpeechVoice.TabIndex = 0;
            //
            // groupBoxTrack
            //
            groupBoxTrack.Controls.Add(activeLaneCountLabel);
            groupBoxTrack.Controls.Add(nudActiveLaneCount);
            groupBoxTrack.Location = new Point(12, 242);
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
            // bOK
            //
            bOK.Location = new Point(255, 313);
            bOK.Name = "bOK";
            bOK.Size = new Size(75, 23);
            bOK.TabIndex = 4;
            bOK.Text = "OK";
            bOK.UseVisualStyleBackColor = true;
            bOK.Click += bOK_Click;
            //
            // bCancel
            //
            bCancel.Location = new Point(336, 313);
            bCancel.Name = "bCancel";
            bCancel.Size = new Size(75, 23);
            bCancel.TabIndex = 5;
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
            ClientSize = new Size(428, 352);
            Controls.Add(bCancel);
            Controls.Add(bOK);
            Controls.Add(groupBox5);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBoxTrack);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Configure";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Configure";
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).EndInit();
            groupBox4.ResumeLayout(false);
            groupBox5.ResumeLayout(false);
            groupBoxTrack.ResumeLayout(false);
            groupBoxTrack.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudActiveLaneCount).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox3;
        private CheckBox cbSoundOnTooFastLap;
        private Label label1;
        private NumericUpDown nudMinLapMilliseconds;
        private Button bOK;
        private Button bCancel;
        private GroupBox groupBox4;
        private ComboBox cbSerialPort;
        private GroupBox groupBox5;
        private ComboBox cbSpeechVoice;
        private GroupBox groupBoxTrack;
        private Label activeLaneCountLabel;
        private NumericUpDown nudActiveLaneCount;
    }
}
