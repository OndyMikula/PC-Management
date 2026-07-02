using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PCManager_App
{
    public partial class MainWindow : Window
    {
        // Token, který nám umožní běžící odpočet na pozadí kdykoliv přerušit (stornovat)
        private CancellationTokenSource? _actionCancelTokenSource;
        // Tvoje aktuální verze aplikace (podle ní C# pozná, jestli je na webu novější)
        private readonly string _currentVersion = "2.1";

        public MainWindow()
        {
            InitializeComponent();

            // Spuštění asynchronní inicializace WebView2 při startu okna
            _ = InitializeWebViewAsync();

            // SPUŠTĚNÍ KONTROLY AKTUALIZACÍ:
            // Spustí se na pozadí, takže to nezpomalí start samotné aplikace
            _ = CheckForUpdatesAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                // Cesta do AppData/Local/PCManager_Data pro ukládání cache a profilu prohlížeče
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManager_Data");

                if (!Directory.Exists(userDataFolder))
                {
                    Directory.CreateDirectory(userDataFolder);
                }

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                // 1. Přihlášení k odběru zpráv z JavaScriptu
                webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;

                // 2. Přihlášení k události po dokončení načtení HTML stránky (pro kontrolu hibernace)
                webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;

                // Extrakce schovaného HTML souboru z prostředků (Resources) aplikace na disk
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
                        MessageBox.Show("Nepodařilo se načíst uživatelské rozhraní z paměti aplikace.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Navigace na reálný lokální soubor (vyřeší problém s nefunkčním localStorage)
                webView.CoreWebView2.Navigate(htmlFilePath);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show("Ve tvém systému chybí WebView2 Runtime. Aplikace nemůže zobrazit rozhraní.", "Chybí komponenta", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nastala chyba při inicializaci okna: " + ex.Message, "Kritická chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Tato metoda se spustí sama, jakmile WebView úspěšně vykreslí tvé HTML + přepisuje na základě _currentVersion verzi do footeru v HTML
        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // 1. Zjistíme stav hibernace z registrů Windows
                bool isHibernateEnabled = IsRegistryHibernateEnabled();

                // Spustíme JS funkci uvnitř HTML a předáme jí výsledek (true/false)
                await webView.CoreWebView2.ExecuteScriptAsync($"checkHibernate({isHibernateEnabled.ToString().ToLower()});");


                // 2. PROPOJENÍ VERZE:
                // Najdeme element s třídou .footer-right a přepíšeme jeho obsah textem z C#
                string versionScript = $"var footer = document.querySelector('.footer-right'); if (footer) footer.textContent = 'PC Manager {_currentVersion}';";

                await webView.CoreWebView2.ExecuteScriptAsync(versionScript);
            }
        }

        // Tady zpracováváme vše, co pošleš z JavaScriptu pomocí window.chrome.webview.postMessage()
        // Tady zpracováváme vše, co pošleš z JavaScriptu pomocí window.chrome.webview.postMessage()
        private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // OPRAVA: Použití správné metody TryGetWebMessageAsString()
            string message = e.TryGetWebMessageAsString();

            // Pro jistotu ověříme, že zpráva není prázdná
            if (string.IsNullOrEmpty(message)) return;

            // PŘÍPAD A: Uživatel klikl na tlačítko Zrušit (Storno)
            if (message == "cancel_action")
            {
                if (_actionCancelTokenSource != null)
                {
                    _actionCancelTokenSource.Cancel(); // Zastaví běžící Task.Delay
                    MessageBox.Show("Plánovaná akce byla úspěšně zrušena.", "Oznámení", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            // PŘÍPAD B: Spuštění akce s časem. 
            // Očekává se, že z JS posíláš text ve formátu "akce:sekundy" (např. "exit:60" nebo "hibernate:0")
            if (message.Contains(":"))
            {
                string[] parts = message.Split(':');
                string actionType = parts[0]; // "exit" nebo "hibernate"

                if (int.TryParse(parts[1], out int delayInSeconds))
                {
                    if (delayInSeconds == 0)
                    {
                        // Okamžitá akce (bez odpočtu)
                        ExecuteImmediateAction(actionType);
                    }
                    else
                    {
                        // Spuštění bezpečného odpočtu na pozadí
                        await ExecuteDelayedActionAsync(actionType, delayInSeconds);
                    }
                }
            }
        }

        // Pomocná metoda pro okamžité vykonání příkazu
        private void ExecuteImmediateAction(string actionType)
        {
            if (actionType == "exit")
            {
                Application.Current.Shutdown();
            }
            else if (actionType == "hibernate")
            {
                System.Diagnostics.Process.Start("shutdown", "/h");
            }
        }

        // Metoda, která drží aplikaci naživu po dobu odpočtu a hlídá, zda nedošlo ke stornu
        private async Task ExecuteDelayedActionAsync(string actionType, int delayInSeconds)
        {
            // Pokud už nějaký odpočet běžel z dřívějška, pro jistotu ho zrušíme
            _actionCancelTokenSource?.Cancel();
            _actionCancelTokenSource = new CancellationTokenSource();
            var token = _actionCancelTokenSource.Token;

            try
            {
                // Aplikace zde asynchronně čeká (neblokuje okno ani UI)
                await Task.Delay(delayInSeconds * 1000, token);

                // Pokud čas vypršel a uživatel mezitím neklikl na storno, provedeme příkaz
                if (!token.IsCancellationRequested)
                {
                    ExecuteImmediateAction(actionType);
                }
            }
            catch (TaskCanceledException)
            {
                // Sem kód skočí ve chvíli, kdy uživatel klikne na Zrušit.
                // Výjimku zachytíme, aby aplikace nespadla. Nic dalšího tu dělat nemusíme.
            }
        }

        // Bezpečné přečtení registru Windows pro ověření stavu hibernace
        private bool IsRegistryHibernateEnabled()
        {
            try
            {
                object? value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 0);
                return value != null && (int)value == 1;
            }
            catch
            {
                // Pokud nemáme práva k registru nebo nastala chyba, raději vrátíme false (schováme možnost v UI)
                return false;
            }
        }

        #region Update
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCManager_Data");
                string skippedVersionPath = Path.Combine(userDataFolder, "skipped_version.txt");

                // 1. Pokud existuje soubor s přeskočenou verzí, načteme ji
                string skippedVersion = "";
                if (File.Exists(skippedVersionPath))
                {
                    skippedVersion = await File.ReadAllTextAsync(skippedVersionPath);
                }

                // 2. Dotážeme se GitHub API na nejnovější release
                using HttpClient client = new HttpClient();
                // GitHub striktně vyžaduje User-Agent hlavičku, jinak vrátí chybu 403
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PCManager_App");

                string json = await client.GetStringAsync("https://api.github.com/repos/OndyMikula/PC-Management/releases/latest");

                // 3. Vytáhneme z JSONu položku "tag_name"
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
                {
                    string latestVersion = tagElement.GetString() ?? "";
                    // Pokud tag na GitHubu pojmenuješ "v2.2", odmažeme "v", aby zůstalo jen "2.2"
                    latestVersion = latestVersion.Replace("v", "").Trim();

                    // Převedeme text na objekty Version pro bezpečné porovnání (aby např. 2.10 bylo bráno jako novější než 2.2)
                    Version current = new Version(_currentVersion);
                    Version latest = new Version(latestVersion);

                    // 4. Je na GitHubu novější verze?
                    if (latest > current)
                    {
                        // Pokud se tato nová verze shoduje s tou, kterou uživatel dříve zakázal, tiše skončíme
                        if (latestVersion == skippedVersion)
                        {
                            return;
                        }

                        // 5. Zobrazíme naše vlastní okno s checkboxem
                        var dialogResult = UpdateDialog.Show(this, latestVersion);

                        if (dialogResult.Result)
                        {
                            // Uživatel klikl na "Ano" -> Otevřeme mu GitHub stránku s releasy v prohlížeči
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "https://github.com/OndyMikula/PC-Management/releases",
                                UseShellExecute = true
                            });
                        }
                        else if (dialogResult.DontShowAgain)
                        {
                            // Uživatel klikl na "Ne" A ZÁROVEŇ zaškrtl "Příště nezobrazovat"
                            // Zapíšeme si TUTO konkrétní verzi do souboru. Dokud nevyjde jiná, okno se neukáže.
                            await File.WriteAllTextAsync(skippedVersionPath, latestVersion);
                        }
                    }
                }
            }
            catch
            {
                // Pokud selže internet nebo GitHub API, chybu ignorujeme, aby aplikace normálně běžela dál
            }
        }

        // Pomocná třída, která vytvoří pěkné WPF okénko s Checkboxem čistě pomocí C# kódu
        public static class UpdateDialog
        {
            public static (bool Result, bool DontShowAgain) Show(Window owner, string newVersion)
            {
                var window = new Window
                {
                    Title = "Dostupná aktualizace",
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

                var txt = new TextBlock
                {
                    Text = $"Dostupná nová verze {newVersion}! Chceš aplikaci aktualizovat?",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(txt, 0);

                var chk = new CheckBox
                {
                    Content = "Příště nezobrazovat",
                    Margin = new Thickness(0, 10, 0, 10),
                    FontSize = 12
                };
                Grid.SetRow(chk, 1);

                var stack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetRow(stack, 2);

                bool result = false;

                var btnYes = new Button { Content = "Ano", Width = 75, Height = 23, Margin = new Thickness(0, 0, 10, 0) };
                btnYes.Click += (s, e) => { result = true; window.Close(); };

                var btnNo = new Button { Content = "Ne", Width = 75, Height = 23 };
                btnNo.Click += (s, e) => { result = false; window.Close(); };

                stack.Children.Add(btnYes);
                stack.Children.Add(btnNo);

                grid.Children.Add(txt);
                grid.Children.Add(chk);
                grid.Children.Add(stack);

                window.Content = grid;
                window.ShowDialog();

                return (result, chk.IsChecked ?? false);
            }
        }
        #endregion
    }
}