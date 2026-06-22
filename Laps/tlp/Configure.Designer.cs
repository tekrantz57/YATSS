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
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
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
            // Configure
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Name = "Configure";
            Text = "Configure";
            groupBox1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        public ComboBox cbNumLanes;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        public ComboBox comboBox1;
        private Button button1;
    }
}