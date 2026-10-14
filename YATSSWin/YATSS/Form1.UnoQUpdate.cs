namespace YATSS;

public partial class YATSS
{
    private async Task UpdateUnoQAsync()
    {
        if (!ControllerEndpoint.TryParseTcp(port, out string host, out int tcpPort) ||
            host is not ("127.0.0.1" or "localhost") || tcpPort != 45991)
        {
            MessageBox.Show(this, "UNO Q updates currently require YATSS on the UNO Q itself, using TCP 127.0.0.1:45991. " +
                "Remote Windows updates are not supported yet.", "UNO Q Update");
            return;
        }
        using Form prompt = new()
        {
            Text = "UNO Q Update Authorization", ClientSize = new Size(530, 140),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false
        };
        Label label = new() { Text = "Local updater token", AutoSize = true, Location = new Point(16, 15) };
        TextBox token = new() { Location = new Point(16, 42), Width = 495, UseSystemPasswordChar = true };
        Button okay = new() { Text = "Continue", DialogResult = DialogResult.OK, Location = new Point(310, 90), Width = 100 };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(420, 90), Width = 90 };
        prompt.Controls.AddRange(new Control[] { label, token, okay, cancel });
        prompt.AcceptButton = okay; prompt.CancelButton = cancel;
        if (prompt.ShowDialog(this) != DialogResult.OK) return;

        bool suspended = false;
        bool updateSubmitted = false;
        bool verified = false;
        using FirmwareUpdateProgressForm progressForm = new();
        try
        {
            using UnoQUpdateClient client = new(token.Text.Trim());
            var bundle = UnoQUpdateClient.Bundle(Path.Combine(AppContext.BaseDirectory, "Linux", "UNOQ"));
            await client.PreflightAsync();
            if (MessageBox.Show(this, $"Install UNO Q controller {bundle.Version}?\n\n" +
                "Disconnect track power and relay-coil power physically before continuing. " +
                "The Linux helper will stop the controller, install the bundled source, compile and flash it, " +
                "restart it, and verify identity. Do not close YATSS or App Lab or remove power during the update.",
                "Update UNO Q?", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            progressForm.Show(this); Enabled = false;
            suspended = true;
            await s.SuspendForFirmwareUpdateAsync();
            updateSubmitted = true;
            await client.UpdateAsync(bundle.Data, progressForm.CreateProgress());
            updateSubmitted = false;
            s.ResumeAfterFirmwareUpdate(); suspended = false;
            verified = await s.WaitForControllerIdentityAsync(ControllerFirmwareUpdatePolicy.ArduinoUnoQBoardProfile,
                bundle.Version, TimeSpan.FromSeconds(30), requireReady: true);
            if (!verified) throw new InvalidOperationException("Helper completed, but YATSS could not verify the reconnected controller. Leave track power disconnected.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or
                                    HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            MessageBox.Show(this, ex.Message + "\n\nSee Documentation/UNO_Q_UPDATES.md for helper setup and recovery. " +
                "Do not resume racing until the controller is verified." +
                (updateSubmitted ? " YATSS will leave its controller connection suspended. Check the helper log, " +
                    "wait for the update to finish, then restart YATSS." : ""),
                "UNO Q Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (suspended && !updateSubmitted) s.ResumeAfterFirmwareUpdate();
            progressForm.Complete(); progressForm.Close(); Enabled = true; Activate();
        }
        SetStatusMessage(verified ? "UNO Q update verified; track power remains off" : "UNO Q update not verified; leave track power off");
    }
}
