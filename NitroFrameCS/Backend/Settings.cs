using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NitroFrame.Backend;

// Хранимые настройки приложения (%LOCALAPPDATA%\NitroFrame\settings.json).
// Пустая строка в *Vendor означает «пользователь ещё не выбирал» — на этом
// построена фильтрация вендорных твиков (твик с CpuVendor="intel" не совпадёт
// с ""). LaunchMode всегда имеет конкретное значение: вопрос при первом
// запуске убран, по умолчанию Majestic, меняется в «Настройках».
public class AppSettings
{
    // --- параметры оптимизации ---
    public double PollIntervalSeconds { get; set; } = 2.0;   // как часто опрашивать процесс игры
    public bool AutoRestoreOnExit { get; set; } = true;      // откатывать твики при закрытии приложения
    // Автопилот: игра появилась в процессах — оптимизация включается сама,
    // без кнопки и без запуска RP-лаунчера; после выхода из игры всё
    // откатывается, как при обычной сессии. Выключено по умолчанию: автоматическое
    // изменение системы должно быть явным выбором человека.
    public bool AutoOptimizeOnGameStart { get; set; } = false;
    public List<string> DisabledTweaks { get; set; } = [];   // описания твиков, выключенных пользователем
    // Инструменты (OptIn-твики со страницы «Инструменты») — наоборот:
    // по умолчанию выключены, применяются только из этого списка.
    public List<string> EnabledTools { get; set; } = [];
    public string GtaSettingsPath { get; set; } = "";        // пусто — ищем автоматически (GtaSettings.FindSettingsFile)
    public string GtaActivePreset { get; set; } = "";        // ключ последнего применённого пресета графики
    public string PubgActivePreset { get; set; } = "";       // ключ последнего применённого пресета PUBG
    public string Cs2ActivePreset { get; set; } = "";        // ключ последнего применённого пресета CS2
    public string GpuVendor { get; set; } = "";              // "nvidia" | "amd"; пусто — не выбрано
    public string CpuVendorChoice { get; set; } = "";        // "intel" | "amd"; пусто — не выбрано (спросим при запуске)

    // --- режим запуска кнопкой «Играть» ---
    public string LaunchMode { get; set; } = "majestic";      // "majestic" | "gta5rp"
    public string MajesticPath { get; set; } = "";            // путь к Majestic Launcher.exe (иначе автопоиск)
    public string Gta5RpPath { get; set; } = "";              // путь к лаунчеру GTA5RP (иначе автопоиск)
    public string SteamPath { get; set; } = "";               // путь к steam.exe (пусто = автопоиск при старте)
    public bool CleanRamOnLaunch { get; set; } = true;        // запускать очистку памяти при старте игры

    // --- игровой режим (временные пер-процессные оптимизации) ---
    public bool BoostPriority { get; set; } = true;           // высокий приоритет процесса игры
    public bool BoostIoPriority { get; set; } = true;         // высокий приоритет ввода-вывода
    public bool BoostEcoQos { get; set; } = true;             // отключать EcoQoS/троттлинг для игры
    public bool BoostTimer { get; set; } = true;              // разрешение таймера 1 мс на время игры
    public bool BoostAffinity { get; set; } = true;           // привязка игры к ядрам без ядра 0 (ровнее фреймтайм)
    public bool BoostMemoryPriority { get; set; } = true;     // высокий приоритет памяти игры
    // BoostPowerPlan и BoostCoreParking убраны в 2.1.11: оба действия сознательно
    // не выполнялись (см. комментарий в конце GameBooster.Boost), а поля
    // продолжали сохраняться и выглядеть рабочими. Старые ключи в settings.json
    // просто игнорируются при чтении.

    // --- интерфейс / предпочтения ---
    public bool ReduceAnimations { get; set; } = false;        // облегчённый режим анимаций (слабое железо)
    public bool MinimizeToTray { get; set; } = false;          // сворачивать в трей вместо закрытия
    public bool SubscriptionPromptShown { get; set; } = false; // окно активации показано при первом запуске
    // Ключ акцентного цвета из ThemeService.Accents. Неизвестное значение
    // (удалённый вариант, битый settings.json) молча заменяется дефолтом.
    public string ThemeAccent { get; set; } = "violet";

    // Язык интерфейса: "ru" | "en". Русский — язык по умолчанию и одновременно
    // ключ локализации (см. Backend/Localization/Loc.cs): отсутствующий в
    // словаре перевод возвращается как есть, поэтому деградация — на русский.
    public string Language { get; set; } = "ru";

    // Цвет иконки уведомления: ключ пресета ThemeService.ToastAccents
    // или «#RRGGBB» (свой цвет). Дефолт — «как тема».
    public string ToastAccent { get; set; } = "theme";

    // Цвет обычных кнопок: «default» (фирменный графит), ключ пресета
    // ThemeService.Accents или «#RRGGBB». Мусор вычищается в Normalize.
    public string ButtonColor { get; set; } = "default";

    // Активный игровой профиль (GTA V / CS2 / PUBG / Rust). Неизвестное
    // значение — дефолт «gta», как и у темы.
    public string ActiveGameProfile { get; set; } = "gta";

    // Кастомные цвета профилей: ключ профиля → «#RRGGBB». Отсутствие записи
    // (или мусор, вычищенный в Normalize) = фирменный цвет профиля.
    public Dictionary<string, string> ProfileColors { get; set; } = [];

    // Уведомление о перезагрузке после первого логина показано. false — при
    // следующем успешном входе всплывёт напоминание «перезагрузи систему один
    // раз для полной оптимизации».
    public bool FirstLoginRebootNoticeShown { get; set; } = false;



    // --- оверлей производительности (отдельное окно поверх игры) ---
    // Это НЕ оверлей внутри игры: никакой инъекции в GTA5.exe, только своё
    // прозрачное окно поверх остальных и системные источники данных (ETW).
    // --- NVIDIA DRS (Driver Profile Settings) ---
    public bool DrsEnabled { get; set; } = false;           // авто-применение DRS перед игрой
    public int DrsFpsLimit { get; set; } = 0;               // 0 = без ограничения
    public int DrsLowLatencyMode { get; set; } = 2;          // 0=выкл, 1=вкл, 2=Ultra
    public bool DrsVsyncForceOff { get; set; } = true;       // VSync Force Off в профиле
    public bool DrsPowerModeMaxPerf { get; set; } = true;    // Режим питания: Max Performance

    public bool OverlayEnabled { get; set; } = false;          // показывать оверлей вообще
    public string OverlayHotkey { get; set; } = Hotkey.Default; // "F9" — одна клавиша, как у игровых OSD
    public string OverlayPreset { get; set; } = "standard";     // "minimal" | "standard" | "detailed"

    // Где появляется оверлей: "top" — сверху по центру (как панель
    // статистики NVIDIA), "corner" — в левом верхнем углу (классический OSD).
    // После ручного перетаскивания становится "custom" — иначе позиция,
    // выбранная мышкой, сбрасывалась бы при смене компоновки.
    public string OverlayAnchor { get; set; } = "top";
    // Позиция запоминается в пикселях виртуального рабочего стола: при двух
    // мониторах координаты бывают отрицательными, поэтому clamp по 0 нельзя.
    public double OverlayX { get; set; } = double.NaN;         // NaN — ещё не двигали, встанем в угол
    public double OverlayY { get; set; } = double.NaN;

    // Приводит прочитанный файл к допустимым значениям. settings.json лежит в
    // пользовательской папке и его правят руками (или портит аварийное
    // выключение), поэтому доверять содержимому нельзя: незнакомый вендор
    // отфильтровал бы все вендорные твики, а null в списке уронил бы
    // построение списка твиков.
    public void Normalize()
    {
        PollIntervalSeconds = Math.Clamp(Math.Round(PollIntervalSeconds, 1), 0.5, 5.0);
        ActiveGameProfile = GameProfiles.Normalized(ActiveGameProfile);

        // Кастомные цвета профилей: мусор (не профиль / не hex) выкидываем,
        // остальное живёт. Кисти красятся при старте и на лету.
        ProfileColors = (ProfileColors ?? [])
            .Where(kv => GameProfiles.All.Any(p => p.Key == kv.Key) && GameProfiles.IsValidHex(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        DisabledTweaks = DisabledTweaks is null
            ? []
            : [.. DisabledTweaks.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().Order()];

        EnabledTools = EnabledTools is null
            ? []
            : [.. EnabledTools.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().Order()];

        // Язык: неизвестное значение (битый файл) — русский.
        if (Language != "en") Language = "ru";

        GpuVendor = Pick(GpuVendor, "nvidia", "amd");
        CpuVendorChoice = Pick(CpuVendorChoice, "intel", "amd");
        LaunchMode = Pick(LaunchMode, "majestic", "gta5rp");

        ButtonColor = ThemeService.NormalizeButtonKey(ButtonColor);

        GtaSettingsPath = Clean(GtaSettingsPath);
        MajesticPath = Clean(MajesticPath);
        Gta5RpPath = Clean(Gta5RpPath);
        SteamPath = Clean(SteamPath);
        GtaActivePreset = (GtaActivePreset ?? "").Trim();

        // Комбинацию правят руками чаще прочего («поставлю сам в файле»), и
        // неразобранная строка означала бы оверлей без способа его вызвать.
        OverlayHotkey = Hotkey.TryParse(OverlayHotkey, out _) ? OverlayHotkey.Trim() : Hotkey.Default;
        OverlayPreset = Pick(OverlayPreset, "minimal", "standard", "detailed") is { Length: > 0 } p
            ? p : "standard";
        OverlayAnchor = Pick(OverlayAnchor, "top", "corner", "custom") is { Length: > 0 } a
            ? a : "top";

        DrsFpsLimit = Math.Max(0, DrsFpsLimit);
        DrsLowLatencyMode = Math.Clamp(DrsLowLatencyMode, 0, 2);
    }

    // Значение вне списка допустимых (включая null из JSON) = «не выбрано».
    private static string Pick(string? value, params string[] allowed) =>
        value is not null && Array.IndexOf(allowed, value) >= 0 ? value : "";

    private static string Clean(string? path) => (path ?? "").Trim().Trim('"', '\'').Trim();
}

// Загрузка и сохранение settings.json.
//
// Save вызывается на каждое изменение свойства (в том числе на каждый шаг
// ползунка), поэтому запись сделана устойчивой и «тихой»:
//   * пишем во временный файл и подменяем им целевой — прерывание записи
//     (выключение питания, закрытие приложения) не оставит обрезанный JSON,
//     из-за которого слетели бы все настройки;
//   * ошибки ввода-вывода не выбрасываются наружу: настройки — не та причина,
//     по которой приложение вправе упасть посреди перетаскивания ползунка.
//     Последняя ошибка остаётся в LastError.
public class SettingsStore
{
    // NumberHandling обязателен: OverlayX/Y по умолчанию double.NaN («ещё не
    // двигали»), а из железа может прийти Infinity от сломанного датчика.
    // Без флага первая же запись настроек падает с ArgumentException — и, если
    // это происходит при старте (автодетект железа), приложение остаётся живым
    // БЕЗ ОКНА: исключение глотается обработчиком dispatcher'а. Так падал
    // запуск 2.1.6 на свежих установках.
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private AppSettings _data = new();

    public AppSettings Data => _data;

    // Текст последней неудачи чтения/записи (пусто — всё в порядке).
    public string LastError { get; private set; } = "";

    public void Load()
    {
        LastError = "";
        try
        {
            if (!File.Exists(Helpers.SettingsFile))
            {
                _data = new();
                return;
            }

            var text = File.ReadAllText(Helpers.SettingsFile);
            _data = JsonSerializer.Deserialize<AppSettings>(text, _opts) ?? new();
        }
        catch (Exception exc) when (exc is JsonException or IOException
                                        or UnauthorizedAccessException or ArgumentException)
        {
            // Файл испорчен или недоступен. Стартуем со стандартных значений,
            // но повреждённый файл не затираем молча — отводим его в сторону,
            // иначе первая же запись уничтожит единственную копию настроек
            // пользователя (и причину поломки).
            LastError = exc.Message;
            _data = new();
            Quarantine();
        }
        _data.Normalize();
    }

    public bool Save()
    {
        LastError = "";
        try
        {
            Helpers.EnsureDirs();
            var tmp = Helpers.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, _opts));
            File.Move(tmp, Helpers.SettingsFile, overwrite: true);
            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException
                                        or NotSupportedException or ArgumentException)
        {
            LastError = exc.Message;
            return false;
        }
    }

    // Переименовывает нечитаемый settings.json в settings.bad.json.
    private static void Quarantine()
    {
        try
        {
            var bad = Path.Combine(Helpers.AppDataDir, "settings.bad.json");
            File.Move(Helpers.SettingsFile, bad, overwrite: true);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            // не вышло — не страшно, настройки всё равно перезапишутся
        }
    }
}
