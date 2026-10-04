using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NitroFrame.Backend;

namespace NitroFrame.ViewModels;

// ViewModel тарифа и оплаты: статус подписки, остаток пробного периода,
// цены двух тарифов (1 и 3 месяца), создание платежа (СБП/крипта) и активация
// ключа. Все решения о доступе принимает сервер лицензий — здесь только показ
// его ответа и обращения к нему.
public partial class LicenseViewModel : ObservableObject
{
    private const int MaxPolls = 150;   // ~10 минут при опросе раз в 4 сек

    private readonly LicenseManager _license;
    private readonly DispatcherTimer _pollTimer;

    private PaymentIntent? _pendingPayment;
    private DateTimeOffset _lastPaymentAttempt = DateTimeOffset.MinValue;
    private int _pollCount;
    // Опрос платежа асинхронный, а тик таймера — нет: без этого флага
    // подвисший запрос успел бы наложиться на следующий тик, и один и тот же
    // ключ активировался бы дважды.
    private bool _polling;

    public event Action<string, string>? Notify;
    public event Action? LicenseChanged;
    public event Action<string, string, string>? PaymentCreated;   // method, id, url
    public event Action<PaymentIntent>? PaymentDialogRequested;
    // Пользователь вышел из аккаунта — окно решает, что дальше: вход
    // обязателен, поэтому либо новый вход, либо выход из программы.
    public event Action? LoggedOut;
    // После валидации выяснилось, что подписки нет — показать окно
    // предложения ввести gift key или оплатить (один раз за запуск).
    public event Action? NeedsSubscription;

    public LicenseViewModel(LicenseManager license)
    {
        _license = license;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _pollTimer.Tick += async (_, _) => await PollPaymentAsync();
    }

    public string PlanStatus => _license.StatusText();
    public string TrialRemainingText => _license.TrialRemainingText();
    public bool IsTrial => _license.IsTrial;
    public bool IsPaid => _license.IsPaid();
    public string PaidUntil => _license.PaidUntilHuman();

    // Полоса подписки на главной: прогресс 0..1 и подписи.
    // Заморозка проверяется первой: у замороженной подписки статус остаётся
    // active, поэтому без этой ветки полоса рисовала бы обычный PRO и тающий
    // счётчик дней — то есть врала бы дважды: и о доступе, и о расходе срока.
    public string PlanLabel => IsFrozen ? Loc.T("ПАУЗА") : IsPaid ? "PRO" : IsTrial ? "TRIAL" : "—";
    public string RemainingText => IsFrozen
        ? FreezeShortText
        : IsPaid
            ? string.Format(Loc.T("Осталось дней: {0}"), _license.PaidRemainingText())
            : IsTrial
                ? string.Format(Loc.T("Осталось дней: {0}"), TrialRemainingText)
                : Loc.T("Подписка не активна");

    // Сколько осталось до конца периода (0..1) — внутренняя доля для свечения.
    public double ProgressFraction => RemainingFraction();

    // Полоса на главной всегда заполнена на 100%, пока подписка жива
    // (на паузе — приглушённым цветом через триггер IsFrozen в XAML).
    // Тает только свечение блика, см. GlowOpacity.
    public double BarFill
    {
        get
        {
            if (IsFrozen) return 1;
            if (!(IsPaid || IsTrial)) return 0;
            var end = _license.ExpiresAtLocal;
            if (end is null) return 0;
            return end.Value > DateTimeOffset.Now ? 1 : 0;
        }
    }

    // Яркость блика на полосе: 1 при полном сроке → 0 к концу срока.
    // Дни уменьшаются — свечение гаснет, а сама полоса остаётся заполненной.
    public double GlowOpacity
    {
        get
        {
            if (IsFrozen) return 0;
            return RemainingFraction();
        }
    }

    private double RemainingFraction()
    {
        var end = _license.ExpiresAtLocal;
        if (end is null) return 0;
        var totalDays = IsTrial ? Config.TrialDays : (TierDurationDays());
        if (totalDays <= 0) totalDays = 30;
        var left = end.Value - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero) return 0;        // истекла — свечения нет
        var f = left.TotalDays / totalDays;
        return f < 0 ? 0 : f > 1 ? 1 : f;
    }

    private int TierDurationDays() => _license.Tier switch
    {
        "3m" => 90,
        "1m" => 30,
        _ => 30,
    };

    // Плашка состояния на главной: одной строкой — можно ли жать «Ускорить».
    // Причину отказа берём ту же, что и сам движок, чтобы плашка не спорила
    // с кнопкой.
    public bool CanOptimize => _license.CanOptimize().Ok;
    public string ReadyText => IsFrozen
        ? Loc.T("Подписка заморожена")
        : CanOptimize ? Loc.T("Готово к запуску") : Loc.T("Подписка не активна");

    public double Price1mRub => Config.SubscriptionPrice1MRub;
    public double Price3mRub => Config.SubscriptionPrice3MRub;

    // Отпечаток железа: по нему сервер ведёт пробный период и привязку ключа.
    // Он же нужен поддержке, когда лицензию просят перепривязать вручную.
    public string DeviceId => _license.DeviceId;
    public LicenseManager License => _license;

    public void ShowPaymentDialog(PaymentIntent intent) => PaymentDialogRequested?.Invoke(intent);

    [RelayCommand]
    private void CopyDeviceId()
    {
        System.Windows.Clipboard.SetText(DeviceId);
        Notify?.Invoke(Config.AppName, Loc.T(Loc.T("HWID скопирован в буфер обмена.")));
    }

    public string PriceText1m => $"{Config.SubscriptionPrice1MRub:F0} ₽";
    public string PriceText3m => $"{Config.SubscriptionPrice3MRub:F0} ₽";

    // Тариф, выбранный на странице профиля: по нему платит общая кнопка
    // «Оплатить». По умолчанию — самый короткий (1 месяц).
    [ObservableProperty]
    private string _selectedPlan = "1m";

    [RelayCommand]
    private void SelectPlan(string? plan)
    {
        if (!string.IsNullOrEmpty(plan)) SelectedPlan = plan;
    }

    // ------------------------------------------------------------------ #
    // Заморозка подписки
    // ------------------------------------------------------------------ #
    public bool IsFrozen => _license.IsFrozen;
    public DateTimeOffset? FrozenUntil => _license.FrozenUntil;
    public int FreezesRemaining => _license.FreezesRemaining;
    public bool CanFreeze => _license.CanFreeze;
    public string FreezeRemainingText => _license.FreezeRemainingText();

    // Короткая версия для полосы на главной: там строка стоит рядом с
    // кнопкой и длинный текст из профиля там обрезался бы многоточием.
    public string FreezeShortText
    {
        get
        {
            if (!IsFrozen) return string.Empty;
            return FrozenUntil is { } until
                ? string.Format(Loc.T("Срок не тратится · после разморозки до {0:dd.MM.yyyy}"), until.ToLocalTime())
                : Loc.T("Срок не тратится");
        }
    }

    [RelayCommand]
    private async Task FreezeSubscriptionAsync()
    {
        var (ok, message) = await _license.FreezeAsync();
        Notify?.Invoke(Config.AppName, message);
        if (ok) Refresh();
    }

    [RelayCommand]
    private async Task UnfreezeSubscriptionAsync()
    {
        var (ok, message) = await _license.UnfreezeAsync();
        Notify?.Invoke(Config.AppName, message);
        if (ok) Refresh();
    }

    [ObservableProperty]
    private string _licenseKeyInput = "";

    // ------------------------------------------------------------------ #
    // Аккаунт: вход, регистрация, выход
    // ------------------------------------------------------------------ #
    [ObservableProperty]
    private string _emailInput = "";

    public bool IsLoggedIn => _license.IsLoggedIn;
    public bool IsAdmin => _license.Session.Role == "admin";

    // ------------------------------------------------------------------ #
    // Через что вошли
    // ------------------------------------------------------------------ #
    // Telegram-аккаунт живёт под внутренним адресом tg{id}@telegram.local —
    // человеку его показывать нельзя, он подумает, что это его почта. Поэтому
    // способ входа показываем отдельно от того, кем он вошёл.
    public bool IsTelegramLogin => AccountIdentity.IsTelegram(_license.Email);
    public string LoginProviderLabel => AccountIdentity.ProviderLabel(_license.Email);

    // Кем вошли: свой никнейм (если задан), иначе почта как есть,
    // для Telegram — Telegram ID из адреса-маркера.
    public string AccountName
        => string.IsNullOrWhiteSpace(_license.Session.Nickname)
            ? AccountIdentity.Display(_license.Email)
            : _license.Session.Nickname!;

    // Ник уже выбран? Главному окну: false — показать окно «придумай никнейм».
    public bool HasNickname => !string.IsNullOrWhiteSpace(_license.Session.Nickname);

    // Дозапросить ник с сервера, если сессия его не знает (сессии старых
    // сборок его теряли). Пробрасываем менеджеру.
    public Task TryRestoreNicknameAsync(CancellationToken ct = default) =>
        _license.TryRestoreNicknameAsync(ct);

    // Иконка рядом с подписью: telegram против конверта-почты.
    public string LoginProviderIcon => IsTelegramLogin ? "telegram" : "user";

    // Подсказка, зачем это знать: способ входа определяет, как восстановить
    // доступ. Через Telegram пароля нет, и «забыли пароль» не поможет.
    public string LoginProviderHint => IsTelegramLogin
        ? Loc.T("Вход через Telegram — пароля у аккаунта нет. Заходи тем же Telegram-аккаунтом.")
        : Loc.T("Вход по почте и паролю. Пароль можно сбросить на экране входа.");

    public string AccountText => _license.Email is { } email
        ? string.Format(Loc.T("Вы вошли как {0}"), AccountIdentity.Display(email))
        : Loc.T("Войдите, чтобы подписка находилась по аккаунту на любом устройстве.");

    // Внутренняя почта Telegram-аккаунта (tg{id}@telegram.local). Платежи
    // привязываются к ней, и на сайте подписку можно оплатить, введя этот
    // адрес. Показываем только Telegram-аккаунтам: у почтовых он совпадает
    // с логином и уже виден в AccountName.
    public bool HasInternalPaymentEmail => IsTelegramLogin && !string.IsNullOrWhiteSpace(_license.Email);
    public string? InternalPaymentEmail => HasInternalPaymentEmail ? _license.Email : null;

    [RelayCommand]
    private void CopyInternalPaymentEmail()
    {
        if (_license.Email is not { } email) return;
        System.Windows.Clipboard.SetText(email);
        Notify?.Invoke(Config.AppName, Loc.T(Loc.T("Почта для оплаты скопирована в буфер обмена.")));
    }

    // PasswordBox приходит параметром команды: его Password намеренно не
    // привязывается — WPF держит открытый пароль вне механизма привязок.
    [RelayCommand]
    private Task LoginAsync(object? box) => AuthAsync(box, register: false);

    [RelayCommand]
    private Task RegisterAsync(object? box) => AuthAsync(box, register: true);

    private async Task AuthAsync(object? box, bool register)
    {
        if (box is not System.Windows.Controls.PasswordBox pw) return;

        var (ok, message) = register
            ? await _license.RegisterAsync(EmailInput, pw.Password, acceptTerms: false)
            : await _license.LoginAsync(EmailInput, pw.Password);
        Notify?.Invoke(Config.AppName, message);
        if (!ok) return;

        pw.Clear();
        Refresh();
    }

    [RelayCommand]
    private void Logout()
    {
        _license.Logout();
        Refresh();
        LoggedOut?.Invoke();
    }

    // После входа в другом окне (LoginWindow) — подтянуть сессию с диска.
    public void ReloadSession()
    {
        _license.Reload();
        Refresh();
    }

    // Проверка подписки на старте приложения. Ответ сервера перезаписывает
    // сохранённое состояние; при недоступном сервере остаёмся на нём же —
    // сколько это состояние ещё действует, решает грейс-период.
    public async Task RefreshFromAccountAsync()
    {
        var result = await _license.RefreshAccountStateAsync();
        if (result.Outcome != ApiOutcome.Ok || result.State is null)
            Notify?.Invoke(Config.AppName, result.Outcome == ApiOutcome.Unreachable
                ? Loc.T("Сервер лицензий недоступен. Проверь подключение к интернету.")
                : string.Format(Loc.T("Обновление подписки не выполнено: {0}"), result.Message));
        Refresh();
        OnPropertyChanged(nameof(PlanStatus));
        OnPropertyChanged(nameof(PaidUntil));
        OnPropertyChanged(nameof(IsPaid));
    }

    public async Task ValidateAsync()
    {
        var result = await _license.ValidateAsync();
        Refresh();
        OnPropertyChanged(nameof(PlanStatus));
        OnPropertyChanged(nameof(PaidUntil));
        OnPropertyChanged(nameof(TrialRemainingText));
        OnPropertyChanged(nameof(IsPaid));

        // Сервер требует обновиться: показываем это отдельным понятным
        // сообщением. Иначе 426 попадал в общую ветку и выглядел как
        // «сервер недоступен» — человек шёл в поддержку вместо обновления.
        if (result.Outcome == ApiOutcome.UpdateRequired)
        {
            Notify?.Invoke(Config.AppName, string.IsNullOrWhiteSpace(result.Message)
                ? Loc.T("Эта версия NitroFrame устарела — установи новую, чтобы продолжить.")
                : result.Message);
            return;
        }

        // Молчим о недоступности сервера, пока грейс-период не кончился:
        // ругаться на каждый запуск без интернета при живой подписке незачем.
        if (result.Outcome == ApiOutcome.Unreachable && !CanOptimize)
            Notify?.Invoke(Config.AppName, _license.CanOptimize().Reason);

        // Новый пользователь без подписки и без триала — предлагаем
        // ввести gift key или оплатить. Один раз за запуск.
        // Замороженную подписку не трогаем: у неё есть Pro, он просто
        // приостановлен.
        if (!CanOptimize && !IsTrial && !IsFrozen)
            NeedsSubscription?.Invoke();
    }

    // ------------------------------------------------------------------ #
    // Промокод: скидка от медиа-партнёра. Проверяется сервером сразу —
    // человек видит свою скидку ДО оплаты; код уезжает и в платёж,
    // чтобы скидка попала в счёт даже без проверки на этом экране.
    // ------------------------------------------------------------------ #
    [ObservableProperty]
    private string _promoInput = "";

    [ObservableProperty]
    private string? _appliedPromoCode;

    [ObservableProperty]
    private string _promoStatusText = "";

    public bool HasAppliedPromo => AppliedPromoCode != null;

    partial void OnAppliedPromoCodeChanged(string? value) => OnPropertyChanged(nameof(HasAppliedPromo));

    public bool CanApplyPromo => _license.IsLoggedIn && !string.IsNullOrWhiteSpace(PromoInput) && AppliedPromoCode == null;

    partial void OnPromoInputChanged(string value) => OnPropertyChanged(nameof(CanApplyPromo));

    [RelayCommand]
    private async Task ApplyPromoAsync()
    {
        if (!CanApplyPromo)
        {
            if (!_license.IsLoggedIn)
                Notify?.Invoke(Config.AppName, Loc.T(Loc.T("Сначала войди в аккаунт — промокод привязывается к нему.")));
            return;
        }

        var code = PromoInput.Trim();
        var (outcome, result, message) = await LicenseApi
            .CheckPromoAsync(code, _license.Email).ConfigureAwait(false);

        // Показ статуса всегда из UI-контекста — свойство смотрится биндингом.
        PromoStatusText = message;
        if (outcome == ApiOutcome.Ok && result is { Valid: true })
        {
            AppliedPromoCode = code;
            PromoInput = code;
        }
        else
        {
            AppliedPromoCode = null;
        }
    }

    [RelayCommand]
    private void ClearPromo()
    {
        AppliedPromoCode = null;
        PromoInput = "";
        PromoStatusText = "";
        OnPropertyChanged(nameof(CanApplyPromo));
    }

    [RelayCommand]
    private async Task StartPaymentAsync(string? arg)
    {
        if (!_license.IsLoggedIn)
        {
            Notify?.Invoke(Config.AppName, Loc.T(Loc.T("Аккаунт не определён. Перезапусти лаунчер и войди через Telegram ещё раз.")));
            return;
        }
        if (_pendingPayment is not null)
        {
            Notify?.Invoke(Config.AppName, Loc.T(Loc.T("У тебя уже есть незавершённый платёж. Открой окно платежа и проверь его.")));
            return;
        }
        if (DateTimeOffset.UtcNow - _lastPaymentAttempt < TimeSpan.FromSeconds(8))
        {
            Notify?.Invoke(Config.AppName, Loc.T(Loc.T("Слишком часто. Подожди несколько секунд.")));
            return;
        }
        _lastPaymentAttempt = DateTimeOffset.UtcNow;
        // arg приходит из XAML как "метод|тариф", например "sbp|3m".
        // Без параметра — общая кнопка «Оплатить»: метод СБП, тариф —
        // выбранный на странице профиля (SelectedPlan).
        var method = "sbp";
        var plan = SelectedPlan;
        if (!string.IsNullOrEmpty(arg))
        {
            var parts = arg.Split('|');
            method = parts[0];
            plan = parts.Length > 1 ? parts[1] : "1m";
        }

        // Назначение платежа видно плательщику на странице оплаты.
        var paymentDescription = Loc.T("NitroFrame — подписка");
        var (ok, intent, message) = await Payment.CreateForAccountAsync(
            method, plan, DeviceId, _license.Email, paymentDescription, AppliedPromoCode);
        if (!ok || intent is null)
        {
            // Провайдер ещё не подключён — сервер отвечает 501 и объясняет, чего
            // не хватает. Показываем это как есть: ссылка в никуда была бы хуже.
            Notify?.Invoke(Config.AppName, message);
            return;
        }

        _pendingPayment = intent;
        _pollCount = 0;
        _pollTimer.Start();

        PaymentCreated?.Invoke(intent.Method, intent.PaymentId.ToString(), intent.PayUrl);
        // Событие поднимается из async-команды; обработчик окна сам переносит
        // показ диалога на UI-поток без блокирующего Dispatcher.Invoke.
        PaymentDialogRequested?.Invoke(intent);
        Notify?.Invoke(Config.AppName, string.Format(Loc.T("Счёт на {0:F0} {1} создан."), intent.Amount, intent.Currency));
    }

    [RelayCommand]
    private async Task ActivateKeyAsync()
    {
        // Сначала пробуем активировать как подарочный код.
        var (redeemOk, redeemMsg) = await _license.RedeemGiftCodeAsync(LicenseKeyInput);
        if (redeemOk)
        {
            Notify?.Invoke(Config.AppName, redeemMsg);
            LicenseKeyInput = "";
            _pendingPayment = null;
            _pollTimer.Stop();
            Refresh();
            return;
        }

        // Если не подарочный — пробуем как обычный лицензионный ключ.
        var (ok, message) = await _license.ActivateAsync(LicenseKeyInput);
        Notify?.Invoke(Config.AppName, message);
        if (!ok) return;

        LicenseKeyInput = "";
        _pendingPayment = null;
        _pollTimer.Stop();
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(AccountText));
        OnPropertyChanged(nameof(IsTelegramLogin));
        OnPropertyChanged(nameof(LoginProviderLabel));
        OnPropertyChanged(nameof(LoginProviderIcon));
        OnPropertyChanged(nameof(LoginProviderHint));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(HasNickname));
        OnPropertyChanged(nameof(PlanStatus));
        OnPropertyChanged(nameof(TrialRemainingText));
        OnPropertyChanged(nameof(IsTrial));
        OnPropertyChanged(nameof(IsPaid));
        OnPropertyChanged(nameof(PaidUntil));
        OnPropertyChanged(nameof(CanOptimize));
        OnPropertyChanged(nameof(ReadyText));
        OnPropertyChanged(nameof(PlanLabel));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(BarFill));
        OnPropertyChanged(nameof(GlowOpacity));
        OnPropertyChanged(nameof(IsFrozen));
        OnPropertyChanged(nameof(FrozenUntil));
        OnPropertyChanged(nameof(FreezesRemaining));
        OnPropertyChanged(nameof(CanFreeze));
        OnPropertyChanged(nameof(FreezeRemainingText));
        OnPropertyChanged(nameof(FreezeShortText));
        LicenseChanged?.Invoke();
    }

    // Отмена незавершённого платежа из окна оплаты: останавливаем опрос
    // статуса, чтобы он не висел в фоне 10 минут после закрытого окна.
    // Следующая «Оплатить» создаст новый счёт как обычно.
    public void CancelPendingPayment()
    {
        _pendingPayment = null;
        _pollTimer.Stop();
    }

    public void Shutdown() => _pollTimer.Stop();

    private async Task PollPaymentAsync()
    {
        if (_pendingPayment == null)
        {
            _pollTimer.Stop();
            return;
        }
        if (++_pollCount > MaxPolls)
        {
            _pollTimer.Stop();
            _pendingPayment = null;
            Notify?.Invoke(Config.AppName,
                Loc.T("Оплата так и не подтвердилась. Если деньги списаны — напиши в поддержку, ") +
                Loc.T("ключ выдадут вручную."));
            return;
        }
        if (_polling) return;

        _polling = true;
        try
        {
            var paymentId = _pendingPayment.PaymentId;
            if (await Payment.CheckStatusAsync(paymentId) != PaymentStatus.Paid) return;

            _pollTimer.Stop();
            _pendingPayment = null;

            // Ключ выпускается сервером по вебхуку провайдера и приходит вместе
            // со статусом — переписывать его руками из письма не нужно.
            var key = await Payment.IssuedKeyAsync(paymentId);
            if (string.IsNullOrEmpty(key))
            {
                Notify?.Invoke(Config.AppName,
                    Loc.T("Оплата прошла, но сервер ещё выпускает подписку. Обновление продолжится автоматически."));
                await _license.RefreshAccountStateAsync();
                Refresh();
                return;
            }

            // Для аккаунтной покупки сервер уже привязал лицензию к UserId.
            // Обновляем аккаунт напрямую; активация ключа по HWID нужна только
            // старому сценарию оплаты без email.
            if (!string.IsNullOrWhiteSpace(_license.Email))
            {
                await _license.RefreshAccountStateAsync();
                Notify?.Invoke(Config.AppName, Loc.T(Loc.T("Оплата подтверждена. Подписка активирована автоматически.")));
            }
            else
            {
                var (ok, message) = await _license.ActivateAsync(key!);
                Notify?.Invoke(Config.AppName, ok ? string.Format(Loc.T("Оплата подтверждена. {0}"), message) : message);
            }
            Refresh();
        }
        finally
        {
            _polling = false;
        }
    }
}
