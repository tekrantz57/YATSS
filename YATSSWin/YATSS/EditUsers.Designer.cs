namespace YATSS
{
    partial class EditUsers
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
            this.cbUsers = new System.Windows.Forms.ComboBox();
            this.bAddUser = new System.Windows.Forms.Button();
            this.bOK = new System.Windows.Forms.Button();
            this.bCancel = new System.Windows.Forms.Button();
            this.bDeleteUser = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // cbUsers
            // 
            this.cbUsers.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbUsers.FormattingEnabled = true;
            this.cbUsers.Location = new System.Drawing.Point(12, 43);
            this.cbUsers.Name = "cbUsers";
            this.cbUsers.Size = new System.Drawing.Size(276, 23);
            this.cbUsers.Sorted = true;
            this.cbUsers.TabIndex = 0;
            // 
            // bAddUser
            // 
            this.bAddUser.Location = new System.Drawing.Point(323, 26);
            this.bAddUser.Name = "bAddUser";
            this.bAddUser.Size = new System.Drawing.Size(75, 23);
            this.bAddUser.TabIndex = 1;
            this.bAddUser.Text = "Add User";
            this.bAddUser.UseVisualStyleBackColor = true;
            this.bAddUser.Click += new System.EventHandler(this.bAddUser_Click);
            // 
            // bOK
            // 
            this.bOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.bOK.Location = new System.Drawing.Point(33, 141);
            this.bOK.Name = "bOK";
            this.bOK.Size = new System.Drawing.Size(75, 23);
            this.bOK.TabIndex = 2;
            this.bOK.Text = "OK";
            this.bOK.UseVisualStyleBackColor = true;
            this.bOK.Click += new System.EventHandler(this.bOK_Click);
            // 
            // bCancel
            // 
            this.bCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.bCancel.Location = new System.Drawing.Point(159, 141);
            this.bCancel.Name = "bCancel";
            this.bCancel.Size = new System.Drawing.Size(75, 23);
            this.bCancel.TabIndex = 3;
            this.bCancel.Text = "Cancel";
            this.bCancel.UseVisualStyleBackColor = true;
            this.bCancel.Click += new System.EventHandler(this.bCancel_Click);
            // 
            // bDeleteUser
            // 
            this.bDeleteUser.Location = new System.Drawing.Point(323, 55);
            this.bDeleteUser.Name = "bDeleteUser";
            this.bDeleteUser.Size = new System.Drawing.Size(75, 23);
            this.bDeleteUser.TabIndex = 4;
            this.bDeleteUser.Text = "Delete User";
            this.bDeleteUser.UseVisualStyleBackColor = true;
            this.bDeleteUser.Click += new System.EventHandler(this.bDeleteUser_Click);
            // 
            // EditUsers
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(410, 199);
            this.Controls.Add(this.bDeleteUser);
            this.Controls.Add(this.bCancel);
            this.Controls.Add(this.bOK);
            this.Controls.Add(this.bAddUser);
            this.Controls.Add(this.cbUsers);
            this.Name = "EditUsers";
            this.Text = "EditUsers";
            this.Load += new System.EventHandler(this.EditUsers_Load);
            this.ResumeLayout(false);

        }

        #endregion

        public ComboBox cbUsers;
        private Button bAddUser;
        private Button bOK;
        private Button bCancel;
        private Button bDeleteUser;
    }
}
