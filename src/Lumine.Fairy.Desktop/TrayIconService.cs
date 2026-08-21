using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Controls;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Desktop;

public sealed class TrayIconService : IDisposable
{
    private readonly Window _window;
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _exitAction;
    private readonly Func<Task>? _markAllReadAction;
    private readonly ToolStripMenuItem _pauseNotificationsItem;
    private bool _notificationsPaused;
    private bool _allowExit;

    public TrayIconService(Window window, Action exitAction, Func<Task>? markAllReadAction = null)
    {
        _window = window;
        _exitAction = exitAction;
        _markAllReadAction = markAllReadAction;
        _pauseNotificationsItem = CreateItem("暂停通知", MenuIcon.Pause, async (_, _) => await ToggleNotificationsAsync());
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "fairy-tray.ico");
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Fairy",
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            ContextMenuStrip = CreateContextMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new TrayContextMenuStrip
        {
            AutoSize = true,
            MinimumSize = new Size(230, 0),
            ShowImageMargin = true,
            ShowCheckMargin = false,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(20, 32, 51),
            Font = new Font("Segoe UI", 10.5F),
            Padding = new Padding(8),
            DropShadowEnabled = true,
            Renderer = new TrayMenuRenderer(),
        };
        menu.Items.Add(CreateItem("打开通知中心", MenuIcon.Open, (_, _) => ShowWindow()));
        menu.Items.Add(_pauseNotificationsItem);
        menu.Items.Add(CreateItem("全部标记已读", MenuIcon.MarkRead, async (_, _) => await MarkAllReadAsync()));
        menu.Items.Add(new ToolStripSeparator
        {
            Margin = new Padding(8, 6, 8, 6),
            Height = 1,
        });
        menu.Items.Add(CreateItem("退出", MenuIcon.Exit, (_, _) =>
        {
            _allowExit = true;
            _exitAction();
        }));
        return menu;
    }

    private static ToolStripMenuItem CreateItem(string text, MenuIcon icon, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Width = 212,
            Height = 38,
            Image = CreateIcon(icon),
            ImageAlign = ContentAlignment.MiddleLeft,
            ImageScaling = ToolStripItemImageScaling.None,
            TextAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(10, 0, 14, 0),
            Margin = new Padding(0, 1, 0, 1),
        };
        item.Click += handler;
        return item;
    }

    public bool AllowExit => _allowExit;

    public void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void HideWindow() => _window.Hide();

    public void ShowNotification(NotificationRecord notification)
    {
        if (_notificationsPaused) return;

        var icon = notification.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)
                   || notification.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
            ? ToolTipIcon.Error
            : notification.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? ToolTipIcon.Warning
                : ToolTipIcon.Info;
        _notifyIcon.BalloonTipTitle = notification.Title;
        _notifyIcon.BalloonTipText = notification.Body;
        _notifyIcon.ShowBalloonTip(Math.Clamp(notification.DurationSeconds * 1000, 1000, 30000), notification.Title, notification.Body, icon);
    }

    private async Task ToggleNotificationsAsync()
    {
        _notificationsPaused = !_notificationsPaused;
        _pauseNotificationsItem.Text = _notificationsPaused ? "恢复通知" : "暂停通知";
        await Task.CompletedTask;
    }

    private async Task MarkAllReadAsync()
    {
        if (_markAllReadAction is not null)
            await _markAllReadAction();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private enum MenuIcon
    {
        Open,
        Pause,
        MarkRead,
        Exit,
    }

    private static Bitmap CreateIcon(MenuIcon icon)
    {
        var bitmap = new Bitmap(20, 20, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(20, 32, 51), 1.55F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var brush = new SolidBrush(Color.FromArgb(20, 32, 51));

        switch (icon)
        {
            case MenuIcon.Open:
                graphics.DrawRectangle(pen, 3, 4, 14, 10);
                graphics.DrawLine(pen, 6, 14, 6, 17);
                graphics.DrawLine(pen, 6, 17, 9, 14);
                graphics.FillEllipse(brush, 6, 8, 2, 2);
                graphics.FillEllipse(brush, 9, 8, 2, 2);
                graphics.FillEllipse(brush, 12, 8, 2, 2);
                break;
            case MenuIcon.Pause:
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawLine(pen, 8, 7, 8, 13);
                graphics.DrawLine(pen, 12, 7, 12, 13);
                break;
            case MenuIcon.MarkRead:
                graphics.DrawEllipse(pen, 3, 3, 14, 14);
                graphics.DrawLine(pen, 6, 10, 9, 13);
                graphics.DrawLine(pen, 9, 13, 15, 6);
                break;
            case MenuIcon.Exit:
                graphics.DrawRectangle(pen, 7, 3, 8, 14);
                graphics.DrawLine(pen, 2, 10, 11, 10);
                graphics.DrawLine(pen, 8, 7, 11, 10);
                graphics.DrawLine(pen, 8, 13, 11, 10);
                break;
        }

        return bitmap;
    }

    private sealed class TrayContextMenuStrip : ContextMenuStrip
    {
        private const int ClassStyleDropShadow = 0x00020000;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ClassStyle |= ClassStyleDropShadow;
                return parameters;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) UpdateRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Visible) UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using var path = CreateRoundedRectanglePath(new Rectangle(Point.Empty, Size), 10);
            Region?.Dispose();
            Region = new Region(path);
        }
    }

    private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayMenuColorTable()) => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.White);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            var bounds = e.Item.Bounds;
            bounds.Inflate(-2, -1);
            using var brush = new SolidBrush(Color.FromArgb(241, 244, 248));
            using var path = CreateRoundedRectanglePath(bounds, 6);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(229, 233, 240));
            var y = e.Item.Bounds.Top + e.Item.Bounds.Height / 2;
            e.Graphics.DrawLine(pen, e.Item.Bounds.Left + 8, y, e.Item.Bounds.Right - 8, y);
        }
    }

    private sealed class TrayMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Color.FromArgb(220, 226, 234);
        public override Color MenuItemBorder => Color.FromArgb(210, 224, 240);
        public override Color MenuItemSelected => Color.FromArgb(232, 242, 255);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 242, 255);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(232, 242, 255);
        public override Color ToolStripDropDownBackground => Color.White;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
