using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace tlp
{
    public partial class AddUser : Form
    {
        public string name = "";
        public AddUser()
        {
            InitializeComponent();
            AcceptButton = bOK;
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            name = tbName.Text;
            this.Close();
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
