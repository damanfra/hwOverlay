using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using HwOverlay.Services;
using HwOverlay.ViewModels;

namespace HwOverlay.Views;

public partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _vm;
    private readonly DispatcherTimer _topmostTimer;
    private bool _positioned;
    private bool _anchoredRight;   // posição padrão: cresce para a esquerda, grudado na borda direita
    private bool _movingByCode;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        SourceInitialized += (_, _) =>
        {
            OverlayWindowHelper.ApplyToolWindowStyle(this);
            OverlayWindowHelper.SetClickThrough(this, _vm.ClickThrough);
        };

        // SizeToContent: a posição padrão (canto superior direito) depende do tamanho real.
        SizeChanged += (_, _) => EnsurePosition();
        LocationChanged += (_, _) =>
        {
            if (!_positioned || _movingByCode) return;
            _anchoredRight = false; // o usuário arrastou: respeita a posição escolhida
            _vm.SavePosition(Left, Top);
        };

        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Alguns jogos/apps em tela cheia "sem bordas" jogam o overlay para trás de vez em quando.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _topmostTimer.Tick += (_, _) => OverlayWindowHelper.BringToTopmost(this);
        _topmostTimer.Start();

        Closed += (_, _) =>
        {
            _topmostTimer.Stop();
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    public event EventHandler? OpenSensorsRequested;

    public event EventHandler? ExitRequested;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.ClickThrough))
            OverlayWindowHelper.SetClickThrough(this, _vm.ClickThrough);
    }

    private void EnsurePosition()
    {
        if (ActualWidth <= 0) return;

        var area = SystemParameters.WorkArea;

        if (!_positioned)
        {
            var (left, top) = _vm.SavedPosition;
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            var onScreen = left is { } l && top is { } t
                           && l + 40 < virtualRight && l + ActualWidth - 40 > virtualLeft
                           && t + 20 < virtualBottom && t > virtualTop - 20;

            if (onScreen)
            {
                MoveTo(left!.Value, top!.Value);
            }
            else
            {
                // Padrão: canto superior direito da tela principal.
                _anchoredRight = true;
                MoveTo(area.Right - ActualWidth - 16, area.Top + 16);
            }

            _positioned = true;
            return;
        }

        // O overlay mudou de tamanho (gauge adicionado/removido, tamanho alterado).
        if (_anchoredRight)
        {
            MoveTo(area.Right - ActualWidth - 16, Top);
        }
        else
        {
            // Não deixa crescer para fora da área de trabalho.
            var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            if (Left + ActualWidth > right) MoveTo(Math.Max(SystemParameters.VirtualScreenLeft, right - ActualWidth), Top);
        }
    }

    private void MoveTo(double left, double top)
    {
        _movingByCode = true;
        try
        {
            Left = left;
            Top = top;
        }
        finally
        {
            _movingByCode = false;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm.ClickThrough || e.ButtonState != MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // botão já foi solto
        }
    }

    /// <summary>Ctrl + roda do mouse: aumenta/diminui os gauges.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        _vm.GaugeSize += e.Delta > 0 ? 10 : -10;
        e.Handled = true;
    }

    private void OpenSensors_Click(object sender, RoutedEventArgs e) =>
        OpenSensorsRequested?.Invoke(this, EventArgs.Empty);

    private void Lock_Click(object sender, RoutedEventArgs e) => _vm.ClickThrough = true;

    private void Hide_Click(object sender, RoutedEventArgs e) => _vm.OverlayVisible = false;

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
