using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using NitroFrame.Backend;

namespace NitroFrame;

// Окно обязательного входа. Показывается до главного окна (первый запуск,
// после выхода из аккаунта) — программа без входа не открывается.
//
// DialogResult: true — вход выполнен и сессия сохранена, false — пользователь
// отказался, приложение завершается.
public partial class LoginWindow : Window
{
    // Win32: убираем системный caption-ремнант у безрамочного окна.
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // --- язык интерфейса (переключатель в шапке окна входа) ---

    private void OnLanguageRu(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SwitchLanguage("ru");

    private void OnLanguageEn(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SwitchLanguage("en");

    // Смена языка: память (все {loc:Loc}-биндинги перечитываются) + настройки
    // на диск. Свой короткоживущий SettingsStore: у окна входа нет доступа к
    // стору контроллера, а держать второй постоянный — гонка записей.
    private void SwitchLanguage(string lang)
    {
        if (Loc.Language == lang) return;
        Loc.Set(lang);
        SaveLanguage(lang);
        UpdateLangSwitch();
    }

    private void UpdateLangSwitch() => LangSwitch.Tag = Loc.Language;

    private static void SaveLanguage(string lang)
    {
        try
        {
            var store = new SettingsStore();
            store.Load();
            store.Data.Language = lang;
            store.Save();
        }
        catch (Exception exc) when (exc is System.IO.IOException or UnauthorizedAccessException)
        {
            // Язык не критичен: несохранился — просто сбросится после перезапуска.
        }
    }

    // Водяные знаки живут в Tag и переводятся конвертером при отрисовке;
    // при смене языка на лету обновляем их вручную.
    private void RefreshWatermarks()
    {
        PasswordInput.Tag = Loc.T("Пароль");
        ForgotNewPasswordBox.Tag = Loc.T("Новый пароль");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, style & ~WS_CAPTION);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER);
    }

    // Свой экземпляр, а не общий с AppController: окно живёт до главного
    // окна. Сессию оба читают из одного DPAPI-файла, поэтому после входа
    // AppController увидит аккаунт при своей загрузке (или через Reload).
    private readonly LicenseManager _license = new();

    // Опрос подтверждения Telegram. Таймер, а не цикл с задержкой: живёт в
    // потоке интерфейса, и его безопасно глушить из любого обработчика.
    private DispatcherTimer? _tgTimer;

    // Ждём код из письма для ПОДТВЕРЖДЕНИЯ РЕГИСТРАЦИИ (а не 2FA при входе):
    // после кода LicenseManager сам выполнит вход — DialogResult закроет окно.
    private bool _emailCodePending;
    private string? _tgCode;
    private DateTimeOffset _tgDeadline;
    private bool _tgBusy;
    // Ссылка текущей попытки входа (https://t.me/…): нужна кнопке «Скопировать
    // ссылку», когда ни приложение на компьютере, ни телефон не сработали.
    private string? _tgUrl;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus();
        Closed += (_, _) => StopTelegramPoll();

        // Язык интерфейса: подписка нужна только пока живо окно (иначе
        // статическое событие держало бы закрытое окно до выхода из программы).
        Loc.Changed += RefreshWatermarks;
        Closed += (_, _) => Loc.Changed -= RefreshWatermarks;
        RefreshWatermarks();
        UpdateLangSwitch();

        // Оффскрин-режим автотестов: окно входа тоже не должно мигать на экране
        // (лицензия может кикнуть сессию в любой момент — LoginWindow рождается внезапно).
        if (Environment.GetEnvironmentVariable("NITROFRAME_OFFSCREEN") == "1")
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000; Top = -4000;
            ShowInTaskbar = false;
            ShowActivated = false;

            // Дев-снимки для автотестов: NITROFRAME_SHOTS=префикс — окно само
            // рендерит своё визуальное дерево в PNG (RenderTargetBitmap работает
            // и за экраном, в отличие от PrintWindow для модального окна).
            var shotsPrefix = Environment.GetEnvironmentVariable("NITROFRAME_SHOTS");
            if (!string.IsNullOrEmpty(shotsPrefix))
            {
                async void ShotIn(double seconds, string tag)
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(seconds));
                    try
                    {
                        var w = (int)ActualWidth; var h = (int)ActualHeight;
                        if (w < 10 || h < 10) return;
                        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        rtb.Render(this);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                        using var fs = System.IO.File.Create(shotsPrefix + tag + ".png");
                        enc.Save(fs);
                    }
                    catch { /* дев-инструмент — молча */ }
                }
                ShotIn(3, "_t3");
                ShotIn(10, "_t10");
            }
        }
    }

    private async void OnLogin(object sender, RoutedEventArgs e) =>
        await SubmitAsync(register: false);


    private async void OnResendVerification(object sender, RoutedEventArgs e)
    {
        ResendVerificationButton.IsEnabled = false;
        ResendVerificationButton.Content = Loc.T("Отправка…");
        try
        {
            var (outcome, message) = await LicenseApi.ResendVerificationAsync(EmailBox.Text.Trim(), PasswordInput.Password);
            Show(message, outcome != ApiOutcome.Ok);
        }
        finally
        {
            ResendVerificationButton.IsEnabled = true;
            ResendVerificationButton.Content = Loc.T("Отправить письмо ещё раз");
        }
    }

    private async void OnRegister(object sender, RoutedEventArgs e)
    {
        await SubmitAsync(register: true);
    }

    private async void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SubmitAsync(register: false);
    }

    private async void OnTwoFactorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await VerifyTwoFactorFromUiAsync();
    }

    private async void OnTwoFactor(object sender, RoutedEventArgs e) =>
        await VerifyTwoFactorFromUiAsync();

    private async Task VerifyTwoFactorFromUiAsync()
    {
        TwoFactorButton.IsEnabled = false;
        try
        {
            var code = TwoFactorBox.Text.Trim();
            if (_emailCodePending)
            {
                // Завершение регистрации: код подтверждает почту, VerifyEmailAsync
                // сам выполняет вход — окно закрывается уже залогиненным.
                var (verified, message) = await _license.VerifyEmailAsync(code);
                if (verified)
                {
                    _emailCodePending = false;
                    ShowFirstLoginRebootNotice();
                    DialogResult = true;
                    return;
                }
                TwoFactorError.Text = message;
                TwoFactorError.Visibility = Visibility.Visible;
                return;
            }
            var (ok, msg) = await _license.VerifyTwoFactorAsync(TwoFactorBox.Text.Trim());
            if (ok)
            {
                ShowFirstLoginRebootNotice();
                DialogResult = true;
                return;
            }
            TwoFactorError.Text = msg;
            TwoFactorError.Visibility = Visibility.Visible;
        }
        finally
        {
            TwoFactorButton.IsEnabled = true;
        }
    }



    private async void OnForgotPassword(object sender, MouseButtonEventArgs e)
    {
        // Начатая попытка входа через Telegram больше не к месту: её QR
        // перекрывал бы форму сброса, а опрос продолжал бы есть лимит
        // частоты на сервере впустую.
        if (_tgTimer is not null) StopTelegramPoll();

        // Скрыть форму входа, показать панель сброса пароля
        // Панель сброса находится внутри Form, поэтому родитель нельзя скрывать.
        // Скрываем только элементы формы входа, оставляя ForgotPasswordPanel видимой.
        SetLoginFieldsVisibility(Visibility.Collapsed);
        ForgotPasswordPanel.Visibility = Visibility.Visible;
        ForgotEmailBox.Focus();
    }

    private async void OnForgotSendCode(object sender, RoutedEventArgs e)
    {
        ForgotError.Visibility = Visibility.Collapsed;
        var email = ForgotEmailBox.Text.Trim();
        if (string.IsNullOrEmpty(email))
        {
            ForgotError.Text = Loc.T("Введи почту.");
            ForgotError.Visibility = Visibility.Visible;
            return;
        }

        ForgotSendButton.IsEnabled = false;
        ForgotSendButton.Content = Loc.T("Отправка…");
        try
        {
            var (ok, message) = await _license.ForgotPasswordAsync(email);
            if (ok)
            {
                ForgotStepEmail.Visibility = Visibility.Collapsed;
                ForgotStepReset.Visibility = Visibility.Visible;
                ForgotCodeBox.Focus();
                ForgotError.Text = Loc.T("Код отправлен на почту. Проверь входящие.");
                ForgotError.Foreground = (Brush)FindResource("TextMutedBrush");
                ForgotError.Visibility = Visibility.Visible;
            }
            else
            {
                ForgotError.Text = message;
                ForgotError.Foreground = (Brush)FindResource("DangerBrush");
                ForgotError.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            ForgotSendButton.IsEnabled = true;
            ForgotSendButton.Content = Loc.T("Отправить код");
        }
    }

    private async void OnForgotResetPassword(object sender, RoutedEventArgs e)
    {
        ForgotError.Visibility = Visibility.Collapsed;
        var email = ForgotEmailBox.Text.Trim();
        var code = ForgotCodeBox.Text.Trim();
        var newPassword = ForgotNewPasswordBox.Password;

        if (string.IsNullOrEmpty(code))
        {
            ForgotError.Text = Loc.T("Введи код из письма.");
            ForgotError.Visibility = Visibility.Visible;
            return;
        }
        if (newPassword.Length < 8)
        {
            ForgotError.Text = Loc.T("Новый пароль должен содержать минимум 8 символов.");
            ForgotError.Visibility = Visibility.Visible;
            return;
        }

        ForgotResetButton.IsEnabled = false;
        ForgotResetButton.Content = Loc.T("Сброс…");
        try
        {
            var (ok, message) = await _license.ResetPasswordAsync(email, code, newPassword);
            if (ok)
            {
                // Вернуться ко входу с новым паролем
                OnBackToLogin(null, null);
                Show(Loc.T("Пароль изменён. Теперь войди с новым паролем."), error: false);
                PasswordInput.Focus();
            }
            else
            {
                ForgotError.Text = message;
                ForgotError.Foreground = (Brush)FindResource("DangerBrush");
                ForgotError.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            ForgotResetButton.IsEnabled = true;
            ForgotResetButton.Content = Loc.T("Сбросить пароль");
        }
    }

    private void OnBackToLogin(object? sender, MouseButtonEventArgs? e)
    {
        TwoFactorPanel.Visibility = Visibility.Collapsed;
        ForgotPasswordPanel.Visibility = Visibility.Collapsed;
        // Возврат ко входу отменяет ожидание кода регистрации: иначе после
        // «Назад» первый же код в панели 2FA трактовался бы как почтовый.
        _emailCodePending = false;
        ResendVerificationButton.Visibility = Visibility.Collapsed;
        // Возврат ко входу гасит и начатую попытку через Telegram: иначе поверх
        // восстановленной формы остался бы висеть QR старого кода.
        if (_tgTimer is not null) StopTelegramPoll();
        TelegramQrPanel.Visibility = Visibility.Collapsed;
        SetLoginFieldsVisibility(Visibility.Visible);
        Form.Visibility = Visibility.Visible;
        // Сбросить forgot password
        ForgotStepEmail.Visibility = Visibility.Visible;
        ForgotStepReset.Visibility = Visibility.Collapsed;
        ForgotError.Visibility = Visibility.Collapsed;
        TwoFactorError.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void SetLoginFieldsVisibility(Visibility visibility)
    {
        CardTitle.Visibility = visibility;
        CardSubtitle.Visibility = visibility;
        EmailLabel.Visibility = visibility;
        EmailBox.Visibility = visibility;
        PassLabel.Visibility = visibility;
        PassRow.Visibility = visibility;
        RememberRow.Visibility = visibility;
        ForgotLink.Visibility = visibility;
        DividerRow.Visibility = visibility;
        TgButton.Visibility = visibility;
        SignupRow.Visibility = visibility;
        if (visibility != Visibility.Visible)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnMinimize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ------------------------------------------------------------------ #
    // Вход через Telegram: сервер выдаёт одноразовую ссылку в бота, клиент
    // открывает её в установленном приложении и заодно показывает QR для
    // телефона, а окно раз в две секунды спрашивает, нажат ли «Старт».
    //
    // Почему не браузер, как было до 2.1.11. Ссылка t.me у части российских
    // провайдеров не открывается: TCP до адреса Telegram устанавливается, а
    // TLS с SNI t.me висит до таймаута. Человек видел пустую страницу и решал,
    // что вход сломан. Схема tg:// уходит в уже установленное приложение через
    // ShellExecute, минуя браузер и HTTP целиком, а QR открывает бота на
    // телефоне: приложение Telegram перехватывает ссылку t.me как app link, то
    // есть запроса к заблокированному хосту снова не происходит.
    // ------------------------------------------------------------------ #
    private async void OnTelegram(object sender, RoutedEventArgs e)
    {
        if (_tgTimer is not null) return; // опрос уже идёт

        SetTelegramBusy(true);
        try
        {
            var (ok, start, message) = await _license.TelegramBeginAsync();
            if (!ok || start is null)
            {
                Show(message, error: true);
                SetTelegramBusy(false);
                return;
            }

            // Порядок важен: сначала приложение, и только если его нет —
            // браузер. Обратный порядок возвращал бы исходную поломку.
            var opened = TryOpen(TelegramLink.AppUri(start.Url))
                         || TryOpen(start.Url);

            _tgUrl = start.Url;
            ShowTelegramQr(start.Url, opened);

            _tgCode = start.Code;
            _tgDeadline = start.ExpiresAt;
            _tgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _tgTimer.Tick += OnTelegramTick;
            _tgTimer.Start();
        }
        catch
        {
            SetTelegramBusy(false);
            throw;
        }
    }

    // true — оболочка приняла адрес. Незарегистрированная схема (нет Telegram)
    // и отсутствие браузера дают Win32Exception — это не ошибка, а сигнал
    // пробовать следующий способ.
    private static bool TryOpen(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception exc) when (exc is System.ComponentModel.Win32Exception
                                        or InvalidOperationException
                                        or System.IO.FileNotFoundException)
        {
            return false;
        }
    }

    // Панель с QR замещает форму входа, а не встаёт внутрь неё: так же
    // устроены шаг кода и сброс пароля. Иначе карточка не умещается в окно
    // и нижняя часть панели («Отмена») обрезается по краю.
    //
    // В код кладём ту же https-ссылку, а не tg://: телефон отдаёт t.me
    // установленному приложению как app link (Android App Links, iOS Universal
    // Links), а произвольную схему сканер камеры чаще всего молча игнорирует.
    private void ShowTelegramQr(string url, bool appOpened)
    {
        try
        {
            TelegramQrImage.Source = Views.QrCodeImage.Create(url, Brushes.Black, Brushes.White);
        }
        catch (Exception exc) when (exc is ArgumentException or InvalidOperationException)
        {
            // Код не построился (недопустимая ссылка) — панель всё равно нужна:
            // в ней остаётся кнопка «Скопировать ссылку».
            TelegramQrImage.Source = null;
        }

        SetLoginFieldsVisibility(Visibility.Collapsed);
        TelegramQrPanel.Visibility = Visibility.Visible;
        TelegramQrHint.Text = appOpened
            ? Loc.T("Ждём подтверждения…")
            : Loc.T("Telegram на этом компьютере не найден — отсканируй код телефоном.");
        ErrorText.Visibility = Visibility.Collapsed;
        // Предложение выслать письмо ещё раз относится к входу по почте — на
        // экране подтверждения Telegram оно только сбивает.
        ResendVerificationButton.Visibility = Visibility.Collapsed;
    }

    private void OnCopyTelegramLink(object sender, RoutedEventArgs e)
    {
        if (_tgUrl is not { Length: > 0 } url) return;
        try
        {
            Clipboard.SetText(url);
            TelegramQrHint.Text = Loc.T("Ссылка скопирована — открой её в Telegram.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Буфер обмена занят другим приложением — редкое, но штатное
            // состояние Windows. Ронять окно входа из-за этого нельзя.
            TelegramQrHint.Text = Loc.T("Не удалось скопировать — попробуй ещё раз.");
        }
    }

    private void OnCancelTelegram(object sender, MouseButtonEventArgs e) => StopTelegramPoll();

    private async void OnTelegramTick(object? sender, EventArgs e)
    {
        // Тик может прийти, пока предыдущий запрос ещё в полёте, — не
        // наслаиваем опросы, иначе на медленной сети они съедят лимит частоты.
        if (_tgBusy || _tgCode is not { } code) return;

        if (DateTimeOffset.UtcNow >= _tgDeadline)
        {
            StopTelegramPoll();
            Show(Loc.T("Ссылка входа устарела — нажми «Войти через Telegram» ещё раз."), error: true);
            return;
        }

        _tgBusy = true;
        try
        {
            var (done, ok, message) = await _license.TelegramPollAsync(code);
            if (!done) return;

            StopTelegramPoll();
            if (ok)
            {
                ShowFirstLoginRebootNotice();
                DialogResult = true;
                return;
            }
            Show(message, error: true);
        }
        finally
        {
            _tgBusy = false;
        }
    }

    private void StopTelegramPoll()
    {
        if (_tgTimer is not null)
        {
            _tgTimer.Stop();
            _tgTimer.Tick -= OnTelegramTick;
            _tgTimer = null;
        }
        _tgCode = null;
        _tgUrl = null;
        // Картинку отпускаем вместе с попыткой: код одноразовый, и оставлять на
        // экране QR, который уже ничего не откроет, — обманывать пользователя.
        TelegramQrImage.Source = null;
        TelegramQrPanel.Visibility = Visibility.Collapsed;
        // Форма возвращается только если мы её же и спрятали. После успешного
        // входа окно закрывается, и мигать полями на прощание не надо.
        if (IsLoaded && Visibility == Visibility.Visible)
        {
            SetLoginFieldsVisibility(Visibility.Visible);
            SetTelegramBusy(false);
        }
    }

    // Пока ждём бота, обычный вход остаётся доступным: передумал — просто
    // вводишь почту с паролем, опрос сам заглохнет по сроку ссылки.
    private void SetTelegramBusy(bool busy)
    {
        TgButton.IsEnabled = !busy;
        TgButton.Content = busy ? Loc.T("Ждём подтверждения в Telegram...") : Loc.T("Продолжить через Telegram");
    }

    private async Task SubmitAsync(bool register)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        // Человек передумал и вводит почту — недожданный опрос Telegram только
        // ест лимит частоты на сервере, под который попадает и сам вход.
        if (_tgTimer is not null) StopTelegramPoll();
        SetBusy(true);
        try
        {
            var (ok, message) = register
                ? await _license.RegisterAsync(EmailBox.Text, PasswordInput.Password, acceptTerms: true)
                : await _license.LoginAsync(EmailBox.Text, PasswordInput.Password, RememberMeBox.IsChecked == true);

            if (ok)
            {
                if (register && _license.RequiresEmailVerification)
                {
                    // Код-сценарий: письмо с 6-значным кодом уже ушло. Дальше
                    // пользователь вводит код прямо здесь — подтверждение и
                    // вход выполняются автоматически, без браузера.
                    _emailCodePending = true;
                    SetLoginFieldsVisibility(Visibility.Collapsed);
                    TwoFactorPanel.Visibility = Visibility.Visible;
                    TwoFactorPanel.IsEnabled = true;
                    TwoFactorError.Text = Loc.T("Мы отправили 6-значный код на твою почту. Он действует 15 минут.");
                    TwoFactorError.Visibility = Visibility.Visible;
                    TwoFactorBox.Focus();
                    ResendVerificationButton.Visibility = Visibility.Visible;
                    return;
                }

                // После успешной регистрации/входа панель формы должна быть полностью
                // восстановлена: при регистрации она могла остаться скрытой после
                // предыдущего перехода к сбросу пароля.
                ForgotPasswordPanel.Visibility = Visibility.Collapsed;
                TwoFactorPanel.Visibility = Visibility.Collapsed;
                TelegramQrPanel.Visibility = Visibility.Collapsed;
                SetLoginFieldsVisibility(Visibility.Visible);
                Form.Visibility = Visibility.Visible;

                // Для обычного входа оставляем только 2FA; подтверждение email — по ссылке.

                ShowFirstLoginRebootNotice();
                DialogResult = true;
                return;
            }

            if (_license.RequiresTwoFactor)
            {
                SetLoginFieldsVisibility(Visibility.Collapsed);
                TwoFactorPanel.Visibility = Visibility.Visible;
                TwoFactorPanel.IsEnabled = true;
                TwoFactorError.Text = message;
                TwoFactorError.Visibility = Visibility.Visible;
                TwoFactorBox.Focus();
                return;
            }

            Show(string.IsNullOrWhiteSpace(message) ? Loc.T("Не удалось выполнить вход. Проверь подключение к серверу.") : message, error: true);
            ResendVerificationButton.Visibility = message.Contains("подтверд", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
            if (ResendVerificationButton.Visibility == Visibility.Visible)
                ResendVerificationButton.IsEnabled = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Show(string message, bool error)
    {
        ErrorText.Text = message;
        ErrorText.Foreground = error
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("TextMutedBrush");
        ErrorText.Visibility = Visibility.Visible;
    }

    // Пока идёт запрос — форма выключена: повторный клик по «Войти» отправил
    // бы второй запрос и съел лимит частоты на сервере.
    private void SetBusy(bool busy)
    {
        Form.IsEnabled = !busy;
        ForgotPasswordPanel.IsEnabled = !busy;
        LoginButton.Content = busy ? Loc.T("Подключение...") : Loc.T("Войти");
        if (busy) ErrorText.Text = Loc.T("Проверяем данные…");
    }

    private static readonly System.Windows.Media.Brush ValidBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128));
    private static readonly System.Windows.Media.Brush InvalidBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));

    private void EmailBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        SetFieldState(EmailBox, email.Length == 0 ? null : EmailRules.Check(email).Ok);
        RefreshCreateHighlight();
    }

    // «Создать» горит акцентом, только когда почта введена корректно;
    // пока поле пустое или невалидное — кнопка приглушена.
    private void RefreshCreateHighlight()
    {
        if (CreateAccountButton == null) return; // ещё идёт InitializeComponent
        var emailOk = EmailRules.Check(EmailBox.Text.Trim()).Ok;
        CreateAccountButton.Foreground = emailOk
            ? (System.Windows.Media.Brush)FindResource("AccentTopBrush")
            : (System.Windows.Media.Brush)FindResource("TextFaintBrush");
    }

    // Ссылки-кнопки («Создать», «Отправить письмо ещё раз»): при наведении
    // текст горит белым. Стандартная подложка Button убрана шаблоном —
    // она светлая и выбивается из тёмной темы.
    private void LinkButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Control b)
            b.Foreground = System.Windows.Media.Brushes.White;
    }

    private void LinkButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender == CreateAccountButton)
            RefreshCreateHighlight();
        else if (sender is System.Windows.Controls.Control b)
            b.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentTopBrush");
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) =>
        SetFieldState(PasswordInput, PasswordInput.Password.Length == 0 ? null : PasswordInput.Password.Length >= 8);

    private static void SetFieldState(System.Windows.Controls.Control field, bool? valid)
    {
        field.BorderBrush = valid is null ? null : valid.Value ? ValidBrush : InvalidBrush;
        field.BorderThickness = valid is null ? new Thickness(1) : new Thickness(valid.Value ? 1.5 : 1);
    }

    private void Field_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Control field)
        {
            field.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 120, 130));
            // Каретка — в начало пустого поля, а не туда, куда пришёлся клик.
            // В непустом поле курсор остаётся там, куда нажал пользователь.
            if (field is System.Windows.Controls.TextBox tb && tb.Text.Length == 0)
                tb.CaretIndex = 0;
        }
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox) EmailBox_TextChanged(sender, null!);
        else if (sender is System.Windows.Controls.PasswordBox) PasswordInput_PasswordChanged(sender, null!);
    }

    // После первого успешного входа — уведомление о перезагрузке для полной
    // оптимизации (часть твиков в бусте живут в реестре и требуют reboot).
    // Показываем один раз: флаг сохраняется в settings.json.
    private void ShowFirstLoginRebootNotice()
    {
        try
        {
            var store = new SettingsStore();
            store.Load();
            if (store.Data.FirstLoginRebootNoticeShown)
                return; // уже показывали

            store.Data.FirstLoginRebootNoticeShown = true;
            store.Save();

            // Уведомление показываем через MessageBox — окно входа ещё живо,
            // главное окно не открыто, тостов пока нет.
            MessageBox.Show(
                Loc.T("Перезагрузи систему один раз для полной оптимизации — часть твиков буста вступят в силу после reboot."),
                "NitroFrame",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch
        {
            // Не удалось загрузить/сохранить настройки — не критично,
            // уведомление просто покажется ещё раз при следующем входе.
        }
    }
}
