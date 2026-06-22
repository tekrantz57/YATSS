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
            cbNumLanes = new ComboBox();
            groupBox1 = new GroupBox();
            groupBox2 = new GroupBox();
            comboBox1 = new ComboBox();
            button1 = new Button();
            groupBox3 = new GroupBox();
            cbSoundOnTooFastLap = new CheckBox();
            label1 = new Label();
            nudMinLapMilliseconds = new NumericUpDown();
            bOK = new Button();
            bCancel = new Button();
            groupBox4 = new GroupBox();
            cbSerialPort = new ComboBox();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).BeginInit();
            groupBox4.SuspendLayout();
            SuspendLayout();
            //
            // cbNumLanes
            //
            cbNumLanes.DropDownStyle = ComboBoxStyle.DropDownList;
            cbNumLanes.FormattingEnabled = true;
            cbNumLanes.Items.AddRange(new object[] { "1", "2", "3", "4", "5", "6", "7", "8" });
            cbNumLanes.Location = new Point(8, 21);
            cbNumLanes.Name = "cbNumLanes";
            cbNumLanes.Size = new Size(121, 23);
            cbNumLanes.TabIndex = 0;
            //
            // groupBox1
            //
            groupBox1.Controls.Add(cbNumLanes);
            groupBox1.Location = new Point(12, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(158, 53);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Number of Lanes";
            //
            // groupBox2
            //
            groupBox2.Controls.Add(button1);
            groupBox2.Controls.Add(comboBox1);
            groupBox2.Location = new Point(206, 12);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(205, 53);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "Configure Lane";
            //
            // comboBox1
            //
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.FormattingEnabled = true;
            comboBox1.Items.AddRange(new object[] { "1", "2", "3", "4", "5", "6", "7", "8" });
            comboBox1.Location = new Point(6, 22);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(121, 23);
            comboBox1.TabIndex = 1;
            //
            // button1
            //
            button1.Location = new Point(133, 22);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 2;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            //
            // groupBox3
            //
            groupBox3.Controls.Add(cbSoundOnTooFastLap);
            groupBox3.Controls.Add(label1);
            groupBox3.Controls.Add(nudMinLapMilliseconds);
            groupBox3.Location = new Point(12, 83);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(399, 85);
            groupBox3.TabIndex = 3;
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
            groupBox4.Location = new Point(12, 181);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(399, 53);
            groupBox4.TabIndex = 4;
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
            // bOK
            //
            bOK.Location = new Point(255, 252);
            bOK.Name = "bOK";
            bOK.Size = new Size(75, 23);
            bOK.TabIndex = 5;
            bOK.Text = "OK";
            bOK.UseVisualStyleBackColor = true;
            bOK.Click += bOK_Click;
            //
            // bCancel
            //
            bCancel.Location = new Point(336, 252);
            bCancel.Name = "bCancel";
            bCancel.Size = new Size(75, 23);
            bCancel.TabIndex = 6;
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
            ClientSize = new Size(428, 291);
            Controls.Add(bCancel);
            Controls.Add(bOK);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Configure";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Configure";
            groupBox1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudMinLapMilliseconds).EndInit();
            groupBox4.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        public ComboBox cbNumLanes;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        public ComboBox comboBox1;
        private Button button1;
        private GroupBox groupBox3;
        private CheckBox cbSoundOnTooFastLap;
        private Label label1;
        private NumericUpDown nudMinLapMilliseconds;
        private Button bOK;
        private Button bCancel;
        private GroupBox groupBox4;
        private ComboBox cbSerialPort;
    }
}
