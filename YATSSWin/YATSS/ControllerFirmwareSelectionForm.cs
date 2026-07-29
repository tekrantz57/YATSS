namespace YATSS
{
    public sealed class ControllerFirmwareSelectionForm : Form
    {
        private readonly ComboBox _boardComboBox = new();

        public ControllerFirmwareSelectionForm(IReadOnlyList<ControllerFirmwarePackage> packages)
        {
            ArgumentNullException.ThrowIfNull(packages);
            Text = "Select Controller Board";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(470, 150);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Label prompt = new()
            {
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(438, 42),
                Text = "YATSS could not identify the connected controller. Select the board " +
                       "only after confirming its printed model."
            };

            _boardComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _boardComboBox.Location = new Point(16, 61);
            _boardComboBox.Size = new Size(438, 28);
            foreach (ControllerFirmwarePackage package in packages.OrderBy(
                         candidate => candidate.Manifest.BoardDisplayName,
                         StringComparer.OrdinalIgnoreCase))
            {
                _boardComboBox.Items.Add(new PackageChoice(package));
            }
            Button cancelButton = new()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(282, 105),
                Size = new Size(80, 30)
            };
            Button continueButton = new()
            {
                Text = "Continue",
                DialogResult = DialogResult.OK,
                Location = new Point(374, 105),
                Size = new Size(80, 30),
                Enabled = false
            };
            _boardComboBox.SelectedIndexChanged += (_, _) =>
                continueButton.Enabled = _boardComboBox.SelectedIndex >= 0;

            Controls.AddRange(new Control[] { prompt, _boardComboBox, cancelButton, continueButton });
            AcceptButton = continueButton;
            CancelButton = cancelButton;
        }

        public ControllerFirmwarePackage? SelectedPackage =>
            (_boardComboBox.SelectedItem as PackageChoice)?.Package;

        private sealed record PackageChoice(ControllerFirmwarePackage Package)
        {
            public override string ToString() =>
                Package.MatchesBoardProfile(ControllerFirmwarePackage.Esp32C6BoardProfile)
                    ? $"ESP32-C6-DevKitC-1 (N4/N8 detected automatically)"
                    : $"{Package.Manifest.BoardDisplayName} ({Package.Manifest.FirmwareVersion})";
        }
    }
}
