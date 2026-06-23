using System.Diagnostics;
using System.Drawing.Text;
using System.IO.Ports;
using System.Xml.Serialization;
using Microsoft.Data.Sqlite;

namespace tlp
{
    public partial class MKTS : Form
    {
        static Serial s = null!;
        private Label[] _boardValueLabels = Array.Empty<Label>();
        private Label[] _boardHeaderLabels = Array.Empty<Label>();
        public string port = "";
        public int MinLapMilliseconds { get; private set; } = LapRaceOptions.Default.MinLapMilliseconds;
        public bool SoundOnTooFastLap { get; private set; } = true;
        public static SqliteConnection conn = new SqliteConnection(@"Data Source=c:\sqlite\data\laps.db");

        public MKTS()
        {
            InitializeComponent();
            ConfigureBoardLayout();
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
            WireBestLapResetClicks();
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
            Trace.WriteLine("practice reset");
            s.ResetRace(resetArduino: true);
        }

        private void practiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPracticeMode();
        }

        private void heatRaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetPracticeMode();
            MessageBox.Show(
                this,
                "Not yet implemented",
                "Heat Race",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void SetPracticeMode()
        {
            practiceToolStripMenuItem.Checked = true;
            heatRaceToolStripMenuItem.Checked = false;
        }

        private void WireBestLapResetClicks()
        {
            Label[] bestLapLabels = { bl0, bl1, bl2, bl3, bl4, bl5, bl6, bl7 };
            for (int i = 0; i < bestLapLabels.Length; i++)
            {
                bestLapLabels[i].Tag = i;
                bestLapLabels[i].Cursor = Cursors.Hand;
                bestLapLabels[i].MouseClick += bestLapLabel_MouseClick;
            }
        }

        private void bestLapLabel_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || sender is not Label { Tag: int laneIndex })
            {
                return;
            }

            s.ResetLane(laneIndex);
        }

        private void ConfigureBoardLayout()
        {
            _boardHeaderLabels = new[] { label2, label4, label6, label8, label10, label12 };
            _boardValueLabels = new[]
            {
                name0, laps0, ll0, bl0, ml0, mph0,
                name1, laps1, ll1, bl1, ml1, mph1,
                name2, laps2, ll2, bl2, ml2, mph2,
                name3, laps3, ll3, bl3, ml3, mph3,
                name4, laps4, ll4, bl4, ml4, mph4,
                name5, laps5, ll5, bl5, ml5, mph5,
                name6, laps6, ll6, bl6, ml6, mph6,
                name7, laps7, ll7, bl7, ml7, mph7
            };

            foreach (Label label in _boardHeaderLabels.Concat(_boardValueLabels))
            {
                label.AutoSize = false;
                label.Dock = DockStyle.Fill;
                label.Margin = Padding.Empty;
                label.TextAlign = ContentAlignment.MiddleCenter;
            }

            labelMKTS.AutoSize = false;
            labelMKTS.TextAlign = ContentAlignment.MiddleCenter;
            ApplyBoardFonts();
        }

        private void MKTS_Load(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void MKTS_Resize(object sender, EventArgs e)
        {
            ApplyBoardFonts();
        }

        private void ApplyBoardFonts()
        {
            foreach (Label label in _boardHeaderLabels)
            {
                SetFontSize(label, label.Height * 0.45f);
            }

            foreach (Label label in _boardValueLabels)
            {
                SetFontSize(label, label.Height * 0.48f);
            }

            SetFontSize(labelMKTS, labelMKTS.Height * 0.4f);
        }

        private static void SetFontSize(Label label, float requestedSize)
        {
            if (label.Height <= 0)
            {
                return;
            }

            float size = Math.Clamp(requestedSize, 10f, 72f);
            if (Math.Abs(label.Font.Size - size) < 0.5f)
            {
                return;
            }

            label.Font = new Font(label.Font.FontFamily, size, label.Font.Style);
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

        private void name7ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name7.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name6ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name6.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name5ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name5.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name4ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name4.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name3ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name3.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name2ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name2.Text = e.ClickedItem?.Text ?? string.Empty;
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

        private void name1ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name1.Text = e.ClickedItem?.Text ?? string.Empty;
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
        private void name0ContextMenuStrip1_Click(object? sender, ToolStripItemClickedEventArgs e)
        {
            name0.Text = e.ClickedItem?.Text ?? string.Empty;
        }

        private void configureToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            using Configure config = new Configure(MinLapMilliseconds, SoundOnTooFastLap, port);
            if (config.ShowDialog(this) == DialogResult.OK)
            {
                MinLapMilliseconds = config.MinLapMilliseconds;
                SoundOnTooFastLap = config.SoundOnTooFastLap;
                s.ApplySettings();
                s.SetPort(config.SelectedPort);
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
