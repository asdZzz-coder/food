using System.IO;
using System.Text.Json;
using food.Models;

namespace food.Services
{
    /// <summary>
    /// 把分類與餐廳存成 %AppData%\FoodKeeper\data.json。
    /// 換電腦時請用「匯出 Excel → 匯入」搬資料，或直接複製 data.json。
    /// </summary>
    public static class DataStore
    {
        // 環境變數 FOODKEEPER_DATA_DIR 可指定其他資料夾（測試用）；平常不設定，存在 %AppData%\FoodKeeper
        public static readonly string DataDirectory =
            Environment.GetEnvironmentVariable("FOODKEEPER_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FoodKeeper");

        private static string FilePath => Path.Combine(DataDirectory, "data.json");

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文直接存成中文，方便閱讀
        };

        public static FoodData Load() => Load(FilePath);

        /// <summary>第一次使用（還沒有資料檔）時放幾個範例分類；檔案壞掉時備份後以空資料開始。</summary>
        internal static FoodData Load(string path)
        {
            if (!File.Exists(path)) return CategoryService.Sample();
            try
            {
                var data = JsonSerializer.Deserialize<FoodData>(File.ReadAllText(path), Options) ?? new();
                data.Categories ??= new();
                data.Restaurants ??= new();
                return data;
            }
            catch (JsonException)
            {
                File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                return new();
            }
        }

        public static void Save(FoodData data) => Save(FilePath, data);

        internal static void Save(string path, FoodData data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
