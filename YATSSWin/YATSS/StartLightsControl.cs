namespace YATSS
{
    internal sealed class StartLightsControl : Control
    {
        private static readonly Color[] LightColors =
        {
            Color.FromArgb(225, 50, 50),
            Color.FromArgb(255, 193, 7),
            Color.FromArgb(40, 200, 90)
        };

        private int _activeLight;

        public StartLightsControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            AccessibleName = "Start countdown lights";
        }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ActiveLight
        {
            get => _activeLight;
            set
            {
                int activeLight = Math.Clamp(value, 0, LightColors.Length);
                if (_activeLight == activeLight)
                {
                    return;
                }

                _activeLight = activeLight;
                AccessibleDescription = activeLight == 0
                    ? "Countdown inactive"
                    : $"Countdown light {activeLight} of {LightColors.Length}";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            const int gap = 6;
            int diameter = Math.Min(24, Math.Min(ClientSize.Height - 8, (ClientSize.Width - (gap * 2)) / 3));
            if (diameter <= 0)
            {
                return;
            }

            int totalWidth = (diameter * 3) + (gap * 2);
            int left = (ClientSize.Width - totalWidth) / 2;
            int top = (ClientSize.Height - diameter) / 2;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            for (int i = 0; i < LightColors.Length; i++)
            {
                Rectangle bounds = new(left + (i * (diameter + gap)), top, diameter, diameter);
                Color fill = _activeLight == i + 1 ? LightColors[i] : Color.FromArgb(68, 68, 68);
                using SolidBrush brush = new(fill);
                using Pen outline = new(Color.FromArgb(175, 175, 175), 1.5F);
                e.Graphics.FillEllipse(brush, bounds);
                e.Graphics.DrawEllipse(outline, bounds);
            }
        }
    }
}
