using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        private readonly ApiService _apiService;
        private readonly Stopwatch _physicsClock = new();
        private PathGeometry? _cordGeometry;
        private PathFigure? _cordFigure;
        private TranslateTransform? _pullTranslate;
        private CancellationTokenSource? _viewCts;
        private StackPanel? _loginContentPanel;
        private bool _lampOn;
        private bool _draggingCord;
        private bool _physicsRunning;
        private double _pullX;
        private double _pullY;
        private double _pullVelocityX;
        private double _pullVelocityY;
        private Point _lastMousePosition;
        private double _lampAngle;
        private double _lampAngularVelocity;
        private const int CordPointCount = 7;
        private readonly Point[] _cordPoints = new Point[CordPointCount];
        private readonly Point[] _cordPointsPrev = new Point[CordPointCount];
        private double _cordSegmentLength;
        private bool _cordInitialized;
        private const double CordGravity = 900.0;
        private const double CordVerletDamping = 0.985;
        private const int CordConstraintIterations = 4;
        private const double CordRestSpeedEpsilon = 0.6;
        private const double CordTopX = 330;
        private const double CordTopY = 245;
        private const double RestPullY = 350;
        private const double MaxPullDistance = 175;
        private const double ToggleThreshold = 72;
        private const double SpringStrength = 20.5;
        private const double Damping = 6.8;
        private const double MaxSpeed = 850.0;
        private const double GlassHoverScale = 1.035;
        private const double GlassPressScale = 0.97;
        private const int GlassHoverMs = 220;
        private const int GlassPressMs = 90;
        private const int GlassReleaseMs = 150;
        private const double LampSubtitleGap = 10;

        public event Action? CreateAccountRequested;
        public event Action? LoginSuccessful;
        public event Action? OwnerLoginSuccessful;

        public LoginView()
        {
            InitializeComponent();
            _apiService = new ApiService();
            Loaded += LoginView_Loaded;
            Unloaded += LoginView_Unloaded;
        }

        private void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            _viewCts = new CancellationTokenSource();
            _lampOn = false;
            _draggingCord = false;
            _pullX = 0;
            _pullY = 0;
            _pullVelocityX = 0;
            _pullVelocityY = 0;
            _lampAngle = 0;
            _lampAngularVelocity = 0;
            EnsureCordGeometry();
            EnsurePullTransform();
            InitializeCordRope();
            ApplyLampVisuals(false, false);
            UpdateCordVisual();
            ApplyLampTransform();
            ApplyWelcomeText();
            PositionLampSubtitle();
            LampPull.LostMouseCapture += LampPull_LostMouseCapture;
            ApplyLiquidGlassHover(LoginButton);
            ApplyLiquidGlassHover(CreateAccountButton);
        }

        private void LoginView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPhysics();
            LampPull.LostMouseCapture -= LampPull_LostMouseCapture;
            _viewCts?.Cancel();
            _viewCts?.Dispose();
            _viewCts = null;
        }

        private void ApplyWelcomeText()
        {
            WelcomeText.Text = "Welcome to NovaChat";
            WelcomeSubText.Text = "Turn on the lamp to get start.";

            if (_loginContentPanel == null && LoginCard.Parent is Grid loginColumn)
            {
                loginColumn.Children.Remove(LoginCard);
                _loginContentPanel = new StackPanel
                {
                    Width = 390,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                loginColumn.Children.Add(_loginContentPanel);
            }

            if (_loginContentPanel == null)
                return;

            if (WelcomeText.Parent is Panel welcomeParent)
                welcomeParent.Children.Remove(WelcomeText);
            if (LoginCard.Parent is Panel loginParent)
                loginParent.Children.Remove(LoginCard);

            WelcomeText.HorizontalAlignment = HorizontalAlignment.Center;
            WelcomeText.Margin = new Thickness(0, 0, 0, 18);
            WelcomeSubText.HorizontalAlignment = HorizontalAlignment.Center;
            WelcomeSubText.Margin = new Thickness(0);

            _loginContentPanel.Children.Clear();
            _loginContentPanel.Children.Add(WelcomeText);
            _loginContentPanel.Children.Add(LoginCard);
        }

        private void PositionLampSubtitle()
        {
            if (WelcomeSubText.Parent is not Canvas)
                return;

            LampCanvas.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WelcomeSubText.Parent is not Canvas)
                    return;

                WelcomeSubText.UpdateLayout();

                double subtitleWidth = WelcomeSubText.ActualWidth > 0
                    ? WelcomeSubText.ActualWidth
                    : WelcomeSubText.DesiredSize.Width;

                double rootWidth = LoginRoot.ActualWidth > 0 ? LoginRoot.ActualWidth : ActualWidth;
                double rootHeight = LoginRoot.ActualHeight > 0 ? LoginRoot.ActualHeight : ActualHeight;
                double canvasOffsetX = (rootWidth - LampCanvas.Width) / 2.0;
                double left = (rootWidth - subtitleWidth) / 2.0 - canvasOffsetX;

                Canvas.SetLeft(WelcomeSubText, left);
                Canvas.SetTop(WelcomeSubText, Math.Max(0, LampCanvas.Height - WelcomeSubText.ActualHeight - 12));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void EnsureCordGeometry()
        {
            if (_cordGeometry != null && _cordFigure != null)
                return;
            _cordFigure = new PathFigure { StartPoint = new Point(CordTopX, CordTopY), IsClosed = false, IsFilled = false };
            for (int i = 0; i < CordPointCount - 1; i++)
                _cordFigure.Segments.Add(new BezierSegment { IsStroked = true });
            _cordGeometry = new PathGeometry();
            _cordGeometry.Figures.Add(_cordFigure);
            PullCord.Data = _cordGeometry;
        }

        private void EnsurePullTransform()
        {
            if (_pullTranslate != null)
                return;
            _pullTranslate = new TranslateTransform();
            LampPull.RenderTransform = _pullTranslate;
            LampPull.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void InitializeCordRope()
        {
            _cordSegmentLength = (RestPullY - CordTopY) / (CordPointCount - 1);
            for (int i = 0; i < CordPointCount; i++)
            {
                double t = (double)i / (CordPointCount - 1);
                var point = new Point(CordTopX, CordTopY + (RestPullY - CordTopY) * t);
                _cordPoints[i] = point;
                _cordPointsPrev[i] = point;
            }
            _cordInitialized = true;
        }

        private void LampPull_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingCord = true;
            _lastMousePosition = e.GetPosition(LampCanvas);
            _pullVelocityX = 0;
            _pullVelocityY = 0;
            StartPhysics();
            LampPull.CaptureMouse();
            e.Handled = true;
        }

        private void LampCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingCord || e.LeftButton != MouseButtonState.Pressed)
                return;
            Point position = e.GetPosition(LampCanvas);
            double deltaX = position.X - _lastMousePosition.X;
            double deltaY = position.Y - _lastMousePosition.Y;
            _lastMousePosition = position;
            double nextX = _pullX + deltaX;
            double nextY = _pullY + deltaY;
            double distance = Math.Sqrt(nextX * nextX + nextY * nextY);
            if (distance > MaxPullDistance)
            {
                double scale = MaxPullDistance / distance;
                nextX *= scale;
                nextY *= scale;
            }
            _pullVelocityX = deltaX * 60;
            _pullVelocityY = deltaY * 60;
            _pullX = nextX;
            _pullY = nextY;
            UpdateCordVisual();
        }

        private void LampPull_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_draggingCord)
                return;
            _draggingCord = false;
            LampPull.ReleaseMouseCapture();
            if (Math.Sqrt(_pullX * _pullX + _pullY * _pullY) >= ToggleThreshold)
                ToggleLamp();
            StartPhysics();
            e.Handled = true;
        }

        private void LampPull_LostMouseCapture(object? sender, MouseEventArgs e)
        {
            if (!_draggingCord)
                return;
            _draggingCord = false;
            StartPhysics();
        }

        private void StartPhysics()
        {
            if (_physicsRunning)
                return;
            _physicsRunning = true;
            _physicsClock.Restart();
            CompositionTarget.Rendering += PhysicsRendering;
        }

        private void StopPhysics()
        {
            if (!_physicsRunning)
                return;
            _physicsRunning = false;
            CompositionTarget.Rendering -= PhysicsRendering;
            _physicsClock.Stop();
        }

        private void PhysicsRendering(object? sender, EventArgs e)
        {
            double dt = Math.Min(_physicsClock.Elapsed.TotalSeconds, 0.033);
            _physicsClock.Restart();
            if (dt <= 0)
                return;

            if (!_draggingCord)
            {
                double ax = -SpringStrength * _pullX - Damping * _pullVelocityX;
                double ay = -SpringStrength * _pullY - Damping * _pullVelocityY;
                _pullVelocityX += ax * dt;
                _pullVelocityY += ay * dt;
                double speed = Math.Sqrt(_pullVelocityX * _pullVelocityX + _pullVelocityY * _pullVelocityY);
                if (speed > MaxSpeed)
                {
                    double scale = MaxSpeed / speed;
                    _pullVelocityX *= scale;
                    _pullVelocityY *= scale;
                }
                _pullX += _pullVelocityX * dt;
                _pullY += _pullVelocityY * dt;
                _lampAngularVelocity += (-_lampAngle * 25.0 - _lampAngularVelocity * 7.0) * dt;
                _lampAngle += _lampAngularVelocity * dt;
                if (Math.Abs(_pullX) < 0.05 && Math.Abs(_pullY) < 0.05 && Math.Abs(_pullVelocityX) < 0.1 && Math.Abs(_pullVelocityY) < 0.1 && Math.Abs(_lampAngle) < 0.05 && Math.Abs(_lampAngularVelocity) < 0.1)
                {
                    _pullX = 0;
                    _pullY = 0;
                    _pullVelocityX = 0;
                    _pullVelocityY = 0;
                    _lampAngle = 0;
                    _lampAngularVelocity = 0;
                    UpdateCordVisual();
                    ApplyLampTransform();
                    StopPhysics();
                    return;
                }
            }

            SimulateCord(dt);
            UpdateCordVisual();
            ApplyLampTransform();
        }

        private double SimulateCord(double dt)
        {
            if (!_cordInitialized)
                InitializeCordRope();
            int last = CordPointCount - 1;
            _cordPoints[0] = new Point(CordTopX, CordTopY);
            _cordPoints[last] = new Point(CordTopX + _pullX, RestPullY + _pullY);

            for (int i = 1; i < last; i++)
            {
                Point current = _cordPoints[i];
                Point previous = _cordPointsPrev[i];
                Vector velocity = (current - previous) * CordVerletDamping;
                _cordPointsPrev[i] = current;
                _cordPoints[i] = current + velocity + new Vector(0, CordGravity) * dt * dt;
            }

            for (int iteration = 0; iteration < CordConstraintIterations; iteration++)
            {
                _cordPoints[0] = new Point(CordTopX, CordTopY);
                _cordPoints[last] = new Point(CordTopX + _pullX, RestPullY + _pullY);
                for (int i = 0; i < last; i++)
                {
                    Point a = _cordPoints[i];
                    Point b = _cordPoints[i + 1];
                    Vector delta = b - a;
                    double distance = delta.Length;
                    if (distance < 0.0001)
                        continue;
                    double error = (distance - _cordSegmentLength) / distance;
                    bool aFixed = i == 0;
                    bool bFixed = i + 1 == last;
                    if (aFixed && bFixed)
                        continue;
                    if (aFixed)
                        _cordPoints[i + 1] = new Point(b.X - delta.X * error, b.Y - delta.Y * error);
                    else if (bFixed)
                        _cordPoints[i] = new Point(a.X + delta.X * error, a.Y + delta.Y * error);
                    else
                    {
                        _cordPoints[i] = new Point(a.X + delta.X * error * 0.5, a.Y + delta.Y * error * 0.5);
                        _cordPoints[i + 1] = new Point(b.X - delta.X * error * 0.5, b.Y - delta.Y * error * 0.5);
                    }
                }
            }
            double totalSpeed = 0;
            for (int i = 1; i < last; i++)
            {
                Vector velocity = _cordPoints[i] - _cordPointsPrev[i];
                totalSpeed += velocity.Length / Math.Max(dt, 0.0001);
            }
            return totalSpeed;
        }

        private void UpdateCordVisual()
        {
            EnsureCordGeometry();
            EnsurePullTransform();
            if (!_cordInitialized)
                InitializeCordRope();
            BuildCordFigure();
            _pullTranslate!.X = _pullX;
            _pullTranslate.Y = _pullY;
        }

        private void BuildCordFigure()
        {
            int last = CordPointCount - 1;
            _cordFigure!.StartPoint = _cordPoints[0];
            for (int i = 0; i < last; i++)
            {
                Point p0 = _cordPoints[Math.Max(i - 1, 0)];
                Point p1 = _cordPoints[i];
                Point p2 = _cordPoints[i + 1];
                Point p3 = _cordPoints[Math.Min(i + 2, last)];
                Point control1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                Point control2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                var segment = (BezierSegment)_cordFigure.Segments[i];
                segment.Point1 = control1;
                segment.Point2 = control2;
                segment.Point3 = p2;
            }
        }

        private void ToggleLamp()
        {
            _lampOn = !_lampOn;
            ApplyLampVisuals(_lampOn, true);
        }

        private void ApplyLampVisuals(bool on, bool animate)
        {
            Color bulbColor = on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(245, 240, 230);
            Color shadeColor = on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(245, 240, 230);
            Color reflectorColor = on ? Color.FromRgb(255, 219, 138) : Color.FromRgb(45, 47, 53);
            Color cordColor = Color.FromRgb(85, 85, 85);
            Color pullColor = Color.FromRgb(212, 163, 115);
            AnimateBrushColor(LampBulb, Shape.FillProperty, bulbColor, 280, animate);
            AnimateBrushColor(LampShadeGlow, Shape.StrokeProperty, shadeColor, 280, animate);
            AnimateBrushColor(LampReflector, Shape.FillProperty, reflectorColor, 280, animate);
            AnimateBrushColor(PullCord, Shape.StrokeProperty, cordColor, 280, animate);
            AnimateBrushColor(LampPull, Shape.FillProperty, pullColor, 280, animate);
            AnimateOpacity(LampGlow, on ? 1.0 : 0.0, 480, animate);
            AnimateOpacity(LampBulbGlow, on ? 0.65 : 0.02, 320, animate);
            AnimateOpacity(LampReflector, on ? 0.55 : 0.0, 320, animate);
            AnimateOpacity(WarmLightOverlay, on ? 0.78 : 0.0, 720, animate);
            AnimateOpacity(LoginCard, on ? 1.0 : 0.0, 700, animate);
            AnimateOpacity(WelcomeText, on ? 1.0 : 0.0, 420, animate);
            AnimateOpacity(WelcomeSubText, on ? 0.0 : 1.0, 420, animate);
            LoginCard.IsHitTestVisible = on;
            AnimateBrushColor(LoginCard, Border.BackgroundProperty, on ? Color.FromRgb(28, 31, 36) : Color.FromRgb(13, 15, 19), 420, animate);
            AnimateBrushColor(LoginCard, Border.BorderBrushProperty, on ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255), 420, animate);
            LoginCardShadow.Color = on ? Color.FromRgb(255, 214, 110) : Color.FromRgb(0, 0, 0);
            LoginCardShadow.Opacity = on ? 0.35 : 0.30;
            SignInTitle.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            SignInSubtitle.Foreground = new SolidColorBrush(on ? Color.FromRgb(153, 153, 153) : Color.FromRgb(119, 119, 119));
            WelcomeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            WelcomeSubText.Foreground = new SolidColorBrush(Color.FromRgb(141, 146, 153));
        }

        private static void AnimateBrushColor(DependencyObject target, DependencyProperty property, Color color, int milliseconds, bool animate)
        {
            if (target.GetValue(property) is SolidColorBrush current)
            {
                if (animate)
                {
                    var animation = new ColorAnimation { To = color, Duration = TimeSpan.FromMilliseconds(milliseconds), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    current.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                }
                else
                {
                    current.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    current.Color = color;
                }
            }
            else
                target.SetValue(property, new SolidColorBrush(color));
        }

        private static void AnimateOpacity(UIElement element, double target, int milliseconds, bool animate)
        {
            if (!animate)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = target;
                return;
            }
            var animation = new DoubleAnimation { To = target, Duration = TimeSpan.FromMilliseconds(milliseconds), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void ApplyLampTransform()
        {
            LampRotateTransform.Angle = _lampAngle;
        }

        private void ApplyLiquidGlassHover(Control control)
        {
            if (control.RenderTransform is not ScaleTransform)
            {
                control.RenderTransform = new ScaleTransform(1.0, 1.0);
                control.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            control.MouseEnter += (_, _) => AnimateGlassHover(control, hovering: true);
            control.MouseLeave += (_, _) => AnimateGlassHover(control, hovering: false);
            control.PreviewMouseLeftButtonDown += (_, _) => AnimateGlassPress(control, pressed: true);
            control.PreviewMouseLeftButtonUp += (_, _) => AnimateGlassPress(control, pressed: false);
        }

        private static void AnimateGlassHover(Control control, bool hovering)
        {
            if (control.RenderTransform is not ScaleTransform scale)
                return;
            var scaleAnimation = new DoubleAnimation { To = hovering ? GlassHoverScale : 1.0, Duration = TimeSpan.FromMilliseconds(GlassHoverMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        private static void AnimateGlassPress(Control control, bool pressed)
        {
            if (control.RenderTransform is not ScaleTransform scale)
                return;
            var animation = new DoubleAnimation { To = pressed ? GlassPressScale : GlassHoverScale, Duration = TimeSpan.FromMilliseconds(pressed ? GlassPressMs : GlassReleaseMs), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string userId = UserIdTextBox.Text.Trim();
            string password = PasswordBox.Password;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please enter your Username or Phone Number and Password.", "Login", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var lifetimeToken = _viewCts;
            try
            {
                LoginButton.IsEnabled = false;
                var request = new LoginRequest { Id = userId, Password = password };
                var result = await _apiService.PostAsync<LoginRequest, LoginResponse>("api/User/login", request);
                if (lifetimeToken is null || lifetimeToken.IsCancellationRequested)
                    return;
                if (result == null || string.IsNullOrWhiteSpace(result.Token))
                {
                    MessageBox.Show("Invalid Username/Phone Number or Password.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AuthState.Set(
                    result.Token,
                    result.User.Id,
                    result.User.Username,
                    result.User.DisplayName,
                    result.User.Email);

                // Ask the server whether this authenticated account has OwnerOnly access.
                // This keeps the client in sync with Owner:Username from appsettings.json and
                // avoids hard-coding a specific Owner username in the WPF application.
                var isOwner = false;
                try
                {
                    await _apiService.GetAsync<OwnerAccessResponse>("api/Admin/test");
                    isOwner = true;
                }
                catch (HttpRequestException)
                {
                    isOwner = false;
                }

                if (isOwner)
                    OwnerLoginSuccessful?.Invoke();
                else
                    LoginSuccessful?.Invoke();
            }
            catch (Exception ex)
            {
                if (lifetimeToken is null || lifetimeToken.IsCancellationRequested)
                    return;
                MessageBox.Show($"Could not connect to the server.\n\n{ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (lifetimeToken is { IsCancellationRequested: false })
                    LoginButton.IsEnabled = true;
            }
        }

        private sealed class OwnerAccessResponse
        {
            public string? Message { get; set; }
        }

        private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAccountRequested?.Invoke();
        }
    }
}