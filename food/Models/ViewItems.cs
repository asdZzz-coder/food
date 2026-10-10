using System.Windows;
using System.Windows.Media;
using food.Services;

namespace food.Models
{
    /// <summary>左側分類清單的一列（「全部餐廳」或某個國家 / 縣市 / 地區），只供畫面使用。</summary>
    public class CategoryNode
    {
        /// <summary>null 表示「全部餐廳」。</summary>
        public string? Id { get; init; }
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public int Count { get; init; }
        public bool HasChildren { get; init; }
        public bool IsExpanded { get; init; }

        public bool IsCategory => Id != null;
        public bool ShowArrowColumn => IsCategory;
        public bool CanAddChild => IsCategory && Level < CategoryService.MaxLevel;
        public string AddTip => CanAddChild ? "新增" + CategoryService.LevelName(Level + 1) : "";
        public string Tooltip => IsCategory ? $"{Name}（{CategoryService.LevelName(Level)}）" : Name;
        public Thickness Indent => new(IsCategory ? Level * 16 : 0, 0, 0, 0);

        // Segoe Fluent Icons：箭頭（右 / 下）、全部（清單）、國家（地球）、縣市（城市）、地區（圖釘）
        public string Arrow => IsExpanded ? "\uE70D" : "\uE76C";
        public string Icon => !IsCategory ? "\uE8FD" : Level switch { 0 => "\uE774", 1 => "\uE80F", _ => "\uE707" };

        public override string ToString() => Name;
    }

    /// <summary>中間餐廳清單的一列，只供畫面使用。</summary>
    public class RestaurantRow
    {
        public required Restaurant Model { get; init; }
        public required string PathText { get; init; }

        public string Name => Model.Name;

        /// <summary>第三行：地址、電話（都沒有時隱藏）。</summary>
        public string Detail => string.Join("  ·  ", new[] { Model.Address, Model.Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        public bool HasDetail => Detail.Length > 0;
        public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);

        /// <summary>右上角的評分標籤（未評分時隱藏）。</summary>
        public bool HasRating => Model.Rating > 0;
        public string RatingText => Model.Rating.ToString();

        public string Initial => string.IsNullOrWhiteSpace(Model.Name)
            ? "?"
            : Model.Name.Trim().EnumerateRunes().First().ToString().ToUpperInvariant();

        private static readonly string[] AvatarColors =
        {
            "#EA580C", "#DC2626", "#D97706", "#16A34A", "#0891B2",
            "#2563EB", "#7C3AED", "#DB2777", "#65A30D", "#0D9488",
        };

        /// <summary>依餐廳名稱固定挑一個頭像底色（同名永遠同色）。</summary>
        private string AvatarColor
        {
            get
            {
                uint h = 2166136261; // FNV-1a，跨次啟動結果一致（string.GetHashCode 每次啟動都不同）
                foreach (var c in Model.Name.Trim().ToUpperInvariant()) { h ^= c; h *= 16777619; }
                return AvatarColors[h % (uint)AvatarColors.Length];
            }
        }

        /// <summary>頭像底色：同一色系由亮到暗的斜向漸層。</summary>
        public Brush AvatarBrush
        {
            get
            {
                var c = (Color)ColorConverter.ConvertFromString(AvatarColor);
                var dark = Color.FromRgb((byte)(c.R * 0.78), (byte)(c.G * 0.78), (byte)(c.B * 0.78));
                var brush = new LinearGradientBrush(c, dark, new Point(0, 0), new Point(1, 1));
                brush.Freeze();
                return brush;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>下拉選單的選項。Id 為空字串表示「不指定」。</summary>
    public record CategoryOption(string Id, string Display)
    {
        public override string ToString() => Display; // 下拉選單收合時顯示的文字
    }

}
