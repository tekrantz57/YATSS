namespace YATSS
{
    public partial class EditUsers : Form
    {
        public EditUsers()
        {
            InitializeComponent();
        }

        private void EditUsers_Load(object sender, EventArgs e)
        {
            cbUsers.Items.Clear();
            foreach (string name in AppDatabase.LoadRacerNames())
            {
                cbUsers.Items.Add(name);
            }

            if (cbUsers.Items.Count > 0)
            {
                cbUsers.SelectedIndex = 0;
            }
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            AppDatabase.SaveRacerNames(cbUsers.Items.Cast<object>().Select(item => item?.ToString() ?? string.Empty));
            DialogResult = DialogResult.OK;
            Close();
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void bAddUser_Click(object sender, EventArgs e)
        {
            using (var addUser = new AddUser())
            {
                addUser.ShowDialog(this);
                if (addUser.DialogResult == DialogResult.OK)
                {
                    string name = addUser.name.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !ContainsUser(name))
                    {
                        cbUsers.SelectedItem = cbUsers.Items.Add(name);
                    }
                }
            }

        }

        private void bDeleteUser_Click(object sender, EventArgs e)
        {
            if (cbUsers.SelectedItem == null)
            {
                return;
            }

            cbUsers.Items.Remove(cbUsers.SelectedItem);
            if (cbUsers.Items.Count > 0)
            {
                cbUsers.SelectedIndex = 0;
            }
        }

        private bool ContainsUser(string name)
        {
            foreach (object? item in cbUsers.Items)
            {
                if (string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
