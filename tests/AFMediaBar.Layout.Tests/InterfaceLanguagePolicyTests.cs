using System.Globalization;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 界面语言解析的三条断言。
///
/// 为什么只留这一份：「跟随系统」在台式机上要切换 Windows 显示语言才能看到结果，属于打开界面也验证不了的行为；
/// 文案翻译对不对、有没有漏一条，打开程序看一眼就知道，因此那些不需要自动化。
/// Three assertions about interface-language resolution.
///
/// Why only this file survives: seeing what "follow the system" does requires switching the Windows display language, which is
/// behaviour no amount of looking at the interface can verify, while whether a translation is right or missing is visible the
/// moment the application is opened and therefore needs no automation.
/// </summary>
[TestClass]
public sealed class InterfaceLanguagePolicyTests
{
    [TestMethod]
    public void AnExplicitChoiceIgnoresTheSystemLanguage()
    {
        foreach (var culture in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" })
        {
            var systemCulture = CultureInfo.GetCultureInfo(culture);
            Assert.AreEqual(LocalizationLanguage.SimplifiedChinese, InterfaceLanguagePolicy.Resolve(InterfaceLanguage.SimplifiedChinese, systemCulture), culture);
            Assert.AreEqual(LocalizationLanguage.TraditionalChinese, InterfaceLanguagePolicy.Resolve(InterfaceLanguage.TraditionalChinese, systemCulture), culture);
            Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.Resolve(InterfaceLanguage.English, systemCulture), culture);
        }
    }

    [TestMethod]
    public void FollowSystemSplitsChineseAndFallsBackToEnglishOtherwise()
    {
        foreach (var culture in new[] { "zh", "zh-CN", "zh-Hans", "zh-SG" })
        {
            Assert.AreEqual(LocalizationLanguage.SimplifiedChinese, InterfaceLanguagePolicy.ResolveSystem(CultureInfo.GetCultureInfo(culture)), culture);
        }

        foreach (var culture in new[] { "zh-TW", "zh-HK", "zh-MO", "zh-Hant" })
        {
            Assert.AreEqual(LocalizationLanguage.TraditionalChinese, InterfaceLanguagePolicy.ResolveSystem(CultureInfo.GetCultureInfo(culture)), culture);
        }

        // 只带三种语言时，不支持的语言必须回到英文：留空或退回简体中文都会让不懂中文的用户无从下手。
        // With only three languages available an unsupported one has to fall back to English: a blank interface or a drop to
        // simplified Chinese would strand a user who reads neither.
        Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.ResolveSystem(CultureInfo.GetCultureInfo("en-US")));
        Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.ResolveSystem(CultureInfo.GetCultureInfo("ja-JP")));
        Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.ResolveSystem(null));
        Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.ResolveSystem(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void AStoredValueThatIsUnknownIsTreatedAsFollowTheSystem()
    {
        // 设置文件里出现不认识的取值时界面必须还能开出来，而不是抛异常或留空；成员值参与序列化，因此也不得重排。
        // An unrecognized value in the settings file must still leave a usable interface instead of throwing or going blank, and
        // since the member values are serialized they may not be reordered either.
        Assert.AreEqual(LocalizationLanguage.English, InterfaceLanguagePolicy.Resolve((InterfaceLanguage)42, CultureInfo.GetCultureInfo("en-US")));
        Assert.AreEqual(InterfaceLanguage.System, new AppSettings().InterfaceLanguage);
        Assert.AreEqual(InterfaceLanguage.System, new AppSettings { InterfaceLanguage = (InterfaceLanguage)42 }.Normalize().InterfaceLanguage);
        Assert.AreEqual(0, (int)InterfaceLanguage.System);
        Assert.AreEqual(1, (int)InterfaceLanguage.SimplifiedChinese);
        Assert.AreEqual(2, (int)InterfaceLanguage.TraditionalChinese);
        Assert.AreEqual(3, (int)InterfaceLanguage.English);
    }
}
