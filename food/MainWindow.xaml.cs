using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using food.Models;
using food.Services;

namespace food
{
    public partial class MainWindow : Window
    {
        private FoodData _data;
        private readonly UpdateService _updater = new();

        // 左側目前選的分類（null = 全部餐廳）與展開中的分類
        private string? _selectedCategoryId;
        private readonly HashSet<string> _expanded = new();
        private bool _rebuildingCategories;
        private bool _fillingCombos;

        private const string NoneText = "（不指定）";

        public MainWindow()
        {
            InitializeComponent();
            // 小螢幕（例如筆電）上不要讓視窗超出可用範圍
            Height = Math.Min(Height, SystemParameters.WorkArea.Height - 20);
            Width = Math.Min(Width, SystemParameters.WorkArea.Width - 20);

            _data = DataStore.Load();
            foreach (var country in CategoryService.Children(_data, null)) _expanded.Add(country.Id);

            RatingBox.ItemsSource = Enumerable.Range(0, 6).Reverse()
                .Select(n => new RatingOption(n, n == 0 ? "未評分" : new string('★', n) + new string('☆', 5 - n)))
                .ToList();

            RebuildCategories();
            RefreshList();
            ClearForm();
            UpdateStatus();
            ThemeService.ThemeChanged += OnThemeChanged; // 切換主題（或系統深淺色改變）時更新標題列與按鈕
            UpdateThemeButton();
        }

        // ---------- 主題：跟隨系統 / 淺色 / 深色 ----------

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.Cycle();
            StatusText.Text = $"主題：{ThemeName(ThemeService.Mode)}";
        }

        private static string ThemeName(AppTheme mode) => mode switch
        {
            AppTheme.Light => "淺色",
            AppTheme.Dark => "深色",
            _ => "跟隨系統",
        };

        private void OnThemeChanged()
        {
            UpdateThemeButton();
            WindowTheme.ApplyTitleBar(this);
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Text = ThemeService.Mode switch
            {
                AppTheme.Light => "\uE706", // 太陽
                AppTheme.Dark => "\uE708",  // 月亮
                _ => "\uE770",              // 電腦（跟隨系統）
            };
            ThemeButton.ToolTip = $"主題：{ThemeName(ThemeService.Mode)}（按一下切換）";
        }

        // ---------- Windows 11：標題列底色與視窗背景同色，看起來是一整片 ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowTheme.ApplyTitleBar(this);
        }

        // ---------- 啟動時檢查更新（詢問使用者） ----------

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CleanupService.RunInBackground(); // 清掉更新後遺留的舊檔
            DesktopShortcutService.TidyUp(_updater.IsInstalled);     // 更新後：重複捷徑只留最新的、工作列釘選改指向新版
            DesktopShortcutService.EnsureOnce(_updater.IsInstalled); // 安裝版第一次開啟時補上桌面捷徑

            await CheckForUpdateAsync(manual: false);
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DesktopShortcutService.Create(_updater.IsInstalled) == ShortcutResult.SourceNotFound)
                {
                    MessageBox.Show("找不到開始功能表裡的「餐廳收藏」捷徑，請重新執行「安裝.cmd」。", "桌面捷徑",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                StatusText.Text = "已在桌面建立捷徑";
                MessageBox.Show("已在桌面建立捷徑。", "桌面捷徑", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or COMException)
            {
                MessageBox.Show($"建立捷徑失敗：{ex.Message}", "桌面捷徑", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdateAsync(manual: true);

        private async Task CheckForUpdateAsync(bool manual)
        {
            if (!_updater.IsInstalled)
            {
                if (manual)
                    MessageBox.Show("目前是開發版（不是從安裝檔安裝的），無法線上更新。", "檢查更新");
                return;
            }

            try
            {
                var info = await _updater.CheckAsync();
                if (info == null)
                {
                    if (manual) MessageBox.Show($"目前已是最新版（v{_updater.CurrentVersion}）。", "檢查更新");
                    return;
                }

                var answer = MessageBox.Show(
                    $"有新版本 v{info.Version}（目前 v{_updater.CurrentVersion}）。\n要現在下載並更新嗎？",
                    "發現新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;

                StatusText.Text = "正在下載更新…";
                await _updater.DownloadAndLaunchAsync(info, p => Dispatcher.Invoke(() => StatusText.Text = $"正在下載更新… {p}%"));
                // 安裝程式已啟動，結束本程式讓它能覆蓋檔案；安裝完成後會自動重新開啟
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatus();
                // 啟動時的自動檢查失敗（例如沒網路）不打擾使用者
                if (manual) MessageBox.Show($"檢查更新失敗：{ex.Message}", "檢查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- 左側分類清單 ----------

        /// <summary>依目前資料重建左側分類清單（只列出展開中的層級），並保留原本的選取。</summary>
        private void RebuildCategories()
        {
            if (CategoryService.Get(_data, _selectedCategoryId) == null) _selectedCategoryId = null; // 被刪掉了 → 回到全部
            foreach (var c in CategoryService.PathOf(_data, _selectedCategoryId).SkipLast(1)) _expanded.Add(c.Id);

            var nodes = new List<CategoryNode> { new() { Id = null, Name = "全部餐廳", Count = _data.Restaurants.Count } };
            void Walk(Category c, int level)
            {
                var children = CategoryService.Children(_data, c.Id).ToList();
                bool open = _expanded.Contains(c.Id);
                nodes.Add(new CategoryNode
                {
                    Id = c.Id,
                    Name = c.Name,
                    Level = level,
                    Count = CategoryService.RestaurantsUnder(_data, c.Id).Count(),
                    HasChildren = children.Count > 0,
                    IsExpanded = open,
                });
                if (open) foreach (var child in children) Walk(child, level + 1);
            }
            foreach (var country in CategoryService.Children(_data, null).ToList()) Walk(country, 0);

            _rebuildingCategories = true;
            CategoryList.ItemsSource = nodes;
            CategoryList.SelectedItem = nodes.First(n => n.Id == _selectedCategoryId);
            _rebuildingCategories = false;
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rebuildingCategories || CategoryList.SelectedItem is not CategoryNode node) return;
            bool hadSelection = RestaurantList.SelectedItem != null;
            _selectedCategoryId = node.Id;
            RefreshList();
            if (RestaurantList.SelectedItem != null) return;
            // 原本選的餐廳不在這個分類裡 → 清空表單，準備在這個分類新增
            if (hadSelection) ClearForm();
            else SetFormCategory(_selectedCategoryId);
        }

        private void ToggleExpanded(string id)
        {
            if (!_expanded.Remove(id)) _expanded.Add(id);
            RebuildCategories();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CategoryNode { Id: { } id } }) ToggleExpanded(id);
        }

        private void CategoryList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CategoryList.SelectedItem is CategoryNode { Id: { } id, HasChildren: true }) ToggleExpanded(id);
        }

        private static CategoryNode? NodeOf(object sender) =>
            sender is FrameworkElement { DataContext: CategoryNode { IsCategory: true } node } ? node : null;

        private void AddCountry_Click(object sender, RoutedEventArgs e) => AddCategory(null);

        private void AddChild_Click(object sender, RoutedEventArgs e)
        {
            if (NodeOf(sender) is { CanAddChild: true } node) AddCategory(node.Id);
        }

        private void RenameCategory_Click(object sender, RoutedEventArgs e)
        {
            if (NodeOf(sender) is { } node) RenameCategory(node.Id!);
        }

        private void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (NodeOf(sender) is { } node) DeleteCategory(node.Id!);
        }

        private string? NameMessage(string? parentId, string name, string? renaming = null) =>
            CategoryService.Validate(_data, parentId, name, renaming) switch
            {
                CategoryNameError.Empty => "請輸入名稱。",
                CategoryNameError.TooLong => $"名稱最多 {CategoryService.MaxNameLength} 個字。",
                CategoryNameError.Duplicate => "這裡已經有同名的分類了。",
                _ => null,
            };

        private void AddCategory(string? parentId)
        {
            int level = parentId == null ? 0 : CategoryService.Level(_data, parentId) + 1;
            var levelName = CategoryService.LevelName(level);
            var prompt = parentId == null ? $"{levelName}名稱" : $"{levelName}名稱（位於：{CategoryService.PathText(_data, parentId)}）";
            var name = InputDialog.Ask(this, $"新增{levelName}", prompt, "", n => NameMessage(parentId, n));
            if (name == null) return;

            var category = CategoryService.Add(_data, parentId, name);
            if (parentId != null) _expanded.Add(parentId);
            _selectedCategoryId = category.Id; // 建好就切過去
            PersistAndRefresh();
            if (RestaurantList.SelectedItem == null) SetFormCategory(category.Id); // 接著新增的餐廳直接放進這個分類
            StatusText.Text = $"已新增{levelName}「{category.Name}」";
        }

        private void RenameCategory(string id)
        {
            var category = CategoryService.Get(_data, id)!;
            var levelName = CategoryService.LevelName(CategoryService.Level(_data, id));
            var name = InputDialog.Ask(this, $"重新命名{levelName}", $"{levelName}名稱", category.Name,
                n => NameMessage(category.ParentId, n, id));
            if (name == null || name == category.Name) return;
            CategoryService.Rename(_data, id, name);
            PersistAndRefresh();
            StatusText.Text = $"已重新命名為「{category.Name}」";
        }

        private void DeleteCategory(string id)
        {
            var category = CategoryService.Get(_data, id)!;
            var levelName = CategoryService.LevelName(CategoryService.Level(_data, id));
            var preview = CategoryService.Preview(_data, id);

            var message = $"確定要刪除{levelName}「{category.Name}」嗎？";
            if (preview.Categories > 1) message += $"\n\n底下 {preview.Categories - 1} 個子分類會一起刪除。";
            if (preview.MovedTo != null && preview.Moved > 0)
                message += $"\n底下 {preview.Moved} 間餐廳會移到「{preview.MovedTo.Name}」。";
            if (preview.Deleted > 0) message += $"\n\n⚠ 底下 {preview.Deleted} 間餐廳也會一起刪除！";

            var ok = MessageBox.Show(this, message, $"刪除{levelName}", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;

            var result = CategoryService.Delete(_data, id);
            if (_selectedCategoryId != null && CategoryService.Get(_data, _selectedCategoryId) == null)
                _selectedCategoryId = result.MovedTo?.Id;
            PersistAndRefresh();
            StatusText.Text = $"已刪除「{category.Name}」";
        }

        /// <summary>從右鍵點到的位置往上找出清單項目（點在空白處則為 null）。</summary>
        private static T? ItemUnderMouse<T>(ContextMenuEventArgs e) where T : class
        {
            for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is ListBoxItem { DataContext: T item }) return item;
            return null;
        }

        private static MenuItem MenuEntry(string header, Action onClick, Brush? foreground = null)
        {
            var mi = new MenuItem { Header = header };
            if (foreground != null) mi.Foreground = foreground;
            mi.Click += (_, _) => onClick();
            return mi;
        }

        // 分類右鍵：新增下一層 / 重新命名 / 刪除；在「全部餐廳」或空白處：新增國家
        private void CategoryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var node = ItemUnderMouse<CategoryNode>(e);
            var menu = CategoryList.ContextMenu;
            menu.Items.Clear();
            if (node is not { Id: { } id })
            {
                menu.Items.Add(MenuEntry("新增國家", () => AddCategory(null)));
                return;
            }
            if (node.CanAddChild) menu.Items.Add(MenuEntry(node.AddTip, () => AddCategory(id)));
            menu.Items.Add(MenuEntry("重新命名", () => RenameCategory(id)));
            menu.Items.Add(MenuEntry("刪除", () => DeleteCategory(id), (Brush)FindResource("DangerBrush")));
        }

        // ---------- 中間餐廳清單：搜尋 / 選取 ----------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        /// <summary>清單只顯示左側選的分類（含子分類）裡、符合搜尋文字的餐廳，新加的在上面。</summary>
        private void RefreshList()
        {
            var selectedId = (RestaurantList.SelectedItem as RestaurantRow)?.Model.Id;
            var keyword = SearchBox.Text.Trim();
            var rows = CategoryService.RestaurantsUnder(_data, _selectedCategoryId)
                .Select(r => new RestaurantRow { Model = r, PathText = CategoryService.PathText(_data, r.CategoryId) })
                .Where(r => keyword.Length == 0 ||
                    new[] { r.Model.Name, r.Model.Address, r.Model.Phone, r.Model.Note, r.PathText }
                        .Any(s => s.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(r => r.Model.CreatedAt)
                .ToList();

            RestaurantList.ItemsSource = rows;
            RestaurantList.SelectedItem = rows.FirstOrDefault(r => r.Model.Id == selectedId);

            BreadcrumbText.Text = _selectedCategoryId == null
                ? $"全部餐廳 · {rows.Count} 間"
                : $"{CategoryService.PathText(_data, _selectedCategoryId)} · {rows.Count} 間";
            EmptyText.Text = _data.Categories.Count == 0
                ? "還沒有任何分類。\n先按左上角「新增國家」開始吧！"
                : keyword.Length > 0
                    ? "找不到符合的餐廳。"
                    : "這裡還沒有餐廳。\n在右側填好資料後按「新增」。";
        }

        private Restaurant? SelectedRestaurant => (RestaurantList.SelectedItem as RestaurantRow)?.Model;

        private void RestaurantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedRestaurant is { } r) FillForm(r);
        }

        // ---------- 右側表單 ----------

        private void FillForm(Restaurant r)
        {
            NameBox.Text = r.Name;
            AddressBox.Text = r.Address;
            PhoneBox.Text = r.Phone;
            RatingBox.SelectedValue = r.Rating;
            NoteBox.Text = r.Note;
            SetFormCategory(r.CategoryId);
        }

        private void ClearForm()
        {
            NameBox.Clear();
            AddressBox.Clear();
            PhoneBox.Clear();
            RatingBox.SelectedValue = 0;
            NoteBox.Clear();
            SetFormCategory(_selectedCategoryId);
        }

        private void ClearForm_Click(object sender, RoutedEventArgs e)
        {
            RestaurantList.SelectedItem = null;
            ClearForm();
            NameBox.Focus();
        }

        /// <summary>把三個下拉選單設成這個分類的路徑；沒有指定時選第一個國家。</summary>
        private void SetFormCategory(string? categoryId)
        {
            var path = CategoryService.PathOf(_data, categoryId).Select(c => c.Id).ToList();
            string? At(int i) => i < path.Count ? path[i] : null;
            _fillingCombos = true;
            FillCombo(CountryBox, CategoryService.Children(_data, null), includeNone: false, At(0));
            FillChildCombo(CityBox, CountryBox, At(1));
            FillChildCombo(DistrictBox, CityBox, At(2));
            _fillingCombos = false;
        }

        private static void FillCombo(ComboBox box, IEnumerable<Category> categories, bool includeNone, string? select)
        {
            var options = categories.Select(c => new CategoryOption(c.Id, c.Name)).ToList();
            if (includeNone) options.Insert(0, new CategoryOption("", NoneText));
            box.ItemsSource = options;
            box.SelectedValue = options.Any(o => o.Id == select) ? select : options.FirstOrDefault()?.Id;
            box.IsEnabled = options.Count > (includeNone ? 1 : 0);
        }

        private void FillChildCombo(ComboBox box, ComboBox parentBox, string? select)
        {
            var parentId = parentBox.SelectedValue as string;
            var children = string.IsNullOrEmpty(parentId) ? [] : CategoryService.Children(_data, parentId);
            FillCombo(box, children, includeNone: true, select);
        }

        private void CountryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingCombos) return;
            _fillingCombos = true;
            FillChildCombo(CityBox, CountryBox, null);
            FillChildCombo(DistrictBox, CityBox, null);
            _fillingCombos = false;
        }

        private void CityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingCombos) return;
            _fillingCombos = true;
            FillChildCombo(DistrictBox, CityBox, null);
            _fillingCombos = false;
        }

        /// <summary>表單選到的最深一層分類；連國家都沒有時為 null。</summary>
        private string? FormCategoryId =>
            new[] { DistrictBox.SelectedValue, CityBox.SelectedValue, CountryBox.SelectedValue }
                .OfType<string>().FirstOrDefault(id => id.Length > 0);

        private Restaurant? ReadForm()
        {
            if (NameBox.Text.Trim().Length == 0)
            {
                MessageBox.Show("請輸入餐廳名稱。", "提示");
                NameBox.Focus();
                return null;
            }
            if (FormCategoryId is not { } categoryId)
            {
                MessageBox.Show("請先在左側新增一個國家，再選擇餐廳所在的分類。", "提示");
                return null;
            }
            return new Restaurant
            {
                Name = NameBox.Text.Trim(),
                CategoryId = categoryId,
                Address = AddressBox.Text.Trim(),
                Phone = PhoneBox.Text.Trim(),
                Rating = RatingBox.SelectedValue as int? ?? 0,
                Note = NoteBox.Text.Trim(),
            };
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var r = ReadForm();
            if (r == null) return;
            _data.Restaurants.Add(r);
            // 存到別的分類時，左側切到那個分類，才看得到剛新增的這間
            if (!CategoryService.RestaurantsUnder(_data, _selectedCategoryId).Contains(r))
                _selectedCategoryId = r.CategoryId;
            SearchBox.Clear();
            PersistAndRefresh();
            SelectRestaurant(r);
            StatusText.Text = $"已新增「{r.Name}」";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRestaurant is not { } selected)
            {
                MessageBox.Show("請先在清單中選一間餐廳，或按「新增」建立新的。", "提示");
                return;
            }
            var edited = ReadForm();
            if (edited == null) return;

            selected.Name = edited.Name;
            selected.CategoryId = edited.CategoryId;
            selected.Address = edited.Address;
            selected.Phone = edited.Phone;
            selected.Rating = edited.Rating;
            selected.Note = edited.Note;
            PersistAndRefresh();
            StatusText.Text = $"已儲存「{selected.Name}」";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRestaurant is not { } selected) return;
            var ok = MessageBox.Show($"確定要刪除「{selected.Name}」嗎？", "刪除餐廳",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;

            _data.Restaurants.Remove(selected);
            RestaurantList.SelectedItem = null;
            PersistAndRefresh();
            ClearForm();
            StatusText.Text = $"已刪除「{selected.Name}」";
        }

        private void SelectRestaurant(Restaurant r)
        {
            var row = RestaurantList.Items.OfType<RestaurantRow>().FirstOrDefault(x => x.Model == r);
            RestaurantList.SelectedItem = row;
            if (row != null) RestaurantList.ScrollIntoView(row);
        }

        private void OpenMap_Click(object sender, RoutedEventArgs e)
        {
            var query = $"{NameBox.Text.Trim()} {AddressBox.Text.Trim()}".Trim();
            if (query.Length == 0) return;
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query)) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show($"無法開啟瀏覽器：{ex.Message}", "地圖");
            }
        }

        // ---------- 存檔 / 重新整理 ----------

        private void PersistAndRefresh()
        {
            var formCategory = FormCategoryId;
            try
            {
                DataStore.Save(_data);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"存檔失敗：{ex.Message}", "存檔", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RebuildCategories();
            RefreshList();
            // 分類可能被改名 / 刪除，下拉選單跟著更新：選取中的餐廳以它的分類為準，否則保留原本選的
            if (SelectedRestaurant is { } r) SetFormCategory(r.CategoryId);
            else SetFormCategory(CategoryService.Get(_data, formCategory) != null ? formCategory : _selectedCategoryId);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            // 視窗標題列顯示版本：安裝版為「餐廳收藏 v1.0.0」，直接從 Visual Studio 執行則標示開發版
            Title = _updater.IsInstalled ? $"餐廳收藏 v{_updater.CurrentVersion}" : "餐廳收藏（開發版）";
            int countries = CategoryService.Children(_data, null).Count();
            CountText.Text = $"{_data.Restaurants.Count} 間餐廳 · {countries} 個國家";
            StatusText.Text = $"版本 {_updater.CurrentVersion} · 資料存在 {DataStore.DataDirectory}";
        }

        // ---------- Excel 匯出 / 匯入 ----------

        private const string ExcelFilter = "Excel 活頁簿 (*.xlsx)|*.xlsx";

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = ExcelFilter, FileName = $"餐廳收藏-{DateTime.Now:yyyyMMdd}.xlsx" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ExcelService.Export(_data, dlg.FileName);
                MessageBox.Show("匯出完成。", "匯出 Excel");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯出失敗：{ex.Message}", "匯出 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = ExcelFilter };
            if (dlg.ShowDialog() != true) return;

            List<ImportRow> rows;
            try
            {
                rows = ExcelService.Import(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"匯入失敗：{ex.Message}", "匯入 Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("檔案裡沒有可以匯入的資料。\n第一列是標題，第一欄（國家）必須有值。", "匯入 Excel");
                return;
            }

            int restaurants = rows.Count(r => r.Restaurant != null);
            var mode = MessageBox.Show(
                $"檔案裡有 {restaurants} 間餐廳。\n\n「是」：加到目前的資料裡（同分類同名的餐廳會略過）\n「否」：清空目前所有資料，改用檔案內容\n「取消」：不匯入",
                "匯入 Excel", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (mode == MessageBoxResult.Cancel) return;
            if (mode == MessageBoxResult.No &&
                MessageBox.Show("確定要清空目前所有分類與餐廳嗎？這個動作無法復原。", "匯入 Excel",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            int added = ExcelService.Apply(_data, rows, replace: mode == MessageBoxResult.No);
            foreach (var country in CategoryService.Children(_data, null)) _expanded.Add(country.Id);
            RestaurantList.SelectedItem = null;
            PersistAndRefresh();
            ClearForm();
            MessageBox.Show($"匯入完成，新增了 {added} 間餐廳。", "匯入 Excel");
        }
    }
}
