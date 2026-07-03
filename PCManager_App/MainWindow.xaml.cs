#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace PCManager_App
{
    public partial class MainWindow : Window
    {
        /*2.1
        Přidáno upozornění na novou verzi!
        Jakýkoliv odpočet se nyní pravidelně vypisuje do konzole (od oznámení do několika minut až po oznámení každou vteřinu)

        Fixes:
        Aplikace je oddělena od Webview2 procesu ve Správci úloh
        Logo aplikace se zobrazuje v Průzkumníku souborů i na ploše
        Delay v řetězci nečekal na dokončení akce a spouštěl další příkaz.
        */

        private CancellationTokenSource? _chainCancellationTokenSource;
        private CancellationTokenSource? _hibernateCancellationTokenSource;

        // Zde definuješ verzi před kompilací. Skript ji pak sám doplní do .footer-right v HTML.
        private readonly string _currentVersion = "2.1";

        // Import nativní Windows funkce pro zamknutí obrazovky
        [DllImport("user32.dll")]
        public static extern void LockWorkStation();

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Okno je bezpečně vykreslené, můžeme zahájit operace
            _ = InitializeWebViewAsync();
            _ = CheckForUpdatesAsync();
        }

        #region WebView & Initialization
        private async Task InitializeWebViewAsync()
        {
            try
            {
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManager_Data");
                if (!Directory.Exists(userDataFolder)) Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
                webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;

                // TOTO JE KRITICKÉ: Musíme HTML extrahovat a spustit z disku, jinak Chromium zablokuje localStorage a JS spadne!
                string resourceName = "PCManager_App.shutdown-web.html";
                string htmlFilePath = Path.Combine(userDataFolder, "shutdown-web.html");

                using (Stream? stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (FileStream fs = new FileStream(htmlFilePath, FileMode.Create, FileAccess.Write))
                        {
                            await stream.CopyToAsync(fs);
                        }
                    }
                    else
                    {
                        MessageBox.Show("Nepodařilo se načíst uživatelské rozhraní z paměti.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                webView.CoreWebView2.Navigate(htmlFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba inicializace: {ex.Message}", "Kritická chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // 1. Odeslání verze do footeru
                string versionScript = $"var footer = document.querySelector('.footer-right'); if (footer) footer.textContent = 'PC Manager {_currentVersion}';";
                await webView.CoreWebView2.ExecuteScriptAsync(versionScript);

                // 2. Kontrola registrů na hibernaci
                bool isHibernateEnabled = IsRegistryHibernateEnabled();
                await webView.CoreWebView2.ExecuteScriptAsync($"checkHibernate({isHibernateEnabled.ToString().ToLower()});");
            }
        }
        #endregion

        #region JSON Parser & Execution
        private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string jsonMessage = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(jsonMessage)) return;

                using (JsonDocument doc = JsonDocument.Parse(jsonMessage))
                {
                    JsonElement root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    if (type == "single")
                    {
                        string actionId = root.GetProperty("actionId").GetString() ?? "";
                        JsonElement parameters = root.TryGetProperty("params", out var p) ? p : default;

                        await ExecuteActionAsync(actionId, parameters);
                    }
                    else if (type == "chain")
                    {
                        // Stopneme předchozí řetězec, pokud by běžel
                        _chainCancellationTokenSource?.Cancel();
                        _chainCancellationTokenSource = new CancellationTokenSource();
                        var token = _chainCancellationTokenSource.Token;

                        // Předáváme akce do Background Tasku a JSON data parsujeme znovu AŽ UVNITŘ,
                        // čímž zabráníme tomu, aby je nadřazený 'using' blok předčasně smazal z paměti!
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using (JsonDocument innerDoc = JsonDocument.Parse(jsonMessage))
                                {
                                    JsonElement innerRoot = innerDoc.RootElement;
                                    JsonElement items = innerRoot.GetProperty("items");

                                    foreach (JsonElement item in items.EnumerateArray())
                                    {
                                        if (token.IsCancellationRequested) break;

                                        string id = item.GetProperty("id").GetString() ?? "";
                                        JsonElement itemParams = item.TryGetProperty("params", out var ip) ? ip : default;

                                        // Spustí akci a POČKÁ na její dokončení (řeší bug s Delayem)
                                        await ExecuteActionAsync(id, itemParams, token);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Chain Execution Error: {ex.Message}");
                            }
                        }, token);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba v komunikaci s JS: {ex.Message}");
            }
        }

        //Funkcionalita buttonů 
        private async Task ExecuteActionAsync(string id, JsonElement parameters, CancellationToken token = default)
        {
            try //try catch pro zachycení jakékoliv chyby a vypsání jej do konzole (místo padání aplikace)
            {
                // --- 1. DETEKCE ODPOČTU / DELAYE ---
                if (id == "delay" || id == "wait")
                {
                    int seconds = 0;
                    if (parameters.ValueKind == JsonValueKind.Number) seconds = parameters.GetInt32();
                    else if (parameters.TryGetProperty("seconds", out var p)) seconds = p.GetInt32();

                    await ExecuteCountdownAsync(seconds, token);
                    return;
                }
                switch (id) //Samotná funkcionalita buttonů
                {
                    case "0": // Exit aplikace
                        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                        break;

                    case "1": // Časování (Vypnutí / Hibernace)
                        if (parameters.ValueKind != JsonValueKind.Undefined && parameters.ValueKind != JsonValueKind.Null)
                        {
                            int minutes = int.Parse(parameters.GetProperty("cas").GetString() ?? "0");
                            string subAction = parameters.GetProperty("akce").GetString() ?? "4";

                            if (subAction == "4")
                            {
                                RunSystemCommand("shutdown", $"-s -f -t {minutes * 60}");
                            }
                            else if (subAction == "5")
                            {
                                _hibernateCancellationTokenSource?.Cancel();
                                _hibernateCancellationTokenSource = new CancellationTokenSource();

                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.Delay(TimeSpan.FromMinutes(minutes), _hibernateCancellationTokenSource.Token);
                                        RunSystemCommand("shutdown", "-h");
                                    }
                                    catch (TaskCanceledException) { }
                                });
                            }
                        }
                        break;

                    case "2": // Zrušení
                        RunSystemCommand("shutdown", "-a");
                        _hibernateCancellationTokenSource?.Cancel(); // Storno odložené hibernace
                        _chainCancellationTokenSource?.Cancel();     // Storno běžícího delay/řetězce
                        break;

                    case "3": // Winget normální
                        RunSystemCommand("cmd.exe", "/c winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause", false, false);
                        break;

                    case "4": // Winget Admin (UAC vyskočí jen pro spuštění CMD, ne u každé aplikace)
                        RunSystemCommand("cmd.exe", "/c winget upgrade --all --include-unknown --accept-source-agreements --accept-package-agreements --silent & pause", true, false);
                        break;

                    case "5": // Zamknutí PC
                        LockWorkStation();
                        break;

                    case "6": // Bezpečný Delay v řetězci
                        if (parameters.ValueKind != JsonValueKind.Undefined && parameters.ValueKind != JsonValueKind.Null)
                        {
                            int delayMinutes = int.Parse(parameters.GetProperty("delay").GetString() ?? "0");
                            await Task.Delay(TimeSpan.FromMinutes(delayMinutes), token);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // Chytí jakýkoliv error (aplikace nespadne)
                // Vypíše kompletní "raw" chybu, přesně to co standardně vypisuje CMD (.ToString() obsahuje typ, zprávu i StackTrace)
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Během provádění akce '{id}' došlo k chybě:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();

                // POZNÁMKA: Pokud chceš, aby při chybě jedné akce skončil celý zbytek zaškrtnutého řetězce,
                // odkomentuj následující řádek 'throw;'. Pokud chceš, aby aplikace chybu jen ignorovala 
                // a hned zkusila další zaškrtnutou akci v pořadí, nechej to takto.
                // throw;
            }
        }

        private void RunSystemCommand(string fileName, string arguments, bool runAsAdmin = false, bool hiddenWindow = true)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true, // Naprosto nezbytné pro Single-File aplikaci!
                    CreateNoWindow = hiddenWindow,
                    WindowStyle = hiddenWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
                };

                if (runAsAdmin) psi.Verb = "runas";

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start process: {ex.Message}");
            }
        }

        private bool IsRegistryHibernateEnabled()
        {
            try
            {
                object? value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 0);
                return value != null && (int)value == 1;
            }
            catch { return false; }
        }

        //Odpočet do konzole
        private async Task ExecuteCountdownAsync(int totalSeconds, CancellationToken token)
        {
            int lastLoggedRemaining = -1;

            for (int remaining = totalSeconds; remaining >= 0; remaining--)
            {
                if (token.IsCancellationRequested) break;

                bool shouldLog = false;

                // 1. Vždy zalogovat úplný začátek a úplný konec
                if (remaining == totalSeconds || remaining == 0)
                {
                    shouldLog = true;
                }
                // 2. Pod 30 sekundami -> každou vteřinu
                else if (remaining < 15)
                {
                    shouldLog = true;
                }
                // 3. Pod 2 minutami (120s) -> každých 30 sekund
                else if (remaining < 120)
                {
                    if (remaining % 30 == 0) shouldLog = true;
                }
                // 4. Pod 10 minutami (600s) -> každých 5 minut (300s)
                else if (remaining < 600)
                {
                    if (remaining % 300 == 0) shouldLog = true;
                }
                // 5. Pod 30 minutami (1800s) i nad 30 minut -> každých 10 minut (600s)
                else
                {
                    if (remaining % 600 == 0) shouldLog = true;
                }

                // Pokud máme logovat a ještě jsme tento konkrétní čas nelogovali
                if (shouldLog && remaining != lastLoggedRemaining)
                {
                    TimeSpan t = TimeSpan.FromSeconds(remaining);
                    string timeFormatted = t.TotalHours >= 1
                        ? $"{((int)t.TotalHours)}h {t.Minutes}m {t.Seconds}s"
                        : $"{t.Minutes}m {t.Seconds}s";

                    Console.WriteLine($"[Odpočet] Do konce akce zbývá: {timeFormatted}");
                    lastLoggedRemaining = remaining;
                }

                // Počkej 1 sekundu do dalšího vyhodnocení
                if (remaining > 0)
                {
                    try
                    {
                        await Task.Delay(1000, token);
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine("[Odpočet] Odpočet byl přerušen uživatelem.");
                        break;
                    }
                }
            }
        }
        #endregion

        #region GitHub Update Logic
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string skippedVersionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManager_Data", "skipped_version.txt");
                string skippedVersion = File.Exists(skippedVersionPath) ? await File.ReadAllTextAsync(skippedVersionPath) : "";

                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PCManager_App");

                string json = await client.GetStringAsync("https://api.github.com/repos/OndyMikula/PC-Management/releases/latest");
                using JsonDocument doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
                {
                    string latestVersion = tagElement.GetString()?.Replace("v", "").Trim() ?? "";

                    if (new Version(latestVersion) > new Version(_currentVersion) && latestVersion != skippedVersion)
                    {
                        var dialogResult = UpdateDialog.Show(this, latestVersion);

                        if (dialogResult.Result)
                        {
                            RunSystemCommand("https://github.com/OndyMikula/PC-Management/releases", "");
                        }
                        else if (dialogResult.DontShowAgain)
                        {
                            await File.WriteAllTextAsync(skippedVersionPath, latestVersion);
                        }
                    }
                }
            }
            catch { /* Ignorováno v případě offline stavu */ }
        }

        public static class UpdateDialog
        {
            public static (bool Result, bool DontShowAgain) Show(Window owner, string newVersion)
            {
                var window = new Window
                {
                    Title = "Aktualizace",
                    Width = 380,
                    Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = owner,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = System.Windows.Media.Brushes.White
                };

                var grid = new Grid { Margin = new Thickness(15) };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var txt = new TextBlock { Text = $"Dostupná nová verze {newVersion}! Chceš aplikaci aktualizovat?", TextWrapping = TextWrapping.Wrap, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(txt, 0);

                var chk = new CheckBox { Content = "Příště nezobrazovat", Margin = new Thickness(0, 10, 0, 10), FontSize = 12 };
                Grid.SetRow(chk, 1);

                var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetRow(stack, 2);

                bool result = false;
                var btnYes = new Button { Content = "Ano", Width = 75, Height = 23, Margin = new Thickness(0, 0, 10, 0) };
                btnYes.Click += (s, e) => { result = true; window.Close(); };

                var btnNo = new Button { Content = "Ne", Width = 75, Height = 23 };
                btnNo.Click += (s, e) => { result = false; window.Close(); };

                stack.Children.Add(btnYes); stack.Children.Add(btnNo);
                grid.Children.Add(txt); grid.Children.Add(chk); grid.Children.Add(stack);

                window.Content = grid;
                window.ShowDialog();

                return (result, chk.IsChecked ?? false);
            }
        }
        #endregion
    }
}