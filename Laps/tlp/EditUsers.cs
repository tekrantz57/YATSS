using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace tlp
{
    public partial class EditUsers : Form
    {
        public EditUsers()
        {
            InitializeComponent();
        }

        private void EditUsers_Load(object sender, EventArgs e)
        {
            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        string? name = reader[0].ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            cbUsers.Items.Add(name.Trim());
                        }
                    }

                    if (cbUsers.Items.Count > 0)
                    {
                        cbUsers.SelectedIndex = 0;
                    }
                }
            }
        }

        private void bOK_Click(object sender, EventArgs e)
        {
            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"delete from users";
            command.ExecuteNonQuery();

            command = MKTS.conn.CreateCommand();
            command.CommandText = @"delete from sqlite_sequence where name = 'users'";
            command.ExecuteNonQuery();

            foreach (var item in cbUsers.Items)
            {
                command = MKTS.conn.CreateCommand();
                command.CommandText = @"INSERT INTO users (name) values ($name)";
                command.Parameters.AddWithValue("$name", item?.ToString() ?? string.Empty);
                command.ExecuteNonQuery();
            }

            this.Close();
        }

        private void bCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void bAddUser_Click(object sender, EventArgs e)
        {
            using (var addUser = new AddUser())
            {
                addUser.ShowDialog();
                if (addUser.DialogResult == DialogResult.OK)
                {
                    if (!cbUsers.Items.Contains(addUser.name))
                    {
                        cbUsers.SelectedItem = cbUsers.Items.Add(addUser.name.Trim());
                    }
                }
            }

        }

        private void cbUsers_SelectedIndexChanged(object sender, EventArgs e)
        {

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
    }
}
