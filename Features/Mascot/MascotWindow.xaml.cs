using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Clipboard.Services;
using PersonalAssistant.Features.Clipboard.Views;
using PersonalAssistant.Features.Widgets;
using PersonalAssistant.Features.Widgets.Services;

namespace PersonalAssistant.Features.Mascot;

/// <summary>
/// 卡通机器人悬浮窗：眼球追踪、悬停放大、点击弹跳、拖拽移动
/// 性能优化：无 DropShadowEffect、鼠标节流 25fps、动画减少重绘范围
/// </summary>
public partial class MascotWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private bool _isDragging;
    private WidgetPanel? _widgetPanel;
    private ContextMenuPopup? _contextMenuPopup;

    // 瞳孔基准位置
    private const double LpX = 35, LpY = 34, RpX = 59, RpY = 34;
    private const double LhX = 38, LhY = 32, RhX = 62, RhY = 32;
    private const double MaxOffset = 3;
    private const double HighlightRatio = 0.5;

    // 鼠标追踪节流：最小间隔 40ms（~25fps）
    private long _lastEyeTick;

    // 人偶中心
    private readonly System.Windows.Point _robotCenter = new(50, 84);

    // 预创建 Brush，避免重复分配
    private static readonly SolidColorBrush GlowGold = new(
        System.Windows.Media.Color.FromArgb(0x50, 0xFF, 0xD7, 0x00));
    private static readonly SolidColorBrush GlowCyan = new(
        System.Windows.Media.Color.FromArgb(0x80, 0x00, 0xFF, 0xFF));

    // 预创建动画对象，复用避免 GC
    private readonly DoubleAnimation _floatAnim = new()
    {
        From = 0, To = -6,
        Duration = TimeSpan.FromSeconds(2),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
    };
    private readonly CubicEase _cubicEase = new() { EasingMode = EasingMode.EaseOut };
    private readonly ElasticEase _elasticEase = new()
        { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 };
    private readonly TimeSpan _hoverDuration = TimeSpan.FromSeconds(0.15);
    private readonly TimeSpan _leaveDuration = TimeSpan.FromSeconds(0.2);
    private readonly TimeSpan _squashDuration = TimeSpan.FromSeconds(0.08);
    private readonly TimeSpan _bounceDuration = TimeSpan.FromSeconds(0.3);

    static MascotWindow()
    {
        GlowGold.Freeze();
        GlowCyan.Freeze();
    }

    public MascotWindow(IServiceProvider serviceProvider)
    {
        Serilog.Log.Information("[MascotWindow] 构造开始");
        _serviceProvider = serviceProvider;
        InitializeComponent();
        Serilog.Log.Information("[MascotWindow] 构造完成");

        Loaded += (_, _) => StartFloat();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) StartFloat(); else StopFloat();
        };

        Left = SystemParameters.WorkArea.Right - 120;
        Top = SystemParameters.WorkArea.Bottom - 185;
    }

    // ── 眼球追踪（节流）──

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDragging = true;
            return;
        }

        // 节流：最小 40ms 间隔
        var tick = Stopwatch.GetTimestamp();
        if (tick - _lastEyeTick < Stopwatch.Frequency / 25)
            return;
        _lastEyeTick = tick;

        var mousePos = e.GetPosition(this);
        var dx = Clamp(
            (mousePos.X - _robotCenter.X) / _robotCenter.X * MaxOffset,
            -MaxOffset, MaxOffset);
        var dy = Clamp(
            (mousePos.Y - _robotCenter.Y) / _robotCenter.Y * MaxOffset,
            -MaxOffset, MaxOffset);

        Canvas.SetLeft(LeftPupil, LpX + dx);
        Canvas.SetTop(LeftPupil, LpY + dy);
        Canvas.SetLeft(RightPupil, RpX + dx);
        Canvas.SetTop(RightPupil, RpY + dy);

        var hdx = dx * HighlightRatio;
        var hdy = dy * HighlightRatio;
        Canvas.SetLeft(LeftHighlight, LhX + hdx);
        Canvas.SetTop(LeftHighlight, LhY + hdy);
        Canvas.SetLeft(RightHighlight, RhX + hdx);
        Canvas.SetTop(RightHighlight, RhY + hdy);
    }

    // ── 悬停 ──

    private void RobotCanvas_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isDragging) return;
        AnimateScale(1.12, 1.12, _hoverDuration);
        AntennaGlow.Fill = GlowCyan;
    }

    private void RobotCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateScale(1.0, 1.0, _leaveDuration);
        AntennaGlow.Fill = GlowGold;
        ResetEyes();
    }

    // ── 点击弹跳 ──

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _isDragging = false;
        AnimateSquash();
        DragMove();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            AnimateBounce();
            HideWidgetPanel();
            _serviceProvider.GetRequiredService<MainWindow>().ShowWindow();
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ShowContextMenu();
    }

    /// <summary>
    /// 右键悬浮窗 → 剪贴板有内容则弹出智能上下文菜单，无内容则回退到 WidgetPanel。
    /// </summary>
    private void ShowContextMenu()
    {
        try
        {
            var monitor = _serviceProvider.GetRequiredService<ClipboardMonitor>();
            var clipText = monitor.LatestClipboardText;

            if (!string.IsNullOrEmpty(clipText))
            {
                if (_contextMenuPopup is null)
                {
                    _contextMenuPopup = _serviceProvider.GetRequiredService<ContextMenuPopup>();
                    _contextMenuPopup.Closed += (_, _) => _contextMenuPopup = null;
                }
                _contextMenuPopup.ShowFor(this, monitor.LatestClipboardType, clipText);
            }
            else
            {
                ToggleWidgetPanel();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[MascotWindow] ShowContextMenu 异常，回退到 WidgetPanel");
            ToggleWidgetPanel();
        }
    }

    private void ToggleWidgetPanel()
    {
        if (_widgetPanel?.IsVisible == true)
        {
            HideWidgetPanel();
        }
        else
        {
            ShowWidgetPanel();
        }
    }

    private void ShowWidgetPanel()
    {
        if (_widgetPanel is null)
        {
            _widgetPanel = _serviceProvider.GetRequiredService<WidgetPanel>();
            _widgetPanel.Closed += (_, _) => _widgetPanel = null;
        }
        _widgetPanel.PositionNear(this);
        _widgetPanel.Show();
    }

    private void HideWidgetPanel()
    {
        _widgetPanel?.Hide();
    }

    // ── 浮动动画控制 ──

    private void StartFloat()
        => FloatTransform.BeginAnimation(TranslateTransform.YProperty, _floatAnim);

    private void StopFloat()
        => FloatTransform.BeginAnimation(TranslateTransform.YProperty, null);

    // ── 辅助（复用预创建对象）──

    private void AnimateScale(double sx, double sy, TimeSpan duration)
    {
        ScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(sx, duration) { EasingFunction = _cubicEase });
        ScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(sy, duration) { EasingFunction = _cubicEase });
    }

    private void AnimateSquash()
        => AnimateScale(1.2, 0.85, _squashDuration);

    private void AnimateBounce()
    {
        ScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.2, 1.0, _bounceDuration) { EasingFunction = _elasticEase });
        ScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.85, 1.0, _bounceDuration) { EasingFunction = _elasticEase });
    }

    private void ResetEyes()
    {
        Canvas.SetLeft(LeftPupil, LpX);
        Canvas.SetTop(LeftPupil, LpY);
        Canvas.SetLeft(RightPupil, RpX);
        Canvas.SetTop(RightPupil, RpY);
        Canvas.SetLeft(LeftHighlight, LhX);
        Canvas.SetTop(LeftHighlight, LhY);
        Canvas.SetLeft(RightHighlight, RhX);
        Canvas.SetTop(RightHighlight, RhY);
    }

    private static double Clamp(double v, double min, double max)
        => v < min ? min : v > max ? max : v;
}
