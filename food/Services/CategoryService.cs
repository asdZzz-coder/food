using food.Models;

namespace food.Services
{
    public enum CategoryNameError { None, Empty, TooLong, Duplicate }

    /// <summary>刪除分類的結果：刪掉幾個分類、幾間餐廳移到上一層（MovedTo）、幾間餐廳一起刪除。</summary>
    public record DeleteResult(int Categories, int Moved, int Deleted, Category? MovedTo);

    /// <summary>
    /// 三層分類（國家 › 縣市 › 地區）的規則，不含任何畫面，方便自動測試。
    /// - 同一層、同一個上層底下不能有同名分類（不分大小寫）。
    /// - 刪除分類時底下的子分類一起刪除；底下的餐廳移到上一層，刪除國家時餐廳一併刪除。
    /// </summary>
    public static class CategoryService
    {
        public static readonly string[] LevelNames = { "國家", "縣市", "地區" };
        public const int MaxLevel = 2;
        public const int MaxNameLength = 40;

        /// <summary>第一次使用時的範例分類。</summary>
        public static FoodData Sample()
        {
            var data = new FoodData();
            var tw = Add(data, null, "台灣");
            var tp = Add(data, tw.Id, "台北市");
            Add(data, tp.Id, "大安區");
            Add(data, tp.Id, "信義區");
            return data;
        }

        // ---------- 查詢 ----------

        public static Category? Get(FoodData data, string? id) =>
            id == null ? null : data.Categories.FirstOrDefault(c => c.Id == id);

        /// <summary>某分類底下的子分類（依新增順序）；parentId 為 null 時是所有國家。</summary>
        public static IEnumerable<Category> Children(FoodData data, string? parentId) =>
            data.Categories.Where(c => c.ParentId == parentId);

        /// <summary>從國家到這個分類的路徑；id 為 null 或找不到時是空清單。</summary>
        public static List<Category> PathOf(FoodData data, string? id)
        {
            var path = new List<Category>();
            var seen = new HashSet<string>();
            for (var c = Get(data, id); c != null && seen.Add(c.Id); c = Get(data, c.ParentId))
                path.Insert(0, c);
            return path;
        }

        /// <summary>0 = 國家、1 = 縣市、2 = 地區。</summary>
        public static int Level(FoodData data, string id) => Math.Max(PathOf(data, id).Count - 1, 0);

        public static string LevelName(int level) => LevelNames[Math.Clamp(level, 0, MaxLevel)];

        public static string PathText(FoodData data, string? id) =>
            string.Join(" › ", PathOf(data, id).Select(c => c.Name));

        /// <summary>這個分類本身加上底下所有子分類的 Id。</summary>
        public static HashSet<string> DescendantIds(FoodData data, string id)
        {
            var ids = new HashSet<string> { id };
            var queue = new Queue<string>(ids);
            while (queue.Count > 0)
                foreach (var child in Children(data, queue.Dequeue()))
                    if (ids.Add(child.Id)) queue.Enqueue(child.Id);
            return ids;
        }

        /// <summary>這個分類（含子分類）裡的餐廳；id 為 null 時是全部餐廳。</summary>
        public static IEnumerable<Restaurant> RestaurantsUnder(FoodData data, string? id)
        {
            if (id == null) return data.Restaurants;
            var ids = DescendantIds(data, id);
            return data.Restaurants.Where(r => ids.Contains(r.CategoryId));
        }

        // ---------- 新增 / 重新命名 / 刪除 ----------

        public static string Normalize(string? name) => (name ?? "").Trim();

        public static CategoryNameError Validate(FoodData data, string? parentId, string name, string? exceptId = null)
        {
            name = Normalize(name);
            if (name.Length == 0) return CategoryNameError.Empty;
            if (name.Length > MaxNameLength) return CategoryNameError.TooLong;
            if (Children(data, parentId).Any(c => c.Id != exceptId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                return CategoryNameError.Duplicate;
            return CategoryNameError.None;
        }

        /// <summary>在 parentId 底下新增分類（parentId 為 null 時新增國家）。</summary>
        public static Category Add(FoodData data, string? parentId, string name)
        {
            if (parentId != null)
            {
                if (Get(data, parentId) == null) throw new ArgumentException("找不到上層分類", nameof(parentId));
                if (Level(data, parentId) >= MaxLevel) throw new InvalidOperationException("地區底下不能再新增分類");
            }
            if (Validate(data, parentId, name) != CategoryNameError.None)
                throw new ArgumentException("分類名稱不正確", nameof(name));
            var category = new Category { Name = Normalize(name), ParentId = parentId };
            data.Categories.Add(category);
            return category;
        }

        public static void Rename(FoodData data, string id, string name)
        {
            var category = Get(data, id) ?? throw new ArgumentException("找不到分類", nameof(id));
            if (Validate(data, category.ParentId, name, id) != CategoryNameError.None)
                throw new ArgumentException("分類名稱不正確", nameof(name));
            category.Name = Normalize(name);
        }

        /// <summary>還沒真的刪除前，先算出刪除後會影響多少東西（用來顯示確認訊息）。</summary>
        public static DeleteResult Preview(FoodData data, string id)
        {
            var category = Get(data, id) ?? throw new ArgumentException("找不到分類", nameof(id));
            var ids = DescendantIds(data, id);
            int restaurants = RestaurantsUnder(data, id).Count();
            var parent = Get(data, category.ParentId);
            return parent != null
                ? new DeleteResult(ids.Count, restaurants, 0, parent)
                : new DeleteResult(ids.Count, 0, restaurants, null);
        }

        public static DeleteResult Delete(FoodData data, string id)
        {
            var result = Preview(data, id);
            var ids = DescendantIds(data, id);
            data.Categories.RemoveAll(c => ids.Contains(c.Id));
            if (result.MovedTo != null)
            {
                foreach (var r in data.Restaurants.Where(r => ids.Contains(r.CategoryId)))
                    r.CategoryId = result.MovedTo.Id;
            }
            else
            {
                data.Restaurants.RemoveAll(r => ids.Contains(r.CategoryId));
            }
            return result;
        }

        // ---------- 匯入用 ----------

        /// <summary>
        /// 依名稱找出（沒有就建立）國家 › 縣市 › 地區，回傳最深那一層的 Id。
        /// 遇到空白的那一層就停（例如只填國家和縣市）；連國家都沒填則回傳 null。
        /// </summary>
        public static string? EnsurePath(FoodData data, IEnumerable<string?> names)
        {
            string? parentId = null;
            foreach (var raw in names.Take(MaxLevel + 1))
            {
                var name = Normalize(raw);
                if (name.Length == 0) break;
                if (name.Length > MaxNameLength) name = name[..MaxNameLength];
                var existing = Children(data, parentId).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                parentId = (existing ?? Add(data, parentId, name)).Id;
            }
            return parentId;
        }
    }
}
