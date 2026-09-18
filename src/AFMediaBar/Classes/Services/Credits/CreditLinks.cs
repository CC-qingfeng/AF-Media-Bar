using AFMediaBar.Classes.Models.Credits;

namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 关于页用到的所有外部地址与包内资源路径，集中一处。
/// Every external address and in-pack asset path the about page uses, in one place.
///
/// 集中在这里的理由是"改地址时不会漏"：仓库地址、议题地址、赞助入口与两份名单的端点此前散落在 XAML、
/// 更新策略与计划文档里，任何一处漏改都会让界面指向旧地址。
/// They live together so that changing an address cannot miss one: the repository, the issue tracker, the support entries, and the
/// endpoints of both lists used to be spread across XAML, the update policy, and planning documents, and missing one left the
/// interface pointing at an old address.
/// </summary>
public static class CreditLinks
{
    /// <summary>仓库主页。/ The repository home page.</summary>
    public const string RepositoryUrl = "https://github.com/Fervent-Tempo/AF-Media-Bar";

    /// <summary>议题页。/ The issue tracker.</summary>
    public const string IssuesUrl = "https://github.com/Fervent-Tempo/AF-Media-Bar/issues";

    /// <summary>仓库所有者的 GitHub 账号。/ The GitHub account that owns the repository.</summary>
    public const string RepositoryOwner = "Fervent-Tempo";

    /// <summary>仓库名。/ The repository name.</summary>
    public const string RepositoryName = "AF-Media-Bar";

    /// <summary>默认分支名。/ The default branch name.</summary>
    public const string DefaultBranch = "main";

    /// <summary>
    /// 爱发电赞助页。
    ///
    /// ⚠️ 待用户补充：填上之后「赞助我」里的那一条会立即生效，空字符串时页面显示一行"待补充"提示而不是一个死链。
    /// ⚠️ Still to be supplied by the user: once filled in, that entry in "support me" takes effect immediately; while it is an
    /// empty string the page shows a "to be supplied" note instead of a dead link.
    /// </summary>
    public const string AfdianUrl = "https://ifdian.net/a/amorfate";

    /// <summary>微信收款码的包内路径（用户后续放入 <c>Assets/Sponsor/</c>）。/ Pack path of the WeChat payment code, to be placed in <c>Assets/Sponsor/</c> later.</summary>
    public const string WeChatQrCodeAssetPath = "Assets/Sponsor/wechat-pay.png";

    /// <summary>支付宝收款码的包内路径。/ Pack path of the Alipay payment code.</summary>
    public const string AlipayQrCodeAssetPath = "Assets/Sponsor/alipay-pay.png";

    /// <summary>
    /// 「赞助我」的条目，按显示顺序：两个收款码 + 一个爱发电链接。
    /// The "support me" entries in display order: two payment codes and one Afdian link.
    /// </summary>
    public static IReadOnlyList<SupportEntry> SupportEntries { get; } =
    [
        new("About.Support.WeChat", WeChatQrCodeAssetPath, string.Empty),
        new("About.Support.Alipay", AlipayQrCodeAssetPath, string.Empty),
        new("About.Support.Afdian", string.Empty, AfdianUrl)
    ];

    /// <summary>界面上展示的仓库地址（与项目文件里的 <c>RepositoryUrl</c> 一致）。/ The repository address shown on the interface, matching <c>RepositoryUrl</c> in the project file.</summary>
    public static string RepositoryDisplayUrl => RepositoryUrl;
}
