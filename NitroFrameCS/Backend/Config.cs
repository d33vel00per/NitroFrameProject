namespace NitroFrame.Backend;

public enum Hive { HKCU, HKLM }
public enum ValueType { DWORD, SZ, QWORD }

public record RegistryTweak(
    Hive Hive,
    string Subkey,
    string Name,
    object Value,
    ValueType ValueType,
    string Description,
    string Category = "Прочее",
    string? GpuVendor = null,
    string? CpuVendor = null,
    // OptIn = «инструмент»: по умолчанию ВЫКЛЮЧЕН и не применяется никогда,
    // пока человек сам не включит его на странице «Инструменты».
    // Обычная галочка работает наоборот: твик применяетcя, пока её не сняли.
    // Сюда попадают настройки, которые снижают защиту (Defender, SmartScreen,
    // HVCI, BitLocker) или меняют интерфейс — не место им в наборе
    // «применяется всем по умолчанию».
    bool OptIn = false,
    // Профили игр, для которых твик НЕ применяется. RAGE MP (Majestic, GTA5RP)
    // на стадии подключения сканирует систему: выключенные PcaSvc / DiagTrack /
    // SysMain — известный античитам маркер «затюненной» системы (тот же список
    // смотрит ServicesCheck), и клиент отвечает «Ошибка соединения». Для профиля
    // gta такие службы остаются включёнными.
    string[]? ExcludeProfiles = null);

// Один шаг командного инструмента: запуск внешней системной утилиты с
// фиксированными аргументами.
public record CommandStep(string Exe, string Args);

// Командный инструмент: то, что нельзя выразить записью в реестр —
// bcdedit, powercfg, schtasks. В отличие от произвольного скрипта:
//   * команды ЗАШИТЫ в программу целиком (exe из закрытого списка,
//     аргументы фиксированы) — из настроек подставить свою строку нельзя;
//   * выполняет их только helper с правами администратора, и он сверяет
//     каждый шаг со своей копией профиля;
//   * откат описан рядом с применением и использует только состояние,
//     зафиксированное ДО применения (например, прежнюю схему питания).
public record CommandTweak(
    string Id,
    string Description,
    string Category,
    IReadOnlyList<CommandStep> Apply,
    IReadOnlyList<CommandStep> Restore,
    // true — перед применением зафиксировать состояние (Capture) в журнал,
    // чтобы откат вернул именно его (схема питания).
    bool CapturesState = false,
    // true — код выхода != 0 при откате не считается ошибкой (bcdedit
    // /deletevalue возвращает 1, если значения и не было).
    bool RestoreToleratesFailure = false);

public record FileTweak(
    string SourcePath,
    string TargetPath,
    string Description,
    string Category = "Файлы игры");

public class TweakProfile
{
    public string Name { get; init; } = "";
    public List<RegistryTweak> RegistryTweaks { get; init; } = [];
    public List<FileTweak> FileTweaks { get; init; } = [];
    // Командные инструменты (bcdedit/powercfg/schtasks) — OptIn по природе:
    // применяются только когда человек включил их в «Инструментах».
    public List<CommandTweak> CommandTools { get; init; } = [];
}

public static partial class Config
{
    public const string AppName = "NitroFrame";
    public const string AppVersion = "2.1.26";

    // Номер перевыпуска того же релиза. 0 — обычный релиз.
    //
    // Зачем: после 2.1.12 потребовалась починка без нового номера версии, а
    // клиент ставит обновление только вверх по версии (UpdateService.ShouldInstall).
    // Одинаковые 2.1.12 в манифесте и на машине означали бы, что плашка не
    // появится ни у одного человека, и исправление никто не получит.
    //
    // Поднять третье число до 2.1.13 было бы неправдой: новый релиз обещает
    // новые возможности, а тут тот же релиз с починкой. Человек видит
    // «v2.1.12», а программа сравнивает полный номер с ревизией.
    //
    // 2.1.13 — обычный релиз (ревизия 0): появилась полная оптимизация через
    // элевируемый helper и режим постоянных настроек — это новая
    // возможность, а не исправление внутри прежнего релиза.
    //
    // Ревизия 1 (02.09.2026, тот же день): карточка постоянных настроек не
    // различала «ждём перезагрузки» и «перезагрузка была» и на любое нажатие
    // отвечала одним и тем же «Применено 93 настройки». Новый номер релиза
    // за час после предыдущего был бы обещанием новых возможностей вместо
    // доведённой до конца той же самой.
    //
    // Ревизия 2 (02.09.2026): продолжение того же разбора. При включённом
    // быстром запуске Windows выключение и включение компьютера НЕ начинает
    // новый сеанс ядра — загрузочные настройки остаются непрочитанными, а
    // программа продолжала просить перезагрузку без объяснения причины.
    //
    // Ревизия 3 (03.09.2026): доведение до конца. Три настройки из 93 Windows
    // возвращает себе при каждой загрузке (режим NTFS last access, тип
    // запуска служб Windows Update и BITS), и каждый заход снова видел их как
    // изменённые и снова просил перезагрузку — вечный круг.
    //
    // 2.1.14 — обычный релиз (ревизия 0): раздел «Инструменты» — ручные
    // настройки, которые по умолчанию выключены и применяются только по
    // явному выбору (Defender, SmartScreen, HVCI, BitLocker, интерфейс).
    // Это новая возможность.
    //
    // 2.1.15 — обычный релиз (ревизия 0): кастомизатор акцента, поиск по
    // настройкам, полный сброс, экспорт/импорт, диагностика helper'а и
    // командные инструменты (bcdedit/powercfg/schtasks) в «Инструментах».
    // Новые возможности.
    //
    // 2.1.16 — обычный релиз (ревизия 0): отчёт после игровой сессии,
    // автозапуск оптимизации при запуске игры (OptIn), карточка «Статус
    // защиты» на главной, исправление просадки FPS в CS2 (HAGS убран из
    // инструментов, принудительный flip-model отключён), фикс обрезавшегося
    // окна подтверждения. Новые возможности + исправления.
    //
    // 2.1.17 — тестовый билд: игровые профили (GTA V / CS2 / PUBG / Rust) на
    // странице «Игра». У каждого профиля свой цвет и свои процессы для
    // автопилота/отчётов; системная оптимизация общая. НЕ публикуется —
    // идёт на проверку владельцу.
    public const int AppRevision = 0;
    //
        //
    // 2.1.19 — обычный релиз (ревизия 0): Steam-интеграция для профилей
    // CS2 / PUBG / Rust. Новое: автопоиск steam.exe (реестр Valve →
    // типовые каталоги) с сохранением пути в настройки; строка «Путь к
    // Steam» в настройках (видна только у Steam-игр); кнопка «Запустить
    // оптимизацию» открывает клиент Steam сразу на странице библиотеки
    // (steam://open/games); если Steam не найден — тихо ждём процесс игры.// 2.1.18 — обычный релиз (ревизия 0): профили доведены до релиза.
    // Новое: настоящие логотипы игр, кастомизация цветов профилей, акцента,
    // уведомлений и кнопок (настройки → «Цвета»), компактная главная без
    // прокрутки. Починки: RP-лаунчер запускается только в профиле GTA V;
    // при закрытии игры статус называет активный профиль, а не «GTA V»;
    // смена профиля на лету переключает ожидание игры (наблюдатели больше
    // не ждут процессы старого профиля); имя сессии в статистике — с именем
    // активной игры. Новые возможности + исправления.

    // Полный номер для сравнения с манифестом и для серверных логов:
    // 2.1.12 при ревизии 0, иначе 2.1.12.1. Version.Parse считает 2.1.12.1 старше
    // чем 2.1.12, и именно это заставляет старую сборку предложить обновление.
    public static string FullVersion =>
        AppRevision == 0 ? AppVersion : $"{AppVersion}.{AppRevision}";

    // Пробный период вместо бесплатных активаций. Счётчик активаций жил в
    // license.json на машине пользователя и обнулялся удалением файла —
    // считать что-либо локально бессмысленно. Срок триала теперь ведёт сервер
    // по HWID, а здесь значение нужно только для текстов в интерфейсе.
    public const int TrialDays = 2;

    // Сколько платная подписка живёт без связи с сервером. На триал не
    // распространяется: 7 дней офлайн-доверия при пробном периоде в 2 дня
    // сделали бы триал бесконечным для любого, кто отключит сеть.
    // Длительность заморозки подписки (в днях). При заморозке ExpiresAt
    // продлевается на это количество дней; заморозку можно отменить в любой
    // момент до истечения FrozenUntil. Значение должно совпадать с таковым
    // на стороне сервера лицензий (если сервер хранит own константу — она
    // authoritative, а это — fallback для текстов и локальных проверок).
    public const int FreezeDurationDays = 30;

    public const int OfflineGraceDays = 7;

    // Боевой сервер лицензий. Публичный адрес должен работать только по HTTPS.
    // Для локальной разработки адрес перекрывается переменной окружения:
    //   NITROFRAME_API=http://localhost:5188
    // В релизном клиенте адрес фиксирован: переменная окружения не должна
    // случайно переключить установленную программу на localhost или старый API.
    public static readonly string ApiBaseUrl =
        Environment.GetEnvironmentVariable("NITROFRAME_API") ?? "https://nitroframe.xyz";

    // Единственный платёжный провайдер. СБП — Avans (avans.pro);
    // крипта у Avans отключена, остаётся заглушка до отдельного провайдера.
    public const string PaymentProviderSbp = "avans";
    public const string PaymentProviderCrypto = "stub";

    public const double SubscriptionPrice1MRub = 299.0;
    public const double SubscriptionPrice3MRub = 799.0;

    public static readonly TweakProfile DemoProfile = new()
    {
        Name = Loc.T("GTA V — базовая оптимизация"),
        RegistryTweaks =
        [
            // Запись и оверлеи Windows
            new(Hive.HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0, ValueType.DWORD,
                Loc.T("Отключение фоновой записи Xbox Game Bar / Game DVR на время игры"), Loc.T("Запись и оверлеи Windows")),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение захвата экрана приложением Xbox (App Capture)"), Loc.T("Запись и оверлеи Windows")),
            // FSO (полноэкранные оптимизации Windows) здесь НЕ отключаются —
            // намеренно, хотя твик "GameDVR_FSEBehaviorMode = 2" есть почти во
            // всех гайдах по задержке ввода.
            //
            // Выключенный FSO означает НАСТОЯЩИЙ монопольный полный экран, а
            // поверх него не рисуется никакое окно: ни оверлей NitroFrame, ни
            // Game Bar, ни оверлей NVIDIA. То есть эта "оптимизация" ломала
            // главную функцию программы, и игрок видел это как "оверлей не
            // работает в полном экране".
            //
            // Обмен того не стоит: по данным самой Microsoft (devblogs,
            // "Demystifying Fullscreen Optimizations") производительность FSO в
            // среднем равна монопольному режиму, а взамен работают быстрый
            // Alt+Tab, несколько мониторов и оверлеи. Кому важнее последние
            // доли миллисекунды — выключает оверлей и ставит флаг вручную.
            new(Hive.HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, ValueType.DWORD,
                Loc.T("Включение игрового режима Windows (Game Mode)"), Loc.T("Запись и оверлеи Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, ValueType.DWORD,
                Loc.T("Запрет Game DVR на уровне групповой политики"), Loc.T("Запись и оверлеи Windows")),

            // Планировщик процессора
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26, ValueType.DWORD,
                Loc.T("Планировщик CPU: короткие кванты с приоритетом активного окна"), Loc.T("Планировщик процессора")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Executive", "BoostPriority", 1, ValueType.DWORD,
                Loc.T("Включение динамического повышения приоритета потоков планировщиком ядра"), Loc.T("Планировщик процессора")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DistributeTimers", 1, ValueType.DWORD,
                Loc.T("Распределение системных таймеров по ядрам CPU (меньше нагрузка на одно ядро)"), Loc.T("Планировщик процессора")),

            // Питание и энергосбережение
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, ValueType.DWORD,
                Loc.T("Отключение энергосберегающего троттлинга процессов (Power Throttling)"), Loc.T("Питание и энергосбережение")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, ValueType.DWORD,
                Loc.T("Отключение выборочного энергосбережения USB (меньше микрофризов от периферии)"), Loc.T("Питание и энергосбережение")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение гибернации (быстрее выход из простоя, меньше нагрузки на диск)"), Loc.T("Питание и энергосбережение")),

            // Память и диск
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1, ValueType.DWORD,
                Loc.T("Запрет выгрузки ядра и драйверов в файл подкачки (держим в оперативной памяти)"), Loc.T("Память и диск")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1, ValueType.DWORD,
                Loc.T("Отключение обновления времени последнего доступа к файлам NTFS (меньше лишних записей на диск)"), Loc.T("Память и диск")),

            // Мультимедиа-профиль системы
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, ValueType.DWORD,
                Loc.T("Приоритет ресурсов CPU для активного приложения вместо фоновых задач"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "HighPrecisionTimer", 1, ValueType.DWORD,
                Loc.T("Высокоточный системный таймер для мультимедиа-профиля"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "HighResolutionEventTimer", 1, ValueType.DWORD,
                Loc.T("Таймер событий высокого разрешения для мультимедиа-профиля"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "LatencyTimer", 1, ValueType.DWORD,
                Loc.T("Минимальная задержка таймера мультимедиа-профиля"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "MaxFrameLatency", 1, ValueType.DWORD,
                Loc.T("Минимальная задержка кадра мультимедиа-профиля"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "IdleDisableOverride", 1, ValueType.DWORD,
                Loc.T("Запрет перевода потоков мультимедиа-профиля в простой"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "Affinity Policy", 2, ValueType.DWORD,
                Loc.T("Политика привязки потоков мультимедиа-профиля к ядрам CPU"), Loc.T("Мультимедиа-профиль системы")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "Background Processing Priority", 0, ValueType.DWORD,
                Loc.T("Мультимедиа-профиль не считается фоновой задачей"), Loc.T("Мультимедиа-профиль системы")),

            // Приоритет игрового процесса
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8, ValueType.DWORD,
                Loc.T("Повышение приоритета GPU-планировщика для игрового процесса"), Loc.T("Приоритет игрового процесса")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "High", ValueType.SZ,
                Loc.T("Категория CPU-планирования 'High' для игрового процесса"), Loc.T("Приоритет игрового процесса")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", 6, ValueType.DWORD,
                Loc.T("Повышенный базовый приоритет потоков игрового процесса (MMCSS)"), Loc.T("Приоритет игрового процесса")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "SFIO Priority", "High", ValueType.SZ,
                Loc.T("Высокий приоритет дискового ввода-вывода для игрового процесса"), Loc.T("Приоритет игрового процесса")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Background Only", "False", ValueType.SZ,
                Loc.T("Игровой процесс не считается фоновой задачей планировщика MMCSS"), Loc.T("Приоритет игрового процесса")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Latency Sensitive", "True", ValueType.SZ,
                Loc.T("Флаг чувствительности к задержкам для игрового потока MMCSS"), Loc.T("Приоритет игрового процесса")),

            // Графика и DirectX
            // HAGS удалён 06.09.2026: настройка — лотерея GPU/драйвер/игра
            // (на части систем режет FPS, на других добавляет), универсального
            // совета не существует; после инцидента с CS2 решено не предлагать
            // вовсе — человек переключает её в настройках Windows сам.
            // FlipNoVsync исключён для профиля GTA с 2026-10-04: отключение
            // VSync на уровне Direct3D даёт мигание/тиринг в оконном режиме
            // GTA V (Majestic), откат — только после перезагрузки (HKLM).
            // Для остальных профилей остаётся по умолчанию.
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Direct3D", "FlipNoVsync", 1, ValueType.DWORD,
                Loc.T("Отключение принудительной синхронизации кадров Direct3D (VSync)"), Loc.T("Графика и DirectX"),
                ExcludeProfiles: ["gta"]),
            new(Hive.HKLM, @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "FlipNoVsync", 1, ValueType.DWORD,
                Loc.T("Отключение принудительной синхронизации кадров Direct3D (32-битные приложения)"), Loc.T("Графика и DirectX"),
                ExcludeProfiles: ["gta"]),

            // Сеть
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), ValueType.DWORD,
                Loc.T("Снятие троттлинга сети для мультимедиа/игровых процессов"), Loc.T("Сеть")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, ValueType.DWORD,
                Loc.T("Отключение фоновой P2P-раздачи обновлений другим ПК (Delivery Optimization)"), Loc.T("Сеть")),

            // Телеметрия и отчёты
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, ValueType.DWORD,
                Loc.T("Отключение сбора телеметрии Windows (Data Collection)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы телеметрии DiagTrack (Connected User Experiences)"), Loc.T("Телеметрия и отчёты"),
                ExcludeProfiles: ["gta"]),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы push-сообщений WAP (dmwappushservice)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, ValueType.DWORD,
                Loc.T("Отключение отправки отчётов об ошибках (Windows Error Reporting)"), Loc.T("Телеметрия и отчёты")),

            // Ниже — телеметрия и приватность, перенесённые из разбора чужого
            // оптимизатора (папка снимков PrdvApp\backups, 03.09.2026).
            //
            // ПОЧЕМУ ЭТО ЗДЕСЬ ВАЖНО. Снимок чужой программы хранит только путь
            // и ИСХОДНОЕ значение, а не то, что она пишет. Поэтому каждый путь
            // сверен с ADMX самой Microsoft на этой машине
            // (C:\Windows\PolicyDefinitions, Windows 10 22H2): оттуда взяты и
            // класс политики (Machine = HKLM), и точное имя значения, и то,
            // какое число означает «выключено».
            //
            // Сверка нашла три ловушки, из-за которых чужие твики не работали:
            //   • LetAppsAccessBluetoothSync   такой политики НЕТ вообще
            //                                  (в ADMX есть LetAppsSyncWithDevices)
            //   • LetAppsAccessDiagnosticInfo   имя другое: LetAppsGetDiagnosticInfo
            //   • AllowSpeechModelUpdate        ветвь Policies\Microsoft\Speech,
            //                                  а не Policies\Microsoft\Windows\Speech
            //   • DisableInventory              ветвь Policies\...\Windows\AppCompat,
            //                                  а не CurrentVersion\AppCompatFlags
            //   • DisableLocation               ветвь Policies\...\LocationAndSensors,
            //                                  а не CurrentVersion\LocationAndSensors
            // Записывать такое — создавать ключ, который никто не читает, и
            // показывать человеку настройку, которая ничего не делает.
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0, ValueType.DWORD,
                Loc.T("Отключение телеметрии влияния приложений (Application Impact Telemetry)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1, ValueType.DWORD,
                Loc.T("Отключение сбора списка установленных программ (Inventory Collector)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0, ValueType.DWORD,
                Loc.T("Отключение программы улучшения качества ПО (CEIP/SQM)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, ValueType.DWORD,
                Loc.T("Отключение запросов отзывов о Windows (Feedback Hub)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, ValueType.DWORD,
                Loc.T("Отключение сбора истории действий (лента активности)"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, ValueType.DWORD,
                Loc.T("Запрет записи истории запусков приложений"), Loc.T("Телеметрия и отчёты")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, ValueType.DWORD,
                Loc.T("Запрет отправки истории действий в облако Microsoft"), Loc.T("Телеметрия и отчёты")),

            // Приватность — доступ приложений
            //
            // Одна ветвь политики AppPrivacy, одно значение на каждое право.
            // Число 2 — не «включить/выключить», а третий пункт списка:
            // 0 = решает пользователь, 1 = принудительно разрешить,
            // 2 = принудительно запретить (ADMX AppPrivacy.admx, enum ForceDeny).
            //
            // Границы честные: политика гасит право у приложений Магазина и у
            // системных компонентов, а не у обычных программ и не у игры.
            // Каждый пункт выключается отдельной галочкой — тому, кому нужен
            // «Связь с телефоном» или Xbox-приложение, достаточно снять её.
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessAccountInfo", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям читать данные учётной записи"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessCalendar", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к календарю"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessCallHistory", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к журналу вызовов"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessContacts", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к контактам"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessEmail", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к электронной почте"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessLocation", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к местоположению"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMessaging", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к сообщениям (SMS/MMS)"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessMotion", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям доступа к датчику движения"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessPhone", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям управлять звонками"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsAccessRadios", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям управлять радиомодулями (Wi-Fi, Bluetooth)"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsSyncWithDevices", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям обмениваться данными с непарными устройствами"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsGetDiagnosticInfo", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям читать диагностику других приложений"), Loc.T("Приватность — доступ приложений")),
            // Cortana / голосовая активация: те же 0/1/2, что и остальные
            // AppPrivacy. На заблокированном экране голос не должен будить
            // приложения — это и батарея, и лишняя фоновая работа микрофона.
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoice", 2, ValueType.DWORD,
                Loc.T("Запрет приложениям активироваться голосом (Cortana и голосовые агенты)"), Loc.T("Приватность — доступ приложений")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsActivateWithVoiceAboveLock", 2, ValueType.DWORD,
                Loc.T("Запрет голосовой активации приложений на экране блокировки"), Loc.T("Приватность — доступ приложений")),

            // Приватность Windows
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, ValueType.DWORD,
                Loc.T("Отключение рекламного идентификатора Windows"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization", 0, ValueType.DWORD,
                Loc.T("Отключение сбора образцов текста и рукописного ввода"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Speech", "AllowSpeechModelUpdate", 0, ValueType.DWORD,
                Loc.T("Отключение загрузки речевых моделей распознавания"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1, ValueType.DWORD,
                Loc.T("Отключение службы определения местоположения Windows"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowSearchToUseLocation", 0, ValueType.DWORD,
                Loc.T("Запрет поиску Windows использовать местоположение"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 2, ValueType.DWORD,
                Loc.T("Отключение синхронизации настроек с аккаунтом Microsoft"), Loc.T("Приватность Windows")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1, ValueType.DWORD,
                Loc.T("Отключение экрана блокировки Windows (минус фоновые слайды и виджеты)"), Loc.T("Приватность Windows")),

            // Фоновые приложения и реклама
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, ValueType.DWORD,
                Loc.T("Запрет фоновой работы UWP-приложений (Background Apps)"), Loc.T("Фоновые приложения и реклама")),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0, ValueType.DWORD,
                Loc.T("Запрет фоновой автоустановки рекомендуемых приложений"), Loc.T("Фоновые приложения и реклама")),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "ContentDeliveryAllowed", 0, ValueType.DWORD,
                Loc.T("Отключение фоновой доставки контента (Content Delivery Manager)"), Loc.T("Фоновые приложения и реклама")),
            // Машинная политика вместо пользовательской ветви Spotlight:
            // DisableWindowsSpotlightFeatures в ADMX объявлен только для
            // класса User, то есть живёт в HKCU и откатился бы вместе с
            // сессионными настройками после выхода из игры.
            // DisableWindowsConsumerFeatures — класс Machine, держится всегда
            // и закрывает главное: тихую доустановку рекламных приложений.
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, ValueType.DWORD,
                Loc.T("Запрет тихой автоустановки рекламных приложений (Consumer Features)"), Loc.T("Фоновые приложения и реклама")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение фонового ускорения запуска Microsoft Edge"), Loc.T("Фоновые приложения и реклама")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, ValueType.DWORD,
                Loc.T("Запрет работы Microsoft Edge в фоне после закрытия окна"), Loc.T("Фоновые приложения и реклама")),

            // Фоновые службы Windows
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы индексации файлов Windows Search"), Loc.T("Фоновые службы Windows")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\MapsBroker", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение фонового менеджера загрузки карт (MapsBroker)"), Loc.T("Фоновые службы Windows")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\WerSvc", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы отчётов об ошибках (WerSvc)"), Loc.T("Фоновые службы Windows")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\WMPNetworkSvc", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы общего доступа Windows Media Player по сети"), Loc.T("Фоновые службы Windows")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\PcaSvc", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение помощника совместимости программ (PcaSvc)"), Loc.T("Фоновые службы Windows"),
                ExcludeProfiles: ["gta"]),

            // Службы Xbox
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\XblAuthManager", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы авторизации Xbox Live (XblAuthManager)"), Loc.T("Службы Xbox")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\XblGameSave", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы сохранений Xbox Live (XblGameSave)"), Loc.T("Службы Xbox")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\XboxNetApiSvc", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение сетевой службы Xbox Live (XboxNetApiSvc)"), Loc.T("Службы Xbox")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\XboxGipSvc", "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы периферии Xbox (XboxGipSvc)"), Loc.T("Службы Xbox")),

            // Видеокарта NVIDIA
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "RmGpsPsEnablePerCpuCoreDpc", 1, ValueType.DWORD,
                Loc.T("NVIDIA: обработка DPC-прерываний драйвера по всем ядрам CPU (меньше микрофризов)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Power", "RmGpsPsEnablePerCpuCoreDpc", 1, ValueType.DWORD,
                Loc.T("NVIDIA: обработка DPC-прерываний по всем ядрам CPU (энергоблок драйвера)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm", "RmGpsPsEnablePerCpuCoreDpc", 1, ValueType.DWORD,
                Loc.T("NVIDIA: обработка DPC-прерываний по всем ядрам CPU (служба драйвера)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\NVAPI", "RmGpsPsEnablePerCpuCoreDpc", 1, ValueType.DWORD,
                Loc.T("NVIDIA: обработка DPC-прерываний по всем ядрам CPU (NVAPI)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak", "RmGpsPsEnablePerCpuCoreDpc", 1, ValueType.DWORD,
                Loc.T("NVIDIA: обработка DPC-прерываний по всем ядрам CPU (NVTweak)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm", "DisableWriteCombining", 0, ValueType.DWORD,
                Loc.T("NVIDIA: настройка объединения записи видеопамяти (снижение задержек)"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "DpiMapIommuContiguous", 1, ValueType.DWORD,
                Loc.T("NVIDIA: снижение задержек DPC драйвера видеокарты"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\nvlddmkm\FTS", "EnableRID61684", 1, ValueType.DWORD,
                Loc.T("NVIDIA: включение оптимизации планировщика драйвера"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),
            // P-state: драйвер сам переключает GPU между энергосбережением и
            // полной частотой. На десктопе это даёт просадки в момент, когда
            // сцена стала тяжелее — кадр ждёт, пока ядро поднимется. Ключ
            // читает nvlddmkm из класса дисплея (0000 = первый адаптер).
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DisableDynamicPstate", 1, ValueType.DWORD,
                Loc.T("NVIDIA: запрет динамических P-State — GPU не сбрасывает частоту в простое кадра"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),

            // Видеокарта AMD
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "EnableUlps", 0, ValueType.DWORD,
                Loc.T("AMD: отключение ULPS (Ultra Low Power State) — меньше просадок/статтеров"), Loc.T("Видеокарта AMD"), GpuVendor: "amd"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD", "Main3D_DEF", "1", ValueType.SZ,
                Loc.T("AMD: значение по умолчанию для количества буферизуемых кадров (1)"), Loc.T("Видеокарта AMD"), GpuVendor: "amd"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD", "Main3D", "1", ValueType.SZ,
                Loc.T("AMD: количество предварительно рендерящихся кадров (снижение задержки ввода)"), Loc.T("Видеокарта AMD"), GpuVendor: "amd"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000\UMD", "FlipQueueSize", "1", ValueType.SZ,
                Loc.T("AMD: размер очереди кадров (снижение задержки ввода)"), Loc.T("Видеокарта AMD"), GpuVendor: "amd"),

            // Процессор Intel
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, ValueType.DWORD,
                Loc.T("Intel: отключение динамического троттлинга питания процессора"), Loc.T("Процессор Intel"), CpuVendor: "intel"),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "CsEnabled", 0, ValueType.DWORD,
                Loc.T("Intel: отключение Modern Standby (меньше фоновый троттлинг вне игры)"), Loc.T("Процессор Intel"), CpuVendor: "intel"),

            // Процессор AMD
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, ValueType.DWORD,
                Loc.T("AMD: отключение динамического троттлинга питания процессора"), Loc.T("Процессор AMD"), CpuVendor: "amd"),
            new(Hive.HKLM,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583",
                "ValueMax", 0, ValueType.DWORD,
                Loc.T("AMD Ryzen: отключение парковки ядер (минимум активных ядер = 100%)"), Loc.T("Процессор AMD"), CpuVendor: "amd"),

            // ============================================================
            // Повышение FPS в игре
            // ============================================================

            // Прямой приоритет игрового процесса
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\GTA5.exe\PerfOptions",
                "CpuPriorityClass", 3, ValueType.DWORD,
                Loc.T("Повышение базового приоритета процесса GTA5.exe (High)"), Loc.T("FPS — приоритет игры")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\GTA5.exe\PerfOptions",
                "CpuAffinityMask", unchecked((int)0xFFFFFFFF), ValueType.DWORD,
                Loc.T("Привязка GTA5.exe ко всем доступным ядрам процессора"), Loc.T("FPS — приоритет игры")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\GTA5_Enhanced.exe\PerfOptions",
                "CpuPriorityClass", 3, ValueType.DWORD,
                Loc.T("Повышение базового приоритета процесса GTA5 Enhanced (High)"), Loc.T("FPS — приоритет игры")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ragemp_v.exe\PerfOptions",
                "CpuPriorityClass", 3, ValueType.DWORD,
                Loc.T("Повышение базового приоритета процесса RAGE MP (High)"), Loc.T("FPS — приоритет игры")),

            // Уменьшение задержки и фризов
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "DistributeTimers", 1, ValueType.DWORD,
                Loc.T("Распределение системных таймеров по ядрам — меньше фризов и микрофризов"), Loc.T("FPS — задержки и фризы")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                "HiberbootEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение гибернации и Hybrid Boot (меньше фоновая нагрузка на диск)"), Loc.T("FPS — задержки и фризы")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                "EnableSuperfetch", 0, ValueType.DWORD,
                Loc.T("Отключение Superfetch/Prefetch (меньше фоновая активность диска во время игры)"), Loc.T("FPS — задержки и фризы")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                "EnablePrefetcher", 0, ValueType.DWORD,
                Loc.T("Отключение Prefetcher (меньше фоновая активность диска во время игры)"), Loc.T("FPS — задержки и фризы")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\SysMain",
                "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы SysMain/Superfetch полностью"), Loc.T("FPS — задержки и фризы"),
                ExcludeProfiles: ["gta"]),

            // GPU — снижение задержки и стабильность FPS
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "PlatformSupportMiracast", 0, ValueType.DWORD,
                Loc.T("Отключение поддержки Miracast (беспроводные дисплеи) — меньше нагрузка на GPU"), Loc.T("FPS — GPU оптимизация")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "DisableOverlays", 1, ValueType.DWORD,
                Loc.T("Отключение встроенных оверлеев графики Windows"), Loc.T("FPS — GPU оптимизация")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "EnablePlatformSupportMiracast", 0, ValueType.DWORD,
                Loc.T("Отключение платформенной поддержки Miracast"), Loc.T("FPS — GPU оптимизация")),
            // ВАЖНО: сабкей ...\GraphicsDrivers\HwSchMode\ — мусорный: Windows
            // читает HwSchMode только из самого GraphicsDrivers (запись выше).
            // Писать HwSchMode во вложенный одноимённый сабкей — создание
            // фантомной ветки, которую не читает никто.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "TdrLevel", 0, ValueType.DWORD,
                Loc.T("Отключение восстановления драйвера по тайм-ауту TDR (меньше фризов в тяжёлых сценах)"), Loc.T("FPS — GPU оптимизация")),

            // Игровой процесс MMCSS — снижение задержки
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                "GPU Priority", 8, ValueType.DWORD,
                Loc.T("Максимальный приоритет GPU для игрового процесса (FPS)"), Loc.T("FPS — MMCSS игровой профиль")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                "Priority", 6, ValueType.DWORD,
                Loc.T("Максимальный приоритет CPU для игрового процесса (FPS)"), Loc.T("FPS — MMCSS игровой профиль")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                "Clock Rate", 10000, ValueType.DWORD,
                Loc.T("Максимальная тактовая частота мультимедиа-потоков игры (FPS)"), Loc.T("FPS — MMCSS игровой профиль")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                "Affinity Policy", 2, ValueType.DWORD,
                Loc.T("Политика привязки потоков игры к ядрам CPU (FPS)"), Loc.T("FPS — MMCSS игровой профиль")),

            // Память — больше FPS под большие сцены
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "FeatureSettings", 3, ValueType.DWORD,
                Loc.T("Профиль функций управления памятью для производительности игр"), Loc.T("FPS — память")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "FeatureSettingsOverride", 3, ValueType.DWORD,
                Loc.T("Переопределение профиля управления памятью для игр"), Loc.T("FPS — память")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "FeatureSettingsOverrideMask", 3, ValueType.DWORD,
                Loc.T("Маска переопределения профиля управления памятью"), Loc.T("FPS — память")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "ClearPageFileAtShutdown", 0, ValueType.DWORD,
                Loc.T("Отключение очистки файла подкачки при выключении (быстрее завершение работы)"), Loc.T("FPS — память")),

            // Сеть для игры (лаги и пинг)
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "TcpAckFrequency", 1, ValueType.DWORD,
                Loc.T("Мгновенное подтверждение TCP-пакетов (снижение пинга в игре)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "TCPNoDelay", 1, ValueType.DWORD,
                Loc.T("Отключение алгоритма Нэгла для TCP (мгновенная отправка пакетов, меньше пинга)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "TcpDelAckTicks", 0, ValueType.DWORD,
                Loc.T("Минимальная задержка подтверждения TCP (снижение пинга)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
                "DefaultTTL", 64, ValueType.DWORD,
                Loc.T("Оптимальный TTL для игровых пакетов"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider",
                "DnsPriority", 6, ValueType.DWORD,
                Loc.T("Приоритет DNS-резолвера (меньше задержек на входе в игру)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider",
                "HostsPriority", 6, ValueType.DWORD,
                Loc.T("Приоритет файла hosts (меньше задержек при подключении)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider",
                "LocalPriority", 4, ValueType.DWORD,
                Loc.T("Приоритет локальных имён (меньше задержек)"), Loc.T("FPS — сеть")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider",
                "NetbtPriority", 7, ValueType.DWORD,
                Loc.T("Приоритет NetBIOS (меньше задержек)"), Loc.T("FPS — сеть")),

            // Игровая планировка (Game Mode и Game Bar)
            new(Hive.HKCU, @"Software\Microsoft\GameBar",
                "AllowAutoGameMode", 1, ValueType.DWORD,
                Loc.T("Автоматическое включение Game Mode при запуске игры"), "FPS — Game Mode"),
            new(Hive.HKCU, @"Software\Microsoft\GameBar",
                "AutoGameModeEnabled", 1, ValueType.DWORD,
                Loc.T("Включение Game Mode для игр (приоритет GPU/CPU игре)"), "FPS — Game Mode"),
            new(Hive.HKCU, @"Software\Microsoft\GameBar",
                "ShowStartupPanel", 0, ValueType.DWORD,
                Loc.T("Отключение стартовой панели Game Bar"), "FPS — Game Mode"),
            new(Hive.HKCU, @"Software\Microsoft\GameBar",
                "UseNexusForGameBarEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение вызова Game Bar по Win+G"), "FPS — Game Mode"),
            new(Hive.HKCU, @"System\GameConfigStore",
                "GameDVR_Enabled", 0, ValueType.DWORD,
                Loc.T("Отключение Game DVR (снижение нагрузки на диск и CPU при игре)"), "FPS — Game Mode"),
            // FSO здесь тоже не трогаем — причина выше, в блоке "Запись и
            // оверлеи Windows": этот твик отключает возможность показать оверлей
            // поверх полноэкранной игры.
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "AppCaptureEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение захвата приложения Xbox (FPS)"), "FPS — Game Mode"),

            // ============================================================
            // FPS — визуальные эффекты и анимации Windows
            // ============================================================
            new(Hive.HKCU, @"Control Panel\Desktop",
                "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x01, 0x80, 0x10, 0x00, 0x00, 0x00 }, ValueType.SZ,
                Loc.T("Минимизация визуальных эффектов Windows (тени, плавность, анимации) для FPS"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Desktop",
                "DragFullWindows", 0, ValueType.SZ,
                Loc.T("Отключение отображения содержимого окна при перетаскивании (FPS)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Desktop",
                "FontSmoothing", 2, ValueType.SZ,
                Loc.T("Оставляем только ClearType-сглаживание шрифтов (минимум нагрузки GPU)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Desktop",
                "MenuShowDelay", 0, ValueType.SZ,
                Loc.T("Мгновенное открытие меню Windows (меньше ожидания)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Desktop\WindowMetrics",
                "MinAnimate", 0, ValueType.SZ,
                Loc.T("Отключение анимации сворачивания/разворачивания окон (FPS)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Desktop",
                "CursorBlinkRate", -1, ValueType.SZ,
                Loc.T("Отключение мигания курсора (минимальная фоновая нагрузка)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                "VisualFXSetting", 3, ValueType.DWORD,
                Loc.T("Режим «Лучшее быстродействие» для визуальных эффектов Windows"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Software\Microsoft\Windows\DWM",
                "EnableAeroPeek", 0, ValueType.DWORD,
                Loc.T("Отключение Aero Peek (предпросмотр окон на панели задач)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Mouse",
                "MouseSpeed", 0, ValueType.SZ,
                Loc.T("Отключение ускорения мыши (точнее ввод в игре)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Mouse",
                "MouseThreshold1", 0, ValueType.SZ,
                Loc.T("Порог ускорения мыши #1 (выкл)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Mouse",
                "MouseThreshold2", 0, ValueType.SZ,
                Loc.T("Порог ускорения мыши #2 (выкл)"), Loc.T("FPS — визуальные эффекты")),
            new(Hive.HKCU, @"Control Panel\Mouse",
                "MouseHoverTime", 10, ValueType.SZ,
                Loc.T("Минимальное время задержки наведения мыши"), Loc.T("FPS — визуальные эффекты")),

            // ============================================================
            // FPS — планировщик и процессор (доп.)
            // ============================================================
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                "IRQ8Priority", 1, ValueType.DWORD,
                Loc.T("Повышение приоритета прерываний системного таймера (IRQ8) для FPS"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                "ConvertLinesCount", 1, ValueType.DWORD,
                Loc.T("Оптимизация счётчика переключения контекста (меньше оверхеда)"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "KernelMUI", 1, ValueType.DWORD,
                Loc.T("Включение оптимизации MUI-ресурсов ядра (меньше оверхед)"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "SerializeTimerExpiration", 1, ValueType.DWORD,
                Loc.T("Сериализация истечения таймеров (меньше DPC-задержек, стабильнее FPS)"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "MultirateSvcEnabled", 1, ValueType.DWORD,
                Loc.T("Включение мультитарифного сервиса ядра (оптимизация потоков)"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "GlobalTimerResolutionRequests", 1, ValueType.DWORD,
                Loc.T("Win11 делает запросы таймера игры личным делом её процесса; этот ключ возвращает глобальное действие (иначе твит таймера 1 мс — плацебо). Требует перезагрузки"), Loc.T("FPS — планировщик и процессор")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
                "BootDriverFlags", 4, ValueType.DWORD,
                Loc.T("Флаги загрузки драйверов для производительности"), Loc.T("FPS — планировщик и процессор")),

            // ============================================================
            // FPS — файл подкачки и дисковая подсистема
            // ============================================================
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagingFiles", new string[] { "" }, ValueType.SZ,
                Loc.T("Автоуправление файлом подкачки для оптимального размера во время игры"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "NonPagedPoolQuota", 0, ValueType.DWORD,
                Loc.T("Авто-квота NonPagedPool (меньше конфликтов памяти)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagedPoolQuota", 0, ValueType.DWORD,
                Loc.T("Авто-квота PagedPool (меньше конфликтов памяти)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "NonPagedPoolSize", 0, ValueType.DWORD,
                Loc.T("Авто-размер NonPagedPool (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagedPoolSize", 0, ValueType.DWORD,
                Loc.T("Авто-размер PagedPool (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "SessionPoolSize", 0, ValueType.DWORD,
                Loc.T("Авто-размер сессионного пула (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "SessionViewSize", 0, ValueType.DWORD,
                Loc.T("Авто-размер сессионного View (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "SystemPages", 0, ValueType.DWORD,
                Loc.T("Авто-управление системными страницами (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "HeapSegmentReserve", 0, ValueType.DWORD,
                Loc.T("Авто-резерв сегмента кучи (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "HeapSegmentCommit", 0, ValueType.DWORD,
                Loc.T("Авто-коммит сегмента кучи (меньше конфликтов)"), Loc.T("FPS — файл подкачки")),

            // ============================================================
            // FPS — отложенные фоновые задачи
            // ============================================================
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsStore\WindowsUpdate",
                "AutoDownload", 2, ValueType.DWORD,
                Loc.T("Запрет авто-загрузки обновлений Store во время игры"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU",
                "NoAutoUpdate", 1, ValueType.DWORD,
                Loc.T("Отключение автообновлений Windows (во время игры не лезет в сеть/диск)"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config",
                "DODownloadMode", 0, ValueType.DWORD,
                Loc.T("Отключение фоновой P2P-раздачи обновлений (Delivery Optimization)"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                "SetAutoRestartNotificationDisable", 1, ValueType.DWORD,
                Loc.T("Отключение авто-перезапуска для установки обновлений"), Loc.T("FPS — фоновые задачи")),
            // Драйверы через Windows Update — отдельная загрузка поверх
            // накопительного обновления. Во время игры это диск + CPU + сеть.
            // Политика ExcludeWUDriversInQualityUpdate (WindowsUpdate.admx,
            // enabled=1) оставляет установку драйверов производителю GPU.
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
                "ExcludeWUDriversInQualityUpdate", 1, ValueType.DWORD,
                Loc.T("Запрет подмешивать драйверы устройств в накопительные обновления Windows"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Device Metadata",
                "PreventDeviceMetadataFromNetwork", 1, ValueType.DWORD,
                Loc.T("Запрет фоновой загрузки иконок и описаний устройств из сети Microsoft"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\wuauserv",
                "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы центра обновления Windows на время игры"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\BITS",
                "Start", 4, ValueType.DWORD,
                Loc.T("Отключение фоновой интеллектуальной передачи (BITS)"), Loc.T("FPS — фоновые задачи")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\DoSvc",
                "Start", 4, ValueType.DWORD,
                Loc.T("Отключение службы оптимизации доставки (DoSvc)"), Loc.T("FPS — фоновые задачи")),

            // ============================================================
            // FPS — рендеринг и композитор
            // ============================================================
            // MPO (Multi-Plane Overlay): на части конфигураций драйвера именно
            // он даёт мерцание, чёрные экраны и статтеры; NVIDIA официально
            // рекомендовала его отключение. OverlayTestMode=5 — задокументированный
            // способ выключить MPO целиком.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                "OverlayTestMode", 5, ValueType.DWORD,
                Loc.T("Отключение Multi-Plane Overlay (MPO) — убирает мерцание и статтеры на части конфигураций"), Loc.T("FPS — рендеринг и композитор")),
            // «Оптимизации для оконных игр» Windows 11 — принудительно НЕ
            // включаем: массовые жалобы на просадку FPS вдвое в сетевых шутерах
            // (CS2 и др.) при этой глобальной настройке. Значение 0 запрещает
            // Windows молча переводить игру на flip-model (жалоба 05.09.2026).
            new(Hive.HKCU, @"Software\Microsoft\DirectX\UserGpuPreferences",
                "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=0;", ValueType.SZ,
                Loc.T("Отключение принудительных «оптимизаций для оконных игр» Windows 11 (на части систем режут FPS вдвое)"), Loc.T("FPS — рендеринг и композитор")),
            // Fault Tolerant Heap: Windows сам подменяет кучу у процесса,
            // который падал. Подмена жрёт CPU на каждом выделении памяти и
            // держит игру в списке HKLM\Software\Microsoft\FTH\State.
            // Документация Microsoft: Enabled=0, затем перезагрузка.
            // Не путать с отключением Defender — FTH не защита, а костыль
            // совместимости для старых программ.
            new(Hive.HKLM, @"SOFTWARE\Microsoft\FTH", "Enabled", 0, ValueType.DWORD,
                Loc.T("Отключение Fault Tolerant Heap (подмена кучи у «падавших» процессов — лишний CPU в игре)"), Loc.T("FPS — задержки и фризы")),

            // ============================================================
            // FPS — устройства ввода (мышь/клавиатура)
            // ============================================================
            // Очереди классовых драйверов ввода: меньше буфер по умолчанию (100) —
            // клик и движение доходят до игры за меньшее число проходов очереди.
            // Значение консервативное (32): при экстремальном опросе 8к Гц совсем
            // маленький буфер терял бы события.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Kbdclass\Parameters",
                "KeyboardDataQueueSize", 32, ValueType.DWORD,
                Loc.T("Уменьшение буфера очереди клавиатуры (ниже задержка ввода)"), Loc.T("FPS — устройства ввода")),
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\Mouclass\Parameters",
                "MouseDataQueueSize", 32, ValueType.DWORD,
                Loc.T("Уменьшение буфера очереди мыши (ниже задержка прицела)"), Loc.T("FPS — устройства ввода")),

            // ============================================================
            // FPS — рабочий стол и панель задач
            // ============================================================
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 0, ValueType.DWORD,
                Loc.T("Отключение прозрачности и акрила Windows (разгрузка композитора DWM)"), Loc.T("FPS — рабочий стол")),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds",
                "EnableFeeds", 0, ValueType.DWORD,
                Loc.T("Отключение ленты новостей и виджетов Windows (фоновая загрузка контента и оверлей панели задач)"), Loc.T("FPS — рабочий стол")),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarDa", 0, ValueType.DWORD,
                Loc.T("Скрытие кнопки виджетов на панели задач"), Loc.T("FPS — рабочий стол")),

            // Телеметрия видеодрайвера пишет и шлёт данные в фоне во время игры.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\NvTelemetryContainer",
                "Start", 4, ValueType.DWORD,
                Loc.T("NVIDIA: отключение фоновой службы телеметрии драйвера"), Loc.T("Видеокарта NVIDIA"), GpuVendor: "nvidia"),

            // ============================================================
            // ИНСТРУМЕНТЫ (OptIn) — по умолчанию выключены.
            //
            // Это НЕ часть профиля оптимизации. Каждая запись:
            //   • снижает защиту системы или меняет интерфейс, то есть
            //     решение за конкретным человеком, а не за программой;
            //   • применяется ТОЛЬКО если пользователь включил её на
            //     странице «Инструменты» (TweakSelection: OptIn-записи
            //     отбираются по EnabledTools, а не по DisabledTweaks);
            //   • попадает в журнал и откатывается тем же механизмом,
            //     что и остальные — отдельного кода применения нет.
            //
            // Пути сверены с ADMX на этой машине (10 22H2) и с текущим
            // реестром, как и основной перенос 03.09.2026.
            // ============================================================

            // --- Приватность и интерфейс пользователя (HKCU = сессия:
            //     действует, пока запущена игра, возвращается после) ---
            // StickyKeys: 506 = выключен хоткея пятикратного Shift
            // (510 — значение по умолчанию). Хранится строкой (REG_SZ).
            new(Hive.HKCU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506", ValueType.SZ,
                Loc.T("Отключение залипания клавиш и его хоткея (пятикратный Shift)"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Control Panel\Desktop", "JPEGImportQuality", 100, ValueType.DWORD,
                Loc.T("Обои рабочего стола без JPEG-сжатия"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, ValueType.DWORD,
                Loc.T("Запрет сайтам запрашивать список языков системы"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, ValueType.DWORD,
                Loc.T("Запрет фонового сбора вводимого текста"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, ValueType.DWORD,
                Loc.T("Запрет фонового сбора рукописного ввода"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\input\Settings", "InsightsEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение обучения ввода (словарь подсказок)"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, ValueType.DWORD,
                Loc.T("Отключение периодических запросов отзывов Windows"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, ValueType.DWORD,
                Loc.T("Отключение облачного распознавания речи"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps",
                "AgentActivationEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение голосовой активации приложений (Cortana)"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"SOFTWARE\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps",
                "AgentActivationOnLockScreenEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение голосовой активации на экране блокировки"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                "SubscribedContent-338388Enabled", 0, ValueType.DWORD,
                Loc.T("Отключение рекомендуемых приложений в меню Пуск"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowRecent", 0, ValueType.DWORD,
                Loc.T("Скрытие недавних файлов в меню Пуск"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowFrequent", 0, ValueType.DWORD,
                Loc.T("Скрытие часто используемых файлов в меню Пуск"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0, ValueType.DWORD,
                Loc.T("Скрытие кнопки «Представление задач» на панели задач"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 0, ValueType.DWORD,
                Loc.T("Скрытие кнопки «Чат» на панели задач"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings",
                "TaskbarEndTask", 1, ValueType.DWORD,
                Loc.T("Кнопка «Завершить задачу» правым кликом по приложению в панели задач"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            // Политика class=User (ADMX WindowsExplorer): работает только из
            // HKCU — поэтому здесь, а не в постоянном наборе.
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoRecentDocsHistory", 1, ValueType.DWORD,
                Loc.T("Отключение истории недавних документов"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, ValueType.DWORD,
                Loc.T("Отключение веб-результатов в поиске меню Пуск"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            // Обе политики ниже в ADMX объявлены ТОЛЬКО для класса User:
            // машинная запись их не читает (проверено на CloudContent.admx
            // и WindowsCopilot.admx этой машины).
            new(Hive.HKCU, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1, ValueType.DWORD,
                Loc.T("Отключение Windows Spotlight (реклама и советы на экране блокировки)"), Loc.T("Инструменты — интерфейс"), OptIn: true),
            new(Hive.HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, ValueType.DWORD,
                Loc.T("Отключение Windows Copilot"), Loc.T("Инструменты — интерфейс"), OptIn: true),

            // --- Защита системы: ОТКЛЮЧЕНИЕ (риск; каждая галочка —
            //     осознанное решение человека, по умолчанию выключено) ---
            // SmartScreen: ShellConfigureSmartScreen, 0 = выключен
            // (ADMX SmartScreen.admx, class=Machine).
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", 0, ValueType.DWORD,
                Loc.T("Отключение SmartScreen (проверка файлов и приложений)"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", 1, ValueType.DWORD,
                Loc.T("Отключение антивируса Microsoft Defender"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", 1, ValueType.DWORD,
                Loc.T("Отключение защиты в реальном времени Microsoft Defender"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
            new(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows Defender Security Center\Notifications", "DisableNotifications", 1, ValueType.DWORD,
                Loc.T("Отключение уведомлений Центра безопасности Windows"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
            // HVCI (изоляция ядра / «Целостность памяти») — заметная нагрузка
            // на CPU в играх на части конфигураций, но это защита от
            // kernel-эксплойтов. Отключать — только осознанно.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, ValueType.DWORD,
                Loc.T("Отключение изоляции ядра (HVCI / целостность памяти)"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
            // Автошифрование устройств (BitLocker Device Encryption) на
            // домашних Windows включается само. Игры не шифруют диск —
            // но отключение не шифрует уже зашифрованное и не защищает от
            // кражи. Инструмент для тех, кто понимает, зачем.
            new(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption", 1, ValueType.DWORD,
                Loc.T("Запрет автоматического шифрования дисков (BitLocker Device Encryption)"), Loc.T("Инструменты — защита (риск)"), OptIn: true),
        ],
        FileTweaks = [],
        CommandTools =
        [
            // HPET: принудительный платформенный таймер. Честная оговорка в
            // описании: на Windows 10/11 прирост не гарантирован, на части
            // систем добавляет задержку. Инструмент для тестов, не рецепт.
            new("hpet",
                Loc.T("Принудительное включение платформенного таймера HPET (для тестов; на большинстве систем выигрыша нет и может добавить задержку)"),
                Loc.T("Инструменты — система (команды)"),
                [new("bcdedit.exe", "/set useplatformclock true")],
                [new("bcdedit.exe", "/deletevalue useplatformclock")],
                RestoreToleratesFailure: true),

            // Схема питания: применяем «Максимальную производительность»,
            // прежнюю схему запоминаем в журнале ДО применения — иначе второе
            // применение записало бы в «исходные» нашу же схему.
            new("power-plan",
                Loc.T("Схема электропитания «Максимальная производительность» (прежняя запоминается и возвращается при отключении)"),
                Loc.T("Инструменты — система (команды)"),
                [new("powercfg.exe", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")],
                [new("powercfg.exe", "/setactive {STATE}")],
                CapturesState: true),

            // Задачи телеметрии в планировщике. Каждая — стандартная задача
            // Windows (сбор совместимости, CEIP, отчёты об ошибках);
            // отключение обратимое: те же задачи с /Enable.
            new("scheduler-telemetry",
                Loc.T("Отключение задач телеметрии в планировщике (совместимость, CEIP, отчёты об ошибках)"),
                Loc.T("Инструменты — система (команды)"),
                [
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Autochk\\Proxy\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\DiskDiagnostic\\Microsoft-Windows-DiskDiagnosticDataCollector\" /Disable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Windows Error Reporting\\QueueReporting\" /Disable"),
                ],
                [
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Autochk\\Proxy\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\DiskDiagnostic\\Microsoft-Windows-DiskDiagnosticDataCollector\" /Enable"),
                    new("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Windows Error Reporting\\QueueReporting\" /Enable"),
                ]),
        ]
    };
}
