using System.Text.Json.Serialization;
using Newtonsoft.Json;
using vkbot_vitalya.Config;
using vkbot_vitalya.Core;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace vkbot_vitalya.Services;

public class SafebooruApi {
    private const string MasterUrl = "https://safebooru.org/";
    private static readonly Random Rand = new();
    public static readonly Dictionary<string, TagCacheEntry> TagsCache;
    public static readonly Dictionary<long, MediaCacheEntry> MediaCache;

    static SafebooruApi() {
        if (!File.Exists("safebooru_tags_cache.json"))
            File.WriteAllText("safebooru_tags_cache.json", "{}");
        
        var text1 = File.ReadAllText("safebooru_tags_cache.json");
        TagsCache = JsonConvert.DeserializeObject<Dictionary<string, TagCacheEntry>>(text1) ?? [];

        if (!File.Exists("safebooru_cache.json"))
            File.WriteAllText("safebooru_cache.json", "{}");

        var text2 = File.ReadAllText("safebooru_cache.json");
        MediaCache = JsonConvert.DeserializeObject<Dictionary<long, MediaCacheEntry>>(text2) ?? [];
    }
    
    public SafebooruApi() {
        // HttpClient = ProxyClient.GetProxyHttpClient(Auth.Instance.ProxyAdress,
        //     new NetworkCredential(Auth.Instance.ProxyLogin, Auth.Instance.ProxyPassword), "vk-bot-vitalya");
        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"vk-bot-vitalya/1.0 (compatible; vk-bot-vitalya/1.0; +http://vk-bot-vitalya.com)");

    }

    public HttpClient HttpClient { get; set; }
    
    private static readonly string[] AllowedFormats = ["jpg", "jpeg", "png", "gif", "webp"]; /* webm, mp4 */
    
    public async Task<(SafebooruPost?, string?)> RandomPostAsync(string tagsString = "") {
        var attempts = 1;

        string[] alwaysExclude = [];
        var tags = tagsString.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString).ToList();

        // Исключаем нерабочие теги
        if (tags.Intersect(alwaysExclude).Any()) 
            return (null, "Ничего нет с такими тегами");

        // Исключаем запрещенные теги
        if (Conf.Instance.UseForbiddenTags && tags.Intersect(Conf.Instance.ForbiddenTags).Any())
            return (null, "Я эту хуйню искать не буду");
        
        // todo Узнаем количество постов с каждым тегом
        var postCount = 5; //await GetPostCountAsync(tags);
        if (postCount == 0) {
            L.I("No posts found.");
            return (null, "");
        }

        for (var attempt = 0; attempt < attempts; attempt++) {
            
            var page = Rand.Next(0, (postCount - 1) / 200 + 1);
            
            var url = MasterUrl + $"index.php?page=dapi&s=post&q=index&limit=200&json=1&pid={page}&tags={tagsString}";

            // Задержка перед выполнением запроса
            // await Task.Delay(1000);

            using var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();

            // Improved check for search error
            if (string.IsNullOrWhiteSpace(responseBody) || responseBody.Contains("search error")) {
                L.E("Search error detected or response body is empty.");
                return (null, "search error");
            }

            var posts = JsonSerializer.Deserialize<List<SafebooruPost>>(responseBody);

            if (posts is not { Count: > 0 }) {
                L.I("No posts found.");
                return (null, "No posts found");
            }

            L.I($"Posts found: {posts.Count}");

            var randomIndex = Rand.Next(posts.Count);
            var post = posts[randomIndex];
            L.I($"Tags: {post.TagString}");

            if (Conf.Instance.UseForbiddenTags && Conf.Instance.ForbiddenTags.Any(tag => post.TagString.Contains(tag))) {
                // Попался запрещенный тег
                L.I("Got forbidden tag. Retrying");
                continue;
            }

            if (post.FileUrl == null) {
                /*
                if (post.TagString.Split(' ').Intersect(alwaysExclude).Any())
                    L.I("Got loli or shota in tags. Retrying");
                else if (post.IsDeleted)
                    L.I("Got deleted post");
                else
                    L.E("post.fileUrl is null");
                */
                L.E("post.fileUrl is null");
                continue;
            }

            if (AllowedFormats.Any(s => post.FileUrl.EndsWith(s)))
                return (post, null);
        }

        return (null, "Извините, не удалось найти изображение аниме");
    }

    public async Task<int> GetPostCountAsync(string tags) {
        var incTags = tags.Replace(",", " ");
        var url = MasterUrl + $"index.php?page=dapi&s=post&q=index&limit=1&json=1&tags={incTags}";

        // Задержка перед выполнением запроса
        await Task.Delay(1000);
        HttpResponseMessage response;
        try {
            response = await HttpClient.GetAsync(url);
        } catch (HttpRequestException e) {
            L.E("", e);
            return 0;
        }

        if (response.IsSuccessStatusCode) {
            var responseBody = await response.Content.ReadAsStringAsync();
            var posts = JsonSerializer.Deserialize<List<SafebooruPost>>(responseBody);
            if (posts != null && posts.Count > 0) {
                return posts[0].Id;
            }
        }

        return 0;
    }
    
    public static void SaveCache() {
        var text = JsonConvert.SerializeObject(TagsCache);
        File.WriteAllText("safebooru_tags_cache.json", text);
        text = JsonConvert.SerializeObject(MediaCache);
        File.WriteAllText("safebooru_cache.json", text);
    }
    
    public class TagCacheEntry(int count, int[] order) {
        public readonly int count = count;
        public readonly int[] order = order;
        public int i = 0;
    }

    public record MediaCacheEntry(
        long VkOwnerId,
        long VkMediaId,
        string VkAccessKey,
        string VkUrl,
        string Path) {
    }
}

public class SafebooruPost {
    public SafebooruPost(string fileUrl, int id) {
        Id = id;
        FileUrl = fileUrl;
    }

    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("file_url")] public string FileUrl { get; set; }
    [JsonPropertyName("tags")] public string TagString { get; set; }
}