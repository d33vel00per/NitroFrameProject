using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NitroFrame.ViewModels;
using NitroFrame.Backend;
using NitroFrame.Views;

namespace NitroFrame;

public partial class MainWindow : Window
{
    private readonly AppController _app = new();

    private static readonly string[] NavKeys = ["home", "boost", "game", "tools", "settings", "performance", "about", "admin"];
    private const double NavStride = 46;

    private bool _closing;
    private bool _sidebarCollapsed;
    private bool _subPromptShown;
    // Трей: иконка всегда на месте, крестик сворачивает в трей — автопилот
    // и защита продолжают работать. Полный выход — только из меню трея.
    private TrayIcon? _tray;
    private ContextMenu? _trayMenu;
    private bool _trayHintShown;
    private bool _forcedExit;
    // Окно ника показываем один раз за запуск: пропустил — спросим при
    // следующем старте, пока ник не задан.
    private bool _nicknamePromptShown;

    public MainWindow()
    {
        // Автоматизация скриншотов (NITROFRAME_OFFSCREEN=1): окно рождается
        // за экраном и никогда не активируется — можно снимать страницы
        // через PrintWindow/UIA, не мешая работающему на машине человеку.
        if (Environment.GetEnvironmentVariable("NITROFRAME_OFFSCREEN") == "1")
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000;
            Top = -4000;
            ShowInTaskbar = false;
            ShowActivated = false;
        }

        StartupTrace.Mark("MainWindow: начало конструктора (AppController готов)");
        InitializeComponent();
        StartupTrace.Mark("MainWindow: InitializeComponent (XAML всех страниц)");
        DataContext = _app;

        _app.Notify += (source, text) =>
        {
            // Референс «Dynamic Island»: жирный заголовок + приглушённая
            // подпись. Первый элемент пары — название раздела (NitroFrame,
            // Настройки…), второй — сообщение.
            ToastBar.Show(source, text);
            NotifyPopup.Visibility = Visibility.Collapsed;
        };
        _app.CrashRecovery += OnCrashRecovery;
        _app.DriftDetected += OnDriftDetected;
        _app.RebootRequested += OnRebootRequested;
        _app.PropertyChanged += OnAppPropertyChanged;
        _app.Licensing.LoggedOut += OnLoggedOut;
        _app.Licensing.PaymentDialogRequested += OnPaymentDialogRequested;
        _app.Licensing.PaymentCreated += (_, _, url) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            });
        };
        _app.NeedsSubscription += OnNeedsSubscription;

        // Одно окно подтверждения обслуживает два противоположных вопроса (откатить /
        // вернуть на место), поэтому действие выбирается по тому, что именно
        // спрашивали. Флаг сбрасывается сразу: повторное Accepted без нового
        // вопроса не должно ничего менять в системе.
        ConfirmPopup.Accepted += () =>
        {
            var action = _confirmAction;
            _confirmAction = ConfirmAction.None;
            switch (action)
            {
                case ConfirmAction.RestorePending:
                    _app.AcceptCrashRecoveryCommand.Execute(null);
                    break;
                case ConfirmAction.RepairDrift:
                    _app.AcceptDriftRepairCommand.Execute(null);
                    break;
                case ConfirmAction.Reboot:
                    _app.AcceptRebootCommand.Execute(null);
                    break;
            }
        };
        ConfirmPopup.Dismissed += () => _confirmAction = ConfirmAction.None;
        Root.SizeChanged += (_, _) => LayoutBackground();

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => _tray?.Dispose();
        InitTray();
        // Первый кадр и конец отрисовки: между Loaded и появлением окна уходит
        // основное время запуска, и без этих отметок не видно, что именно там
        // происходит — разметка, растризация шрифтов или первая композиция.
        // Замер запуска — только по NITROFRAME_TRACE=1. Подписка на Rendering и таймер
        // пульса работают всю жизнь процесса и в обычном запуске не нужны.
        if (StartupTrace.Enabled)
        {
            SourceInitialized += (_, _) => StartupTrace.Mark("  SourceInitialized (HWND создан)");
            LayoutUpdated += OnFirstLayoutUpdated;
            CompositionTarget.Rendering += OnFirstRendering;
            ContentRendered += (_, _) => StartupTrace.Mark("MainWindow: ContentRendered (первый кадр)");
            StartHeartbeat();
        }

        StartupTrace.Mark("MainWindow: конструктор готов");
    }

    // Пульс потока интерфейса во время запуска.
    //
    // Различает две разные причины одного симптома «окно долго не появляется»:
    // если тики продолжаются, поток свободен и ждём мы чего-то снаружи
    // (DWM, драйвер, ответ клиенту доступности); если обрываются — поток занят
    // или заблокирован, и виноват наш код. Именно эта разница показала, что
    // пятисекундная пауза шла внутри Window.Show(), а не в наших обработчиках.
    private void StartHeartbeat()
    {
        var beat = 0;
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += (_, _) =>
        {
            beat++;
            StartupTrace.Mark(string.Format(Loc.T("    пульс потока #{0}"), beat));
            if (beat >= 60) timer.Stop();
        };
        timer.Start();
    }

    // Первая разметка и первые кадры — только для замера запуска.
    // Обработчики снимаются сразу: подписка на Rendering живёт до конца работы
    // программы и вызывается каждый кадр, то есть 60 раз в секунду впустую.
    private void OnFirstLayoutUpdated(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnFirstLayoutUpdated;
        StartupTrace.Mark("  первая разметка построена (LayoutUpdated)");
    }

    private int _tracedFrames;

    private void OnFirstRendering(object? sender, EventArgs e)
    {
        // Первые кадры по одному: если запуск тормозит один тяжёлый кадр, это
        // видно как большой разрыв между двумя соседними отметками, а если
        // тормозит поток кадров (анимация появления) — как ровная череда.
        _tracedFrames++;
        StartupTrace.Mark(string.Format(Loc.T("  кадр композиции #{0}"), _tracedFrames));
        if (_tracedFrames >= 12) CompositionTarget.Rendering -= OnFirstRendering;
    }

    private void OnPaymentDialogRequested(PaymentIntent intent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnPaymentDialogRequested(intent));
            return;
        }

        try
        {
            var dialog = new PaymentDialog(intent, _app.Licensing.License,
                onCancel: _app.Licensing.CancelPendingPayment)
            {
                Owner = this,
                ShowInTaskbar = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            dialog.Show();
            dialog.Activate();
            dialog.Focus();
            _app.Licensing.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), Loc.T("Ошибка окна оплаты"), MessageBoxButton.OK, MessageBoxImage.Hand);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartupTrace.Mark("MainWindow: Loaded (разметка построена)");
        LayoutBackground();
        StartupTrace.Mark("  LayoutBackground");

        // Скрытый слот сообщества в навигации не показываем: кнопка живёт
        // на странице «Игра», пилюля подсвечивает только реальные разделы.
        CommunityNavButton.Visibility = Visibility.Collapsed;
        NavPillHost.Visibility = Visibility.Visible;
        NavPillHost.Opacity = 1;
        PillOffset.Y = NavIndex() * NavStride;

        PlayEntrance();
        StartupTrace.Mark("  PlayEntrance");
        _app.Start();
        StartupTrace.Mark("  AppController.Start()");
        ApplyAdminVisibility();
        StartupTrace.Mark("  ApplyAdminVisibility");
        CheckForUpdatesAsync();
        StartupTrace.Mark("  CheckForUpdatesAsync");
        RunFirstLaunchSetup();
        StartupTrace.Mark("  RunFirstLaunchSetup");
        CheckBootTweaksOnStartup();
        StartupTrace.Mark("  CheckBootTweaksOnStartup");
        ShowNicknamePromptIfNeeded();
        StartupTrace.Mark("MainWindow: Loaded завершён");
    }

    private void PlayEntrance()
    {
        if (_app.Settings.ReduceAnimations)
        {
            TitleBarTr.Y = 0;
            SidebarTr.X = 0;
            ContentTr.Y = 0;
            BrandPop.ScaleX = BrandPop.ScaleY = 1;
            EntranceScale.ScaleX = EntranceScale.ScaleY = 1;
            Root.Opacity = 1;
            return;
        }

        Root.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, 320));
        EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, Slide(0.98, 1, 420));
        EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, Slide(0.98, 1, 420));

        TitleBar.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, 320, 100));
        TitleBarTr.BeginAnimation(TranslateTransform.YProperty, Slide(-14, 0, 380, 100));

        Sidebar.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, 320, 80));
        SidebarTr.BeginAnimation(TranslateTransform.XProperty, Slide(-42, 0, 440, 80, spring: true));

        Brand.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, 300, 140));
        BrandPop.BeginAnimation(ScaleTransform.ScaleXProperty, Slide(0.88, 1, 420, 140, spring: true));
        BrandPop.BeginAnimation(ScaleTransform.ScaleYProperty, Slide(0.88, 1, 420, 140, spring: true));

        ContentHost.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, 320, 180));
        ContentTr.BeginAnimation(TranslateTransform.YProperty, Slide(22, 0, 400, 180));

        static DoubleAnimation Fade(int from, int to, int ms, int begin = 0) =>
            new(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(begin) };

        static DoubleAnimation Slide(double from, double to, int ms, int begin = 0, bool spring = false)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(begin),
                EasingFunction = spring
                    ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
                    : new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            return anim;
        }
    }

    // Фон сейчас — статичный градиент в XAML; метод оставлен как точка
    // расширения под адаптивную подложку.
    private void LayoutBackground()
    {
        var w = Root.ActualWidth;
        var h = Root.ActualHeight;
        if (w <= 0) return;
        _ = h;
    }

    private async void CheckForUpdatesAsync()
    {
        // На старте (App.OnStartup) проверка уже была. Если плашка там показалась
        // и её отложили — второй раз за один запуск не лезем. Сетевой запрос
        // повторяем только если на старте он не успел — медленная сеть была
        // главной причиной «плашка появилась не у всех».
        if (UpdateService.DeclinedThisSession) return;
        var info = UpdateService.Pending ?? await UpdateService.CheckAsync();
        if (info is null) return;

        if (UpdateWindow.Prompt(info, this))
        {
            Application.Current.Shutdown();
            return;
        }

        UpdateService.MarkDeclined();

        if (info.Mandatory)
        {
            // Отказ от обязательного обновления = выход: сервер всё равно
            // ответит 426 на любое действие, а молча нерабочая программа хуже
            // понятного закрытия.
            Application.Current.Shutdown();
        }
    }

    // После заставки — тихий автопоиск установленных лаунчеров. Никаких
    // вопросов на старте: платформа по умолчанию Majestic, меняется в
    // «Настройках» (радиокнопки Majestic / GTA5RP).
    private void RunFirstLaunchSetup()
    {
        _app.Settings.AutodetectLaunchersCommand.Execute(null);
    }

    // Проверяет статус загрузочных твиков при каждом запуске.
    // Если они ждут перезагрузки (записаны, но полная загрузка Windows не была) —
    // показываем тост-напоминание. Пока твики не активированы, напоминаем каждый раз.
    private void CheckBootTweaksOnStartup()
    {
        if (!_app.Optimizer.BootRebootPending)
            return; // твики уже активны или не применялись — молчим

        _app.ShowToast(
            "NitroFrame",
            Loc.T("Перезагрузи систему один раз для полной оптимизации — часть твиков буста вступят в силу после reboot.")
        );
    }

    private int NavIndex()
    {
        var i = Array.IndexOf(NavKeys, _app.Page);
        return i < 0 ? 0 : i;
    }

    private void ApplyAdminVisibility()
    {
        var isAdmin = _app.Licensing.IsAdmin;
        AdminNavButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!isAdmin && string.Equals(_app.Page, "admin", StringComparison.OrdinalIgnoreCase))
            _app.Page = "home";
    }

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Page") return;

        ApplyAdminVisibility();
        CommunityNavButton.Visibility = Visibility.Collapsed;
        NavPillHost.Visibility = Visibility.Visible;
        PillOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
            NavIndex() * NavStride, new Duration(TimeSpan.FromMilliseconds(240)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void OnSidebarToggle(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        Sidebar.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ContentHost.Margin = _sidebarCollapsed
            ? new Thickness(84, 16, 16, 16)
            : new Thickness(260, 16, 16, 16);
    }

    // Выход из сессии — обратно на окно входа; оттуда же и закрываемся.
    private void OnLoggedOut()
    {
        Hide();
        if (new LoginWindow().ShowDialog() == true)
        {
            _app.Licensing.ReloadSession();
            Show();
            Activate();
            ShowNicknamePromptIfNeeded();
        }
        else
        {
            Close();
        }
    }

    private void OnSupportClick(object sender, RoutedEventArgs e)
    {
        // Поддержка — Telegram-бот @nitfrofamelog_bot: там кнопка «Поддержка»,
        // тикет уходит команде прямо в лс.
        try { Process.Start(new ProcessStartInfo("https://t.me/nitfrofamelog_bot") { UseShellExecute = true }); } catch { }
    }

    // Никнейм ещё не задан — предлагаем придумать прямо сейчас, в главном
    // окне. Один раз за запуск; выбор одноразовый, окно об этом предупреждает.
    private async void ShowNicknamePromptIfNeeded()
    {
        if (_nicknamePromptShown) return;
        if (!_app.Licensing.IsLoggedIn || _app.Licensing.HasNickname) return;
        _nicknamePromptShown = true;

        // Сначала дозапрашиваем ник с сервера: сессии старых сборок его
        // теряли, а без этого предложили бы второй ник поверх существующего —
        // сервер бы ответил «уже задан».
        try { await _app.Licensing.TryRestoreNicknameAsync(); } catch { }
        _app.Licensing.Refresh();
        if (_app.Licensing.HasNickname) return;

        try
        {
            new NicknamePromptWindow { Owner = this }.ShowDialog();
        }
        catch { /* окно не открылось — спросим при следующем запуске */ }
        finally
        {
            // Сессию перечитываем в любом исходе: менеджер окна мог сохранить
            // ник на диск, а «кем вошёл» должно обновиться сразу.
            _app.Licensing.ReloadSession();
        }
    }

    private void OnNeedsSubscription()
    {
        if (_subPromptShown) return;
        _subPromptShown = true;

        // Один раз за всё время: не показываем, если уже показывали
        // в прошлый запуск (даже если пользователь тогда пропустил).
        if (_app.Settings.SubscriptionPromptShown) return;
        _app.Settings.SubscriptionPromptShown = true;

        var prompt = new SubscriptionPromptWindow();
        if (prompt.ShowDialog() == true)
        {
            // Ключ активирован или оплата подтверждена — обновляем состояние.
            _app.Licensing.ReloadSession();
        }
    }

    // Прошлая сессия завершилась аварийно — предлагаем откатить твики.
    private void OnCrashRecovery(string info)
    {
        _confirmAction = ConfirmAction.RestorePending;
        ConfirmPopup.OpenWith(Loc.T("Восстановление после сбоя"),
            Loc.T("Похоже, прошлая сессия завершилась некорректно и изменения ещё не были откачены:\n\n") +
            info + Loc.T("\n\nВосстановить исходные настройки сейчас?"),
            Loc.T("Восстановить"), Loc.T("Позже"));
    }

    // Снапшот есть, а части твиков в реестре уже нет: их сняли извне.
    // Здесь вопрос обратный к восстановлению после сбоя — не «откатить?»,
    // а «вернуть на место?», поэтому и команда другая.
    private void OnDriftDetected(string info)
    {
        _confirmAction = ConfirmAction.RepairDrift;
        ConfirmPopup.OpenWith(Loc.T("Настройки сняты извне"),
            Loc.T("Часть настроек NitroFrame больше не действует — похоже, их изменила другая программа или обновление Windows.\n\n") +
            info + Loc.T("\n\nПрименить их заново?"),
            Loc.T("Применить заново"), Loc.T("Позже"));
    }

    // Постоянные настройки записаны: действовать они начнут после перезагрузки.
    //
    // Предложение, а не действие: перезагрузка без спроса — самый быстрый
    // способ потерять доверие и несохранённую работу человека.
    private void OnRebootRequested(string info)
    {
        _confirmAction = ConfirmAction.Reboot;
        ConfirmPopup.OpenWith(Loc.T("Нужна перезагрузка"), info, Loc.T("Перезагрузить"), Loc.T("Позже"));
    }

    // Чего ждёт открытое окно подтверждения. Одно окно на три разных вопроса,
    // и без этого флага «Применить заново» вызвало бы откат — то есть ровно
    // противоположное тому, о чём просили.
    private enum ConfirmAction { None, RestorePending, RepairDrift, Reboot }

    private ConfirmAction _confirmAction = ConfirmAction.None;

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_app.Settings.MinimizeToTray)
            WindowState = WindowState.Minimized;
        else
            Close();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e) => OnTitleDrag(sender, e);

    private void OnCloseClick(object sender, RoutedEventArgs e) => OnClose(sender, e);

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        // Крестик ≠ выход: программа живёт в трее (автопилот, защита,
        // хоткеи). Первый раз объясняем тостом и даём его дочитать —
        // тост рисуется ВНУТРИ окна, прятать его сразу бессмысленно.
        if (!_forcedExit)
        {
            e.Cancel = true;
            var first = !_trayHintShown;
            _trayHintShown = true;
            if (first)
            {
                _app.ShowToast("NitroFrame",
                    Loc.T("Свернул в трей — оптимизация и защита продолжают работать. ") +
                    Loc.T("Полный выход — правый клик по иконке трея."));
                await Task.Delay(3800);
                if (_forcedExit || _closing) return; // успели выйти из меню
            }
            Hide();
            return;
        }
        _closing = true;
        _app.Shutdown();
    }

    // ------------------------------------------------------------------
    // Трей: иконка + быстрое меню (профили, буст, очистка, выход)
    // ------------------------------------------------------------------

    private void InitTray()
    {
        _tray = new TrayIcon("NitroFrame");
        _tray.LeftClick = ShowFromTray;
        _tray.RightClick = point => OpenTrayMenu(point);
        UpdateTrayTooltip();
        _app.Profiles.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_app.Profiles.SelectedKey))
            {
                UpdateTrayTooltip();
            }
        };
    }

    private void UpdateTrayTooltip()
    {
        try
        {
            _tray?.SetTooltip(string.Format(Loc.T("NitroFrame · {0} активен"), _app.Profiles.Selected.Short));
        }
        catch (Exception)
        {
            // Профиль мог ещё не прочитаться при старте — не падаем, тултип
            // обновится при первом переключении.
        }
    }

    private void ShowFromTray()
    {
        // Диспетчер обязателен: Shell_NotifyIcon шлёт сообщения в
        // message-only окно, а трогаем мы элементы интерфейса.
        Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Show();
            Topmost = true; // подняться над игрой, потом отдать фокус обратно
            Topmost = false;
            Activate();
        });
    }

    private void OpenTrayMenu(TrayIcon.CursorPoint cursor)
    {
        Dispatcher.Invoke(() =>
        {
            CloseTrayBackdrop();
            _trayMenu = BuildTrayMenu();
            if (TryFindResource("TrayMenu") is Style menuStyle)
                _trayMenu.Style = menuStyle;
            _trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            _trayMenu.HorizontalOffset = cursor.X;
            _trayMenu.VerticalOffset = cursor.Y;
            // Клик в пустое место уходит в чужое окно (браузер и т.д.),
            // WPF-меню его не видит и не закрывается. Стандартный фикс —
            // прозрачное окно-подложка на весь экран: оно ловит клик вне
            // меню, закрывает меню и само исчезает.
            _trayMenu.Closed += TrayMenuClosed;
            // ВАЖНО: чисто прозрачный фон (альфа 0) windows прокалывает
            // кликами насквозь — hit-test layered window идёт по альфе.
            // Поэтому фон почти невидимый (альфа 1 из 255): глазу не видно,
            // а клики ловятся.
            var backdropBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            _trayBackdrop = new Window
            {
                AllowsTransparency = true,
                Background = backdropBrush,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Content = new Grid { Background = backdropBrush },
            };
            _trayBackdrop.PreviewMouseDown += TrayBackdropClick;
            _trayBackdrop.Show();
            _trayMenu.IsOpen = true;
        });
    }

    private Window? _trayBackdrop;

    private void TrayBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (_trayMenu != null) _trayMenu.IsOpen = false;
        CloseTrayBackdrop();
        e.Handled = true;
    }

    private void TrayMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (_trayMenu != null) _trayMenu.Closed -= TrayMenuClosed;
        CloseTrayBackdrop();
    }

    private void CloseTrayBackdrop()
    {
        if (_trayBackdrop == null) return;
        _trayBackdrop.PreviewMouseDown -= TrayBackdropClick;
        _trayBackdrop.Close();
        _trayBackdrop = null;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu { StaysOpen = false };
        // ItemContainerStyle НЕ задаем: он назначает стиль MenuItem и
        // Separator'ам, а Separator с чужим стилем роняет создание попапа
        // (крэш-лог: «Стиль, заданный для типа "MenuItem", не может
        // применяться к типу "Separator"»). Стили — явно каждому пункту.
        var itemStyle = TryFindResource("TrayMenuItem") as Style;
        var separatorStyle = TryFindResource("TrayMenuSeparator") as Style;
        if (TryFindResource("TrayMenuHeader") is Style headerStyle)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "NitroFrame",
                Style = headerStyle,
            });
        }

        // Профили: точка цвета профиля, галка у активного. Клик — та же
        // команда, что жмёт плитка на главной.
        foreach (var tile in _app.Profiles.Tiles)
        {
            var item = new MenuItem
            {
                Header = tile.Profile.Short,
                Icon = new Ellipse
                {
                    Width = 9, Height = 9,
                    Fill = tile.Profile.BrushTop,
                    Margin = new Thickness(0, 0, 8, 0),
                },
                IsChecked = tile.IsSelected,
                Style = itemStyle,
            };
            var key = tile.Profile.Key;
            item.Click += (_, _) => _app.Profiles.SelectCommand.Execute(key);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator { Style = separatorStyle });
        menu.Items.Add(MakeItem(Loc.T("Запустить оптимизацию"), _app.Optimizer.ToggleCommand, itemStyle));
        menu.Items.Add(MakeItem(Loc.T("Очистить TEMP"), _app.Maintenance.CleanTempCommand, itemStyle));
        menu.Items.Add(new Separator { Style = separatorStyle });
        var open = new MenuItem { Header = Loc.T("Открыть NitroFrame"), Style = itemStyle };
        open.Click += (_, _) => ShowFromTray();
        menu.Items.Add(open);
        var exit = new MenuItem { Header = Loc.T("Выход"), Style = itemStyle };
        exit.Click += (_, _) =>
        {
            _forcedExit = true;
            Close();
        };
        menu.Items.Add(exit);
        return menu;
    }

    private MenuItem MakeItem(string header, ICommand command, Style? itemStyle)
    {
        var item = new MenuItem { Header = header, Command = command, Style = itemStyle };
        return item;
    }
}