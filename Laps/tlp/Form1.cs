using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Ports;
using System.Xml.Serialization;
using Microsoft.Data.Sqlite;

namespace tlp
{
    public partial class MKTS : Form
    {
        static Serial s;
        public string port = "";
        public int MinLapMilliseconds { get; private set; } = LapRaceOptions.Default.MinLapMilliseconds;
        public bool SoundOnTooFastLap { get; private set; } = true;
        public static SqliteConnection conn = new SqliteConnection(@"Data Source=c:\sqlite\data\laps.db");

        public MKTS()
        {
            InitializeComponent();
            conn.Open();
            var command = conn.CreateCommand();
            command.CommandText = @"SELECT name FROM comports limit 1";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    port = reader.GetString(0);
                }
            }

            s = new Serial(this);
            FormClosed += (_, _) => s.Dispose();
        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Trace.WriteLine("sending reset");
            s.ResetRace(resetArduino: true);
        }

        private int _lastFormSize = 0;

        private void MKTS_Load(object sender, EventArgs e)
        {
            _lastFormSize = this.Size.Width; // GetArea(this.Size);
        }

        private void MKTS_Resize(object sender, EventArgs e)
        {
            int formSize = this.Size.Width;
            float scaleFactor = ((float)(formSize) / (float)(_lastFormSize));
            name0.Font = new Font(name0.Font.FontFamily.Name, name0.Font.Size * scaleFactor);
            name1.Font = new Font(name1.Font.FontFamily.Name, name1.Font.Size * scaleFactor);
            name2.Font = new Font(name2.Font.FontFamily.Name, name2.Font.Size * scaleFactor);
            name3.Font = new Font(name3.Font.FontFamily.Name, name3.Font.Size * scaleFactor);
            name4.Font = new Font(name4.Font.FontFamily.Name, name4.Font.Size * scaleFactor);
            name5.Font = new Font(name5.Font.FontFamily.Name, name5.Font.Size * scaleFactor);
            name6.Font = new Font(name6.Font.FontFamily.Name, name6.Font.Size * scaleFactor);
            name7.Font = new Font(name7.Font.FontFamily.Name, name7.Font.Size * scaleFactor);

            laps0.Font = new Font(laps0.Font.FontFamily.Name, laps0.Font.Size * scaleFactor);
            laps1.Font = new Font(laps1.Font.FontFamily.Name, laps1.Font.Size * scaleFactor);
            laps2.Font = new Font(laps2.Font.FontFamily.Name, laps2.Font.Size * scaleFactor);
            laps3.Font = new Font(laps3.Font.FontFamily.Name, laps3.Font.Size * scaleFactor);
            laps4.Font = new Font(laps4.Font.FontFamily.Name, laps4.Font.Size * scaleFactor);
            laps5.Font = new Font(laps5.Font.FontFamily.Name, laps5.Font.Size * scaleFactor);
            laps6.Font = new Font(laps6.Font.FontFamily.Name, laps6.Font.Size * scaleFactor);
            laps7.Font = new Font(laps7.Font.FontFamily.Name, laps7.Font.Size * scaleFactor);

            bl0.Font = new Font(bl0.Font.FontFamily.Name, bl0.Font.Size * scaleFactor);
            bl1.Font = new Font(bl1.Font.FontFamily.Name, bl1.Font.Size * scaleFactor);
            bl2.Font = new Font(bl2.Font.FontFamily.Name, bl2.Font.Size * scaleFactor);
            bl3.Font = new Font(bl3.Font.FontFamily.Name, bl3.Font.Size * scaleFactor);
            bl4.Font = new Font(bl4.Font.FontFamily.Name, bl4.Font.Size * scaleFactor);
            bl5.Font = new Font(bl5.Font.FontFamily.Name, bl5.Font.Size * scaleFactor);
            bl6.Font = new Font(bl6.Font.FontFamily.Name, bl6.Font.Size * scaleFactor);
            bl7.Font = new Font(bl7.Font.FontFamily.Name, bl7.Font.Size * scaleFactor);

            ll0.Font = new Font(ll0.Font.FontFamily.Name, ll0.Font.Size * scaleFactor);
            ll1.Font = new Font(ll1.Font.FontFamily.Name, ll1.Font.Size * scaleFactor);
            ll2.Font = new Font(ll2.Font.FontFamily.Name, ll2.Font.Size * scaleFactor);
            ll3.Font = new Font(ll3.Font.FontFamily.Name, ll3.Font.Size * scaleFactor);
            ll4.Font = new Font(ll4.Font.FontFamily.Name, ll4.Font.Size * scaleFactor);
            ll5.Font = new Font(ll5.Font.FontFamily.Name, ll5.Font.Size * scaleFactor);
            ll6.Font = new Font(ll6.Font.FontFamily.Name, ll6.Font.Size * scaleFactor);
            ll7.Font = new Font(ll7.Font.FontFamily.Name, ll7.Font.Size * scaleFactor);

            ml0.Font = new Font(ml0.Font.FontFamily.Name, ml0.Font.Size * scaleFactor);
            ml1.Font = new Font(ml1.Font.FontFamily.Name, ml1.Font.Size * scaleFactor);
            ml2.Font = new Font(ml2.Font.FontFamily.Name, ml2.Font.Size * scaleFactor);
            ml3.Font = new Font(ml3.Font.FontFamily.Name, ml3.Font.Size * scaleFactor);
            ml4.Font = new Font(ml4.Font.FontFamily.Name, ml4.Font.Size * scaleFactor);
            ml5.Font = new Font(ml5.Font.FontFamily.Name, ml5.Font.Size * scaleFactor);
            ml6.Font = new Font(ml6.Font.FontFamily.Name, ml6.Font.Size * scaleFactor);
            ml7.Font = new Font(ml7.Font.FontFamily.Name, ml7.Font.Size * scaleFactor);

            mph0.Font = new Font(mph0.Font.FontFamily.Name, mph0.Font.Size * scaleFactor);
            mph1.Font = new Font(mph1.Font.FontFamily.Name, mph1.Font.Size * scaleFactor);
            mph2.Font = new Font(mph2.Font.FontFamily.Name, mph2.Font.Size * scaleFactor);
            mph3.Font = new Font(mph3.Font.FontFamily.Name, mph3.Font.Size * scaleFactor);
            mph4.Font = new Font(mph4.Font.FontFamily.Name, mph4.Font.Size * scaleFactor);
            mph5.Font = new Font(mph5.Font.FontFamily.Name, mph5.Font.Size * scaleFactor);
            mph6.Font = new Font(mph6.Font.FontFamily.Name, mph6.Font.Size * scaleFactor);
            mph7.Font = new Font(mph7.Font.FontFamily.Name, mph7.Font.Size * scaleFactor);

            labelMKTS.Font = new Font(labelMKTS.Font.FontFamily.Name, labelMKTS.Font.Size * scaleFactor);

            _lastFormSize = this.Size.Width; // GetArea(this.Size);
        }

        private int GetArea(Size size)
        {
            return size.Height * size.Width;
        }

        private void editUsersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var editUsers = new EditUsers())
            {
                editUsers.ShowDialog();
            }
        }

        private void name7_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name7ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);
        }

        private void name7ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name7.Text = e.ClickedItem.Text;
        }

        private void name6_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();
            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name6ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name6ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name6.Text = e.ClickedItem.Text;
        }

        private void name5_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name5ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name5ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name5.Text = e.ClickedItem.Text;
        }

        private void name4_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name4ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name4ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name4.Text = e.ClickedItem.Text;
        }
        private void name3_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name3ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name3ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name3.Text = e.ClickedItem.Text;
        }

        private void name2_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name2ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name2ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name2.Text = e.ClickedItem.Text;
        }

        private void name1_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name1ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }

        private void name1ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name1.Text = e.ClickedItem.Text;
        }

        private void name0_Click(object sender, EventArgs e)
        {
            contextMenuStrip1 = new ContextMenuStrip();

            var command = MKTS.conn.CreateCommand();
            command.CommandText = @"SELECT name FROM users";

            using (var reader = command.ExecuteReader())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        contextMenuStrip1.Items.Add(reader[0].ToString());
                    }
                }
            }

            contextMenuStrip1.ItemClicked += new ToolStripItemClickedEventHandler(name0ContextMenuStrip1_Click);
            contextMenuStrip1.Show(Cursor.Position);

        }
        private void name0ContextMenuStrip1_Click(object sender, ToolStripItemClickedEventArgs e)
        {
            name0.Text = e.ClickedItem.Text;
        }

        private void configureToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using Configure config = new Configure(MinLapMilliseconds, SoundOnTooFastLap);
            if (config.ShowDialog(this) == DialogResult.OK)
            {
                MinLapMilliseconds = config.MinLapMilliseconds;
                SoundOnTooFastLap = config.SoundOnTooFastLap;
                s.ApplySettings();
            }
        }

        private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        private void labelMKTS_Click(object sender, EventArgs e)
        {

        }
    }
}
