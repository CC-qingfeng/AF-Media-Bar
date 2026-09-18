using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AFMediaBar.Classes.Models.Credits;

namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 名单的本地缓存：把上一次取到的两份名单写在 <c>%LOCALAPPDATA%\AFMediaBar\cache\credits.json</c>。
/// The local cache for both lists, written to <c>%LOCALAPPDATA%\AFMediaBar\cache\credits.json</c>.
///
/// 缓存文件刻意沿用来源的结构（<c>contributors</c> 数组与 <c>sponsors</c> 数组），因此读回时用的是同一个解析器：
/// 再写第二套结构，就意味着第二套解析规则，而两套规则迟早会不一致。
/// The cache file deliberately keeps the same shape as the sources — a <c>contributors</c> array and a <c>sponsors</c> array — so reading it back
/// goes through the very same parser: a second shape would mean a second set of parsing rules, and the two would eventually disagree.
/// </summary>
public sealed class CreditsCacheStore
{
    private readonly string _directoryPath;
    private readonly string _filePath;

    /// <summary>
    /// 创建缓存存储。
    /// Creates the cache store.
    /// </summary>
    /// <param name="directoryPath">缓存目录；省略时取 <c>%LOCALAPPDATA%\AFMediaBar\cache</c>。/ Cache directory, defaulting to <c>%LOCALAPPDATA%\AFMediaBar\cache</c>.</param>
    public CreditsCacheStore(string? directoryPath = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AFMediaBar",
            "cache");
        _filePath = Path.Combine(_directoryPath, "credits.json");
    }

    /// <summary>缓存文件路径（诊断用）。/ The cache file path, for diagnostics.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 读回缓存；文件不存在、损坏或没有时间戳时返回 null（缓存坏了只意味着"当作没有缓存"）。
    /// Reads the cache back, returning null when the file is missing, damaged, or carries no timestamp: a broken cache only means "behave as if
    /// there were none".
    /// </summary>
    /// <returns>缓存内容与它的时间戳，或 null。/ The cached content and its timestamp, or null.</returns>
    public CachedCredits? TryRead()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("fetchedUtc", out var fetchedElement) ||
                fetchedElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(fetchedElement.GetString(), out var fetchedUtc))
            {
                return null;
            }

            var contributors = CreditsJsonParser.ParseContributors(json);
            var sponsors = CreditsJsonParser.ParseSponsors(json);
            if (!contributors.Succeeded && !sponsors.Succeeded)
            {
                return null;
            }

            return new CachedCredits(
                contributors.Contributors ?? [],
                sponsors.Sponsors ?? [],
                fetchedUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"[Credits] Cache unreadable: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 写入缓存：先写临时文件再替换，读取方不会看到写了一半的文件。
    /// Writes the cache: a temporary file is written first and then replaces the target, so a reader never sees a half-written file.
    /// </summary>
    /// <param name="contributors">贡献者。/ Contributors.</param>
    /// <param name="sponsors">赞助者。/ Sponsors.</param>
    /// <param name="fetchedUtc">写入时间。/ When the data was written.</param>
    /// <returns>是否写入成功。/ Whether the write succeeded.</returns>
    public bool TryWrite(
        IReadOnlyList<ContributorInfo> contributors,
        IReadOnlyList<SponsorInfo> sponsors,
        DateTimeOffset fetchedUtc)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);

            // 结构说明：contributors 用接口的字段名（login/contributions/html_url/avatar_url），这样同一份内容
            // 既可以被接口解析器读，也可以被快照解析器读。
            // Shape note: contributors uses the API's field names (login/contributions/html_url/avatar_url) so that the same content can be read by
            // both the API parser and the snapshot parser.
            var payload = new
            {
                schemaVersion = 1,
                fetchedUtc = fetchedUtc.ToString("O"),
                contributors = contributors.Select(contributor => new
                {
                    login = contributor.Login,
                    contributions = contributor.Contributions,
                    html_url = contributor.ProfileUrl,
                    avatar_url = contributor.AvatarUrl
                }),
                sponsors = sponsors.Select(sponsor => new
                {
                    name = sponsor.Name,
                    tier = sponsor.Tier,
                    note = sponsor.Note,
                    url = sponsor.Url,
                    since = sponsor.Since
                })
            };

            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload), Encoding.UTF8);
            File.Move(temp, _filePath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 写缓存失败不影响本次展示，只是下次还要再取一次。
            // A failed cache write does not affect this display; it only means the next run fetches again.
            Debug.WriteLine($"[Credits] Cache write failed: {exception.Message}");
            return false;
        }
    }
}

/// <summary>
/// 缓存里读回的内容。
/// What was read back from the cache.
/// </summary>
/// <param name="Contributors">贡献者。/ Contributors.</param>
/// <param name="Sponsors">赞助者。/ Sponsors.</param>
/// <param name="FetchedUtc">写入时间。/ When it was written.</param>
public sealed record CachedCredits(
    IReadOnlyList<ContributorInfo> Contributors,
    IReadOnlyList<SponsorInfo> Sponsors,
    DateTimeOffset FetchedUtc);
