using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HwOverlay.Models;
using HwOverlay.Services;
using HwOverlay.ViewModels;
using Microsoft.Win32;

namespace HwOverlay.Views;

/// <summary>
/// Modo ultra compacto: mini-gauges numa janela sobreposta à barra de tarefas.
/// O Windows 11 não deixa colocar nada dentro da barra; esta janela só acompanha a posição dela,
/// se reafirma no topo quando a barra vem para a frente e some em tela cheia/ocultação automática.
/// </summary>
public partial class TaskbarWindow : Window
{
    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(TaskbarWindow), new PropertyMetadata(36.0));

    public static readonly DependencyProperty RingThicknessProperty =
        DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(TaskbarWindow), new PropertyMetadata(3.5));

    public static readonly DependencyProperty ValueFontSizeProperty =
        DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(TaskbarWindow), new PropertyMetadata(12.0));

    public static readonly DependencyProperty LabelFontSizeProperty =
        DependencyProperty.Register(nameof(LabelFontSize), typeof(double), typeof(TaskbarWindow), new PropertyMetadata(10.5));

    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(nameof(Palette), typeof(TaskbarPalette), typeof(TaskbarWindow), new PropertyMetadata(TaskbarPalette.Dark));

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly OverlayViewModel _vm;
    private readonly DispatcherTimer _timer;
    private readonly WinEventDelegate _foregroundCallback; // referência viva: o Windows chama de volta
    private IntPtr _foregroundHook;
    private bool _active;
    private bool _placing;
    private int _barHeightPx;
    private double _scale;

    private bool _dragging;
    private double _dragStartX;
    private double _dragStartOffset;

    public TaskbarWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        SourceInitialized += (_, _) => OverlayWindowHelper.ApplyToolWindowStyle(this);
        SizeChanged += (_, _) => UpdatePlacement();

        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Trocar o tema do Windows (claro/escuro, cor de destaque) muda a cor da barra.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        UpdatePalette();

        // Acompanha mudanças da barra (bandeja cresce, DPI, ocultação automática, tela cheia).
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => UpdatePlacement();

        // Clicar na barra a traz para a frente da nossa janela; reage na hora em vez de esperar o timer.
        _foregroundCallback = (_, _, _, _, _, _, _) => UpdatePlacement();

        Closed += (_, _) =>
        {
            SetActive(false);
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; // evento estático: sem isso, vaza
        };
    }

    public event EventHandler? OpenSensorsRequested;

    public event EventHandler? ExitRequested;

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty);
        set => SetValue(ValueFontSizeProperty, value);
    }

    public double LabelFontSize
    {
        get => (double)GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    public TaskbarPalette Palette
    {
        get => (TaskbarPalette)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>Liga/desliga o modo. Mesmo ativo, a janela some sozinha quando a barra não está visível.</summary>
    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;

        if (active)
        {
            _timer.Start();
            if (_foregroundHook == IntPtr.Zero)
                _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                    _foregroundCallback, 0, 0, WINEVENT_OUTOFCONTEXT);
            UpdatePlacement();
        }
        else
        {
            _timer.Stop();
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            Hide();
        }
    }

    private void UpdatePlacement()
    {
        if (!_active || _placing) return;

        _placing = true; // Show() e Height disparam SizeChanged, que chamaria de novo
        try
        {
            Place();
        }
        finally
        {
            _placing = false;
        }
    }

    private void Place()
    {
        var info = TaskbarLocator.Find();
        if (info is null || !info.IsHorizontal || info.IsHidden || TaskbarLocator.IsFullscreenAppRunning())
        {
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible) Show();

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bar = info.Bounds;

        // Altura da barra (Win11: 48 px a 100%, menos no modo compacto) → tamanho dos mini-gauges.
        if (bar.Height != _barHeightPx || Math.Abs(scale - _scale) > 0.001)
        {
            _barHeightPx = bar.Height;
            _scale = scale;

            var heightDip = bar.Height / scale;
            Height = heightDip;
            var ring = Math.Clamp(heightDip - 14, 20, 40);
            RingSize = ring;
            RingThickness = Math.Max(2.5, ring * 0.1);
            ValueFontSize = Math.Max(9, ring * 0.34);
            LabelFontSize = Math.Clamp(ring * 0.3, 9, 11.5);
            UpdateLayout();
        }

        if (ActualWidth <= 0) return;

        var widthPx = (int)Math.Ceiling(ActualWidth * scale);
        var offsetPx = (int)Math.Round(_vm.TaskbarOffset * scale);

        int x;
        if (_vm.TaskbarSide == TaskbarSide.Left)
        {
            x = bar.Left + offsetPx;
        }
        else
        {
            var anchor = info.Tray.IsEmpty ? bar.Right : info.Tray.Left;
            x = anchor - offsetPx - widthPx;
        }

        x = Math.Clamp(x, bar.Left, Math.Max(bar.Left, bar.Right - widthPx));
        var y = bar.Top + (bar.Height - (int)Math.Round(ActualHeight * scale)) / 2;

        OverlayWindowHelper.PlaceTopmost(this, x, y);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverlayViewModel.TaskbarSide) or nameof(OverlayViewModel.TaskbarOffset))
            UpdatePlacement();
        else if (e.PropertyName == nameof(OverlayViewModel.TaskbarBackgroundOpacity))
            UpdatePalette();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.InvokeAsync(UpdatePalette);

    /// <summary>
    /// Com fundo próprio forte (≥ 50%) a "pílula" escura domina e vale a paleta escura;
    /// abaixo disso o texto fica direto sobre a barra e segue o tema dela.
    /// </summary>
    private void UpdatePalette() =>
        Palette = _vm.TaskbarBackgroundOpacity < 0.5 && TaskbarLocator.IsTaskbarLight()
            ? TaskbarPalette.Light
            : TaskbarPalette.Dark;

    // ---------- Arrastar para os lados ajusta a distância ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = CaptureMouse();
        _dragStartX = PointToScreen(e.GetPosition(this)).X;
        _dragStartOffset = _vm.TaskbarOffset;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var deltaDip = (PointToScreen(e.GetPosition(this)).X - _dragStartX) / VisualTreeHelper.GetDpi(this).DpiScaleX;

        // Lado direito: a distância é medida a partir da bandeja, para a esquerda.
        _vm.TaskbarOffset = _vm.TaskbarSide == TaskbarSide.Left
            ? _dragStartOffset + deltaDip
            : _dragStartOffset - deltaDip;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OpenSensors_Click(object sender, RoutedEventArgs e) =>
        OpenSensorsRequested?.Invoke(this, EventArgs.Empty);

    private void Floating_Click(object sender, RoutedEventArgs e) => _vm.Mode = OverlayMode.Floating;

    private void Hide_Click(object sender, RoutedEventArgs e) => _vm.OverlayVisible = false;

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
